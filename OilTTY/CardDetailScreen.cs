internal enum CardDetailPane
{
    Description,
    Options
}

internal enum CardDetailMainTab
{
    Description,
    Comments
}

internal enum CardDetailField
{
    Title,
    Description,
    Comments,
    Tags,
    BoardColumn,
    CardType,
    AssignedUser,
    Slick,
    ExternalUrl
}

internal sealed record AssigneeChoice(int? UserId, string DisplayName);

internal sealed record SlickChoice(int? Id, string? Name, SlickDefinition? Definition);

internal enum CardDetailCommand
{
    Close,
    Quit,
    Save,
    LoadComments,
    PostComment
}

internal sealed class CardDetailScreen : ITerminalScreen<CardDetailCommand>
{
    private static readonly CardDetailField[] FocusOrder =
    [
        CardDetailField.Title,
        CardDetailField.Description,
        CardDetailField.Tags,
        CardDetailField.BoardColumn,
        CardDetailField.CardType,
        CardDetailField.AssignedUser,
        CardDetailField.Slick,
        CardDetailField.ExternalUrl
    ];

    private readonly CardDetailLayoutEngine _layoutEngine = new();
    private readonly CardDetailRenderer _renderer = new();
    private readonly string _status;
    private readonly bool _isNew;
    private readonly CardDraft? _initialDraft;
    private BoardData _data;
    private BoardCard _card;
    private CardDetailPane _activePane = CardDetailPane.Description;
    private CardDetailMainTab _mainTab = CardDetailMainTab.Description;
    private CardDetailField _focusedField = CardDetailField.Description;
    private CardDetailField? _editingField;
    private CardDraft? _draft;
    private int _descriptionScroll;
    private int _commentsScroll;
    private int _optionsScroll;
    private bool _ensureFocusedFieldVisible;
    private MultilineTextEditor? _editor;
    private InlineMultiChoicePicker<CardTag>? _tagPicker;
    private InlineChoicePicker<BoardColumn>? _columnPicker;
    private InlineChoicePicker<CardTypeDefinition>? _cardTypePicker;
    private InlineChoicePicker<AssigneeChoice>? _assigneePicker;
    private InlineChoicePicker<SlickChoice>? _slickPicker;
    private string? _feedback;
    private bool _feedbackIsError;
    private bool _confirmingDiscard;
    private IReadOnlyList<CardComment>? _comments;
    private bool _commentsLoading;
    private bool _commentsLoadFailed;
    private string? _commentDraft;

    public CardDetailScreen(BoardData data, BoardCard card, string status)
    {
        _data = data;
        _card = card;
        _status = status;
    }

    public CardDetailScreen(BoardData data, CardDraft draft, string status)
    {
        _data = data;
        _card = CreateNewCardPreview(data, draft);
        _status = status;
        _isNew = true;
        _initialDraft = draft;
        _draft = draft;
        _activePane = CardDetailPane.Description;
        _focusedField = CardDetailField.Title;
        _editingField = CardDetailField.Title;
        _editor = new MultilineTextEditor(
            draft.Title,
            allowNewLines: false,
            maximumLength: 200,
            cursorAtEnd: true);
    }

    public int DescriptionScroll => _descriptionScroll;

    public int OptionsScroll => _optionsScroll;

    public int CommentsScroll => _commentsScroll;

    public CardDetailPane ActivePane => _activePane;

    public CardDetailMainTab MainTab => _mainTab;

    public CardDetailField FocusedField => _focusedField;

    public CardDetailField? EditingField => _editingField;

    public bool IsEditing => _editor is not null || ActiveChoicePicker is not null;

    public bool HasDraft => _draft is not null;

    public bool IsNew => _isNew;

    public bool IsConfirmingDiscard => _confirmingDiscard;

    public bool HasUnsavedChanges => HasDirtyDraft() || HasDirtyCommentDraft();

    public string Title => _draft?.Title ?? _card.Title;

    public string Description => _draft?.Description ?? _card.Description;

    public IReadOnlyList<string> TagNames => _tagPicker is not null
        ? _tagPicker.Selected.Select(tag => tag.Name).ToArray()
        : _draft?.TagNames ?? _card.TagNames;

    public string? ExternalUrl => _draft?.ExternalUrl ?? _card.ExternalUrl;

    public int CardTypeId =>
        _cardTypePicker?.Highlighted.Id ?? _draft?.CardTypeId ?? _card.CardTypeId;

    public int BoardColumnId =>
        _columnPicker?.Highlighted.Id ?? _draft?.BoardColumnId ?? _card.BoardColumnId;

    public int? AssignedUserId => _assigneePicker is not null
        ? _assigneePicker.Highlighted.UserId
        : _draft is not null ? _draft.AssignedUserId : _card.AssignedUserId;

    public string? SlickName => _slickPicker is not null
        ? _slickPicker.Highlighted.Name
        : _draft is not null ? _draft.SlickName : _card.SlickName;

    public CardDraft? PendingDraft => _draft;

    public string? PendingCommentText => _commentDraft;

    public IReadOnlyList<CardComment>? Comments => _comments;

    public TerminalFrame Render(TerminalViewport viewport)
    {
        var displayCard = CreateDisplayCard();
        var layout = CreateLayout(displayCard, viewport);
        return _renderer.Render(
            _data,
            displayCard,
            layout,
            _activePane,
            _mainTab,
            _comments?.Count,
            _focusedField,
            _editingField,
            _tagPicker,
            _columnPicker,
            _cardTypePicker,
            _assigneePicker,
            _slickPicker,
            HasUnsavedChanges,
            _isNew,
            _confirmingDiscard,
            EffectiveDescriptionScroll(layout),
            EffectiveCommentsScroll(layout),
            EffectiveOptionsScroll(layout),
            _status,
            _feedback,
            _feedbackIsError);
    }

    public ScreenUpdate<CardDetailCommand> HandleKey(
        ConsoleKeyInfo key,
        TerminalViewport viewport)
    {
        if (BoardStyles.TryToggleTheme(key))
        {
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return ScreenUpdate<CardDetailCommand>.Complete(CardDetailCommand.Quit);
        }

        if (_confirmingDiscard)
        {
            return HandleDiscardConfirmationKey(key);
        }

        if (ActiveChoicePicker is not null)
        {
            return HandleChoicePickerKey(key, viewport);
        }

        if (_editor is not null)
        {
            return HandleEditorKey(key, viewport);
        }

        if (key.Key == ConsoleKey.Escape)
        {
            return DiscardOrClose();
        }

        if (key.Key == ConsoleKey.Q)
        {
            return DiscardOrClose();
        }

        if (key.Key == ConsoleKey.S && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return _focusedField == CardDetailField.Comments && HasDirtyCommentDraft()
                ? RequestPostComment(viewport)
                : RequestSave(viewport);
        }

        if (key.Key == ConsoleKey.Tab)
        {
            MoveFocus(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1);
            return ContinueOrLoadComments();
        }

        if (key.Key == ConsoleKey.LeftArrow)
        {
            if (_focusedField == CardDetailField.Comments)
            {
                Focus(CardDetailField.Description);
                return ScreenUpdate<CardDetailCommand>.Continue();
            }

            if (_focusedField is CardDetailField.Tags
                or CardDetailField.BoardColumn
                or CardDetailField.CardType
                or CardDetailField.AssignedUser
                or CardDetailField.Slick
                or CardDetailField.ExternalUrl)
            {
                Focus(_mainTab == CardDetailMainTab.Comments
                    ? CardDetailField.Comments
                    : CardDetailField.Description);
            }
            else
            {
                MoveFocus(-1, wrap: false);
            }

            return ContinueOrLoadComments();
        }

        if (key.Key == ConsoleKey.RightArrow)
        {
            if (!_isNew && _focusedField == CardDetailField.Description)
            {
                Focus(CardDetailField.Comments);
                return ContinueOrLoadComments();
            }

            if (_focusedField == CardDetailField.Comments)
            {
                Focus(CardDetailField.Tags);
                return ScreenUpdate<CardDetailCommand>.Continue();
            }

            MoveFocus(1, wrap: false);
            return ContinueOrLoadComments();
        }

        if (key.Key == ConsoleKey.UpArrow)
        {
            if (_focusedField == CardDetailField.Comments)
            {
                Focus(CardDetailField.Title);
                return ScreenUpdate<CardDetailCommand>.Continue();
            }

            MoveFocus(-1, wrap: false);
            return ContinueOrLoadComments();
        }

        if (key.Key == ConsoleKey.DownArrow)
        {
            if (_focusedField == CardDetailField.Comments)
            {
                Focus(CardDetailField.Tags);
                return ScreenUpdate<CardDetailCommand>.Continue();
            }

            MoveFocus(1, wrap: false);
            return ContinueOrLoadComments();
        }

        if (key.Key == ConsoleKey.Enter)
        {
            if (_focusedField == CardDetailField.Comments && _comments is null)
            {
                return RequestCommentsLoad();
            }

            BeginEditingFocusedField(viewport);
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        var layout = CreateLayout(CreateDisplayCard(), viewport);
        var delta = key.Key switch
        {
            ConsoleKey.J or ConsoleKey.DownArrow => 1,
            ConsoleKey.K or ConsoleKey.UpArrow => -1,
            ConsoleKey.PageDown => Math.Max(1, layout.PaneViewportRows - 1),
            ConsoleKey.PageUp => -Math.Max(1, layout.PaneViewportRows - 1),
            _ => 0
        };
        if (key.Key == ConsoleKey.Home)
        {
            return SetActiveScroll(0, layout);
        }

        if (key.Key == ConsoleKey.End)
        {
            return SetActiveScroll(ActiveMaximumScroll(layout), layout);
        }

        return delta == 0
            ? ScreenUpdate<CardDetailCommand>.Continue(redraw: false)
            : SetActiveScroll(ActiveScroll() + delta, layout);
    }

    public void BeginSaving()
    {
        _feedback = "Saving…";
        _feedbackIsError = false;
    }

    public void ApplySaved(BoardData data, BoardCard card)
    {
        _data = data;
        _card = card;
        _draft = null;
        _editor = null;
        _tagPicker = null;
        _columnPicker = null;
        _cardTypePicker = null;
        _assigneePicker = null;
        _slickPicker = null;
        _editingField = null;
        _feedback = "Saved.";
        _feedbackIsError = false;
    }

    public void SetSaveError(Exception exception)
    {
        _feedback = ResolveErrorMessage(exception, out var field);
        _feedbackIsError = true;
        if (field is CardDetailField errorField)
        {
            Focus(errorField);
        }
    }

    public void BeginLoadingComments()
    {
        _commentsLoading = true;
        _commentsLoadFailed = false;
        _feedback = "Loading comments…";
        _feedbackIsError = false;
    }

    public void ApplyComments(IReadOnlyList<CardComment> comments)
    {
        _comments = comments
            .OrderByDescending(comment => comment.PostedAtUtc)
            .ThenByDescending(comment => comment.Id)
            .ToArray();
        _commentsLoading = false;
        _commentsLoadFailed = false;
        _feedback = null;
        _feedbackIsError = false;
    }

    public void SetCommentsLoadError(Exception exception)
    {
        _commentsLoading = false;
        _commentsLoadFailed = true;
        _feedback = exception.Message;
        _feedbackIsError = true;
    }

    public void BeginPostingComment()
    {
        _feedback = "Posting comment…";
        _feedbackIsError = false;
    }

    public void ApplyPostedComment(BoardData data, BoardCard card, CardComment comment)
    {
        _data = data;
        _card = card;
        _comments = (_comments ?? [])
            .Where(existing => existing.Id != comment.Id)
            .Append(comment)
            .OrderByDescending(existing => existing.PostedAtUtc)
            .ThenByDescending(existing => existing.Id)
            .ToArray();
        _commentDraft = null;
        _editor = null;
        _editingField = null;
        _feedback = "Comment posted.";
        _feedbackIsError = false;
        _commentsScroll = 0;
    }

    public void SetCommentPostError(Exception exception)
    {
        _feedback = ResolveCommentErrorMessage(exception);
        _feedbackIsError = true;
        Focus(CardDetailField.Comments);
    }

    private ScreenUpdate<CardDetailCommand> HandleEditorKey(
        ConsoleKeyInfo key,
        TerminalViewport viewport)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            CommitEditor();
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (key.Key == ConsoleKey.S && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return _editingField == CardDetailField.Comments
                ? RequestPostComment(viewport)
                : RequestSave(viewport);
        }

        if (key.Key == ConsoleKey.Tab)
        {
            CommitEditor();
            MoveFocus(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1);
            return ContinueOrLoadComments();
        }

        if (key.Key == ConsoleKey.Enter
            && _editingField is not (CardDetailField.Description or CardDetailField.Comments))
        {
            CommitEditor();
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        var layout = CreateLayout(CreateDisplayCard(), viewport);
        var handled = false;
        if (_editingField is CardDetailField.Description or CardDetailField.Comments
            && key.Key is ConsoleKey.PageDown or ConsoleKey.PageUp)
        {
            var directionKey = key.Key == ConsoleKey.PageDown ? ConsoleKey.DownArrow : ConsoleKey.UpArrow;
            var direction = directionKey == ConsoleKey.DownArrow ? '\u2193' : '\u2191';
            for (var count = 0; count < Math.Max(1, layout.PaneViewportRows - 1); count++)
            {
                handled |= _editor!.HandleKey(Key(direction, directionKey), EditorDisplayWidth(layout));
            }
        }
        else
        {
            handled = _editor!.HandleKey(key, EditorDisplayWidth(layout));
        }

        if (!handled)
        {
            return ScreenUpdate<CardDetailCommand>.Continue(redraw: false);
        }

        UpdateDraftFromEditor();
        _feedback = null;
        layout = CreateLayout(CreateDisplayCard(), viewport);
        _descriptionScroll = EffectiveDescriptionScroll(layout);
        _commentsScroll = EffectiveCommentsScroll(layout);
        _optionsScroll = EffectiveOptionsScroll(layout);
        return ScreenUpdate<CardDetailCommand>.Continue();
    }

    private ScreenUpdate<CardDetailCommand> HandleChoicePickerKey(
        ConsoleKeyInfo key,
        TerminalViewport viewport)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            CancelChoicePicker();
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (key.Key == ConsoleKey.S && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return RequestSave(viewport);
        }

        if (key.Key == ConsoleKey.Tab)
        {
            AcceptChoicePicker();
            MoveFocus(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1);
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (key.Key == ConsoleKey.Enter)
        {
            AcceptChoicePicker();
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (key.Key == ConsoleKey.Spacebar && _tagPicker is not null)
        {
            _tagPicker.ToggleHighlighted();
            _feedback = null;
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        var pageSize = Math.Max(1, Math.Min(6, viewport.Height - 8));
        switch (key.Key)
        {
            case ConsoleKey.J:
            case ConsoleKey.DownArrow:
                ActiveChoicePicker!.Move(1);
                break;
            case ConsoleKey.K:
            case ConsoleKey.UpArrow:
                ActiveChoicePicker!.Move(-1);
                break;
            case ConsoleKey.PageDown:
                ActiveChoicePicker!.Move(pageSize);
                break;
            case ConsoleKey.PageUp:
                ActiveChoicePicker!.Move(-pageSize);
                break;
            case ConsoleKey.Home:
                ActiveChoicePicker!.MoveToStart();
                break;
            case ConsoleKey.End:
                ActiveChoicePicker!.MoveToEnd();
                break;
            default:
                return ScreenUpdate<CardDetailCommand>.Continue(redraw: false);
        }

        _feedback = null;
        return ScreenUpdate<CardDetailCommand>.Continue();
    }

    private ScreenUpdate<CardDetailCommand> RequestSave(TerminalViewport viewport)
    {
        AcceptChoicePicker();
        CommitEditor();
        if (_draft is null)
        {
            return HasDirtyCommentDraft()
                ? RequestPostComment(viewport)
                : ScreenUpdate<CardDetailCommand>.Continue(redraw: false);
        }

        var title = _draft.Title.Trim();
        if (title.Length == 0)
        {
            SetLocalValidationError(CardDetailField.Title, "Card title is required.");
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (title.Length > 200)
        {
            SetLocalValidationError(CardDetailField.Title, "Card title must be 200 characters or fewer.");
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (!_data.CardTypes.ContainsKey(_draft.CardTypeId))
        {
            SetLocalValidationError(CardDetailField.CardType, "Card type is no longer available on this board.");
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (!_data.Board.Columns.Any(column => column.Id == _draft.BoardColumnId))
        {
            SetLocalValidationError(
                CardDetailField.BoardColumn,
                "Column is no longer available on this board.");
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (_draft.AssignedUserId is int assignedUserId
            && !_data.Members.Any(member => member.UserId == assignedUserId))
        {
            SetLocalValidationError(
                CardDetailField.AssignedUser,
                "Assigned user is no longer an active member of this board.");
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (_draft.TagNames.Any(tagName => FindTagByName(tagName) is null))
        {
            SetLocalValidationError(
                CardDetailField.Tags,
                "A selected tag is no longer available on this board.");
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (_draft.SlickName is not null
            && FindSlickByName(_draft.SlickName) is null)
        {
            SetLocalValidationError(
                CardDetailField.Slick,
                "Slick is no longer available on this board.");
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        var externalUrl = NormaliseExternalUrl(_draft.ExternalUrl);
        if (externalUrl is not null && !IsHttpOrHttpsUrl(externalUrl))
        {
            SetLocalValidationError(
                CardDetailField.ExternalUrl,
                "External URL must be an absolute HTTP or HTTPS URL.");
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        _draft = _draft with { Title = title, ExternalUrl = externalUrl };
        if (HasDirtyCommentDraft() && !TryPrepareCommentDraft(viewport))
        {
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        return ScreenUpdate<CardDetailCommand>.Complete(CardDetailCommand.Save);
    }

    private ScreenUpdate<CardDetailCommand> RequestPostComment(TerminalViewport viewport)
    {
        CommitEditor();
        if (!TryPrepareCommentDraft(viewport))
        {
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        if (_draft is not null)
        {
            return RequestSave(viewport);
        }

        return ScreenUpdate<CardDetailCommand>.Complete(CardDetailCommand.PostComment);
    }

    private bool TryPrepareCommentDraft(TerminalViewport viewport)
    {
        var text = _commentDraft?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            Focus(CardDetailField.Comments);
            BeginEditingFocusedField(viewport);
            _feedback = "Comment text is required.";
            _feedbackIsError = true;
            return false;
        }

        if (text.Length > 4000)
        {
            Focus(CardDetailField.Comments);
            BeginEditingFocusedField(viewport);
            _feedback = "Comment must be 4,000 characters or fewer.";
            _feedbackIsError = true;
            return false;
        }

        _commentDraft = text;
        return true;
    }

    private ScreenUpdate<CardDetailCommand> RequestCommentsLoad()
    {
        if (_isNew || _commentsLoading || _comments is not null)
        {
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        _commentsLoading = true;
        _commentsLoadFailed = false;
        _feedback = "Loading comments…";
        _feedbackIsError = false;
        return ScreenUpdate<CardDetailCommand>.Complete(CardDetailCommand.LoadComments);
    }

    private ScreenUpdate<CardDetailCommand> ContinueOrLoadComments() =>
        _focusedField == CardDetailField.Comments
            ? RequestCommentsLoad()
            : ScreenUpdate<CardDetailCommand>.Continue();

    private ScreenUpdate<CardDetailCommand> DiscardOrClose()
    {
        if (!HasDirtyDraft() && !HasDirtyCommentDraft())
        {
            return ScreenUpdate<CardDetailCommand>.Complete(CardDetailCommand.Close);
        }

        _confirmingDiscard = true;
        _feedback = null;
        return ScreenUpdate<CardDetailCommand>.Continue();
    }

    private ScreenUpdate<CardDetailCommand> HandleDiscardConfirmationKey(ConsoleKeyInfo key)
    {
        if (key.Key is ConsoleKey.Enter or ConsoleKey.Y)
        {
            return ScreenUpdate<CardDetailCommand>.Complete(CardDetailCommand.Close);
        }

        if (key.Key is ConsoleKey.Escape or ConsoleKey.N)
        {
            _confirmingDiscard = false;
            return ScreenUpdate<CardDetailCommand>.Continue();
        }

        return ScreenUpdate<CardDetailCommand>.Continue(redraw: false);
    }

    private void BeginEditingFocusedField(TerminalViewport viewport)
    {
        _editingField = _focusedField;
        _feedback = null;
        if (_focusedField == CardDetailField.Tags)
        {
            BeginTagPicker();
            return;
        }
        if (_focusedField == CardDetailField.BoardColumn)
        {
            BeginColumnPicker();
            return;
        }
        if (_focusedField == CardDetailField.CardType)
        {
            BeginCardTypePicker();
            return;
        }

        if (_focusedField == CardDetailField.AssignedUser)
        {
            BeginAssigneePicker();
            return;
        }

        if (_focusedField == CardDetailField.Slick)
        {
            BeginSlickPicker();
            return;
        }

        if (_focusedField == CardDetailField.Comments)
        {
            if (_comments is null)
            {
                _editingField = null;
                return;
            }

            _editor = new MultilineTextEditor(
                _commentDraft ?? string.Empty,
                maximumLength: 4000,
                cursorAtEnd: true);
            var commentLayout = CreateLayout(CreateDisplayCard(), viewport);
            _commentsScroll = Math.Clamp(
                _commentsScroll,
                0,
                commentLayout.CommentsMaxScroll);
            return;
        }

        _draft ??= CardDraft.From(_card);
        switch (_focusedField)
        {
            case CardDetailField.Title:
                _editor = new MultilineTextEditor(
                    _draft.Title,
                    allowNewLines: false,
                    maximumLength: 200,
                    cursorAtEnd: true);
                break;
            case CardDetailField.Description:
                _editor = new MultilineTextEditor(_draft.Description);
                var layout = CreateLayout(CreateDisplayCard(), viewport);
                _descriptionScroll = Math.Clamp(
                    _descriptionScroll,
                    0,
                    layout.DescriptionMaxScroll);
                _editor.MoveToVisualRow(_descriptionScroll, layout.DescriptionTextWidth);
                break;
            case CardDetailField.ExternalUrl:
                _editor = new MultilineTextEditor(
                    _draft.ExternalUrl ?? string.Empty,
                    allowNewLines: false,
                    cursorAtEnd: true);
                break;
        }
    }

    private void BeginCardTypePicker()
    {
        var items = _data.CardTypes.Values.ToArray();
        if (items.Length == 0)
        {
            _editingField = null;
            _feedback = "No card types are available on this board.";
            _feedbackIsError = true;
            return;
        }

        var selectedCardTypeId = _draft?.CardTypeId ?? _card.CardTypeId;
        var selectedIndex = Array.FindIndex(items, item => item.Id == selectedCardTypeId);
        _cardTypePicker = new InlineChoicePicker<CardTypeDefinition>(
            items,
            selectedIndex < 0 ? 0 : selectedIndex);
    }

    private void BeginTagPicker()
    {
        var items = _data.Tags
            .OrderBy(tag => tag.Name, StringComparer.Ordinal)
            .ToArray();
        if (items.Length == 0)
        {
            _editingField = null;
            _feedback = "No tags are available on this board.";
            _feedbackIsError = true;
            return;
        }

        var selectedNames = new HashSet<string>(
            _draft?.TagNames ?? _card.TagNames,
            StringComparer.OrdinalIgnoreCase);
        if (selectedNames.Any(name => FindTagByName(name) is null))
        {
            _editingField = null;
            _feedback = "A card tag is no longer available on this board.";
            _feedbackIsError = true;
            return;
        }

        var selectedIndices = items
            .Select((tag, index) => (tag, index))
            .Where(candidate => selectedNames.Contains(candidate.tag.Name))
            .Select(candidate => candidate.index);
        _tagPicker = new InlineMultiChoicePicker<CardTag>(items, selectedIndices);
    }

    private void BeginColumnPicker()
    {
        var items = _data.Board.Columns.ToArray();
        if (items.Length == 0)
        {
            _editingField = null;
            _feedback = "No columns are available on this board.";
            _feedbackIsError = true;
            return;
        }

        var selectedColumnId = _draft?.BoardColumnId ?? _card.BoardColumnId;
        var selectedIndex = Array.FindIndex(items, item => item.Id == selectedColumnId);
        _columnPicker = new InlineChoicePicker<BoardColumn>(
            items,
            selectedIndex < 0 ? 0 : selectedIndex);
    }

    private void BeginAssigneePicker()
    {
        var items = new List<AssigneeChoice>
        {
            new(null, "Unassigned")
        };
        items.AddRange(_data.Members.Select(member =>
            new AssigneeChoice(member.UserId, member.DisplayName)));

        var selectedUserId = _draft is not null ? _draft.AssignedUserId : _card.AssignedUserId;
        var selectedIndex = items.FindIndex(item => item.UserId == selectedUserId);
        _assigneePicker = new InlineChoicePicker<AssigneeChoice>(
            items,
            selectedIndex < 0 ? 0 : selectedIndex);
    }

    private void BeginSlickPicker()
    {
        var items = new List<SlickChoice>
        {
            new(null, null, null)
        };
        items.AddRange(_data.Slicks.Values
            .OrderBy(slick => slick.Name, StringComparer.OrdinalIgnoreCase)
            .Select(slick => new SlickChoice(slick.Id, slick.Name, slick)));

        var selectedSlickName = _draft is not null ? _draft.SlickName : _card.SlickName;
        var selectedIndex = selectedSlickName is null
            ? 0
            : items.FindIndex(item => string.Equals(
                item.Name,
                selectedSlickName,
                StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
        {
            _editingField = null;
            _feedback = "Card's slick is no longer available on this board.";
            _feedbackIsError = true;
            return;
        }

        _slickPicker = new InlineChoicePicker<SlickChoice>(items, selectedIndex);
    }

    private void AcceptChoicePicker()
    {
        if (_tagPicker is not null)
        {
            _draft ??= CardDraft.From(_card);
            _draft = _draft with
            {
                TagNames = _tagPicker.Selected
                    .Select(tag => tag.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            };
        }
        else if (_columnPicker is not null)
        {
            _draft ??= CardDraft.From(_card);
            _draft = _draft with { BoardColumnId = _columnPicker.Highlighted.Id };
        }
        else if (_cardTypePicker is not null)
        {
            _draft ??= CardDraft.From(_card);
            _draft = _draft with { CardTypeId = _cardTypePicker.Highlighted.Id };
        }
        else if (_assigneePicker is not null)
        {
            _draft ??= CardDraft.From(_card);
            _draft = _draft with { AssignedUserId = _assigneePicker.Highlighted.UserId };
        }
        else if (_slickPicker is not null)
        {
            _draft ??= CardDraft.From(_card);
            _draft = _draft with { SlickName = _slickPicker.Highlighted.Name };
        }

        _tagPicker = null;
        _columnPicker = null;
        _cardTypePicker = null;
        _assigneePicker = null;
        _slickPicker = null;
        _editingField = null;
        _feedback = null;
    }

    private void CancelChoicePicker()
    {
        _tagPicker = null;
        _columnPicker = null;
        _cardTypePicker = null;
        _assigneePicker = null;
        _slickPicker = null;
        _editingField = null;
        _feedback = null;
    }

    private void CommitEditor()
    {
        if (_editor is null)
        {
            return;
        }

        var wasComment = _editingField == CardDetailField.Comments;
        UpdateDraftFromEditor();
        _editor = null;
        _editingField = null;
        if (wasComment)
        {
            _commentsScroll = 0;
        }
    }

    private void UpdateDraftFromEditor()
    {
        if (_editor is null || _editingField is not CardDetailField field)
        {
            return;
        }

        if (field == CardDetailField.Comments)
        {
            _commentDraft = _editor.Text;
            return;
        }

        if (_draft is null)
        {
            return;
        }

        _draft = field switch
        {
            CardDetailField.Title => _draft with { Title = _editor.Text },
            CardDetailField.Description => _draft with { Description = _editor.Text },
            CardDetailField.ExternalUrl => _draft with { ExternalUrl = _editor.Text },
            _ => _draft
        };
    }

    private void MoveFocus(int delta, bool wrap = true)
    {
        if (_focusedField == CardDetailField.Comments)
        {
            Focus(delta < 0 ? CardDetailField.Description : CardDetailField.Tags);
            return;
        }

        var index = Array.IndexOf(FocusOrder, _focusedField);
        index = wrap
            ? (index + delta + FocusOrder.Length) % FocusOrder.Length
            : Math.Clamp(index + delta, 0, FocusOrder.Length - 1);
        Focus(FocusOrder[index]);
    }

    private void Focus(CardDetailField field)
    {
        _focusedField = field;
        _ensureFocusedFieldVisible = true;
        if (field is CardDetailField.Description or CardDetailField.Comments)
        {
            _activePane = CardDetailPane.Description;
            _mainTab = field == CardDetailField.Comments
                ? CardDetailMainTab.Comments
                : CardDetailMainTab.Description;
        }
        else if (field is CardDetailField.Tags
            or CardDetailField.BoardColumn
            or CardDetailField.CardType
            or CardDetailField.AssignedUser
            or CardDetailField.Slick
            or CardDetailField.ExternalUrl)
        {
            _activePane = CardDetailPane.Options;
        }
    }

    private ScreenUpdate<CardDetailCommand> SetActiveScroll(int value, CardDetailLayout layout)
    {
        _ensureFocusedFieldVisible = false;
        value = Math.Clamp(value, 0, ActiveMaximumScroll(layout));
        if (value == ActiveScroll())
        {
            return ScreenUpdate<CardDetailCommand>.Continue(redraw: false);
        }

        if (_activePane == CardDetailPane.Description)
        {
            if (_mainTab == CardDetailMainTab.Comments)
            {
                _commentsScroll = value;
            }
            else
            {
                _descriptionScroll = value;
            }
        }
        else
        {
            _optionsScroll = value;
        }

        return ScreenUpdate<CardDetailCommand>.Continue();
    }

    private int EffectiveDescriptionScroll(CardDetailLayout layout)
    {
        var scroll = Math.Clamp(_descriptionScroll, 0, layout.DescriptionMaxScroll);
        if (_editingField != CardDetailField.Description
            || layout.DescriptionCursorRow is not int cursorRow)
        {
            return scroll;
        }

        return ScrollToInclude(scroll, cursorRow, cursorRow, layout.PaneViewportRows, layout.DescriptionMaxScroll);
    }

    private int EffectiveCommentsScroll(CardDetailLayout layout)
    {
        var scroll = Math.Clamp(_commentsScroll, 0, layout.CommentsMaxScroll);
        if (_editingField != CardDetailField.Comments
            || layout.CommentCursorRow is not int cursorRow)
        {
            return scroll;
        }

        return ScrollToInclude(
            scroll,
            cursorRow,
            cursorRow,
            layout.PaneViewportRows,
            layout.CommentsMaxScroll);
    }

    private int EffectiveOptionsScroll(CardDetailLayout layout)
    {
        var scroll = Math.Clamp(_optionsScroll, 0, layout.OptionsMaxScroll);
        var fieldRows = _focusedField switch
        {
            CardDetailField.Tags => (layout.TagsFirstRow, layout.TagsLastRow),
            CardDetailField.BoardColumn => (layout.ColumnFirstRow, layout.ColumnLastRow),
            CardDetailField.CardType => (layout.CardTypeFirstRow, layout.CardTypeLastRow),
            CardDetailField.AssignedUser => (layout.AssignedUserFirstRow, layout.AssignedUserLastRow),
            CardDetailField.Slick => (layout.SlickFirstRow, layout.SlickLastRow),
            CardDetailField.ExternalUrl => (layout.ExternalUrlFirstRow, layout.ExternalUrlLastRow),
            _ => (-1, -1)
        };
        if (fieldRows.Item1 < 0
            || (!_ensureFocusedFieldVisible && _editingField != _focusedField))
        {
            return scroll;
        }

        var firstRow = fieldRows.Item1;
        var lastRow = fieldRows.Item2;
        if (_editingField == CardDetailField.ExternalUrl
            && layout.ExternalUrlCursorRow is int cursorRow)
        {
            firstRow = cursorRow;
            lastRow = cursorRow;
        }

        return ScrollToInclude(scroll, firstRow, lastRow, layout.PaneViewportRows, layout.OptionsMaxScroll);
    }

    private static int ScrollToInclude(
        int scroll,
        int firstRow,
        int lastRow,
        int viewportRows,
        int maximumScroll)
    {
        if (firstRow < scroll)
        {
            return firstRow;
        }

        if (lastRow >= scroll + viewportRows)
        {
            return Math.Min(maximumScroll, lastRow - viewportRows + 1);
        }

        return scroll;
    }

    private int ActiveScroll() =>
        _activePane == CardDetailPane.Options
            ? _optionsScroll
            : _mainTab == CardDetailMainTab.Comments
                ? _commentsScroll
                : _descriptionScroll;

    private int ActiveMaximumScroll(CardDetailLayout layout) =>
        _activePane == CardDetailPane.Options
            ? layout.OptionsMaxScroll
            : _mainTab == CardDetailMainTab.Comments
                ? layout.CommentsMaxScroll
                : layout.DescriptionMaxScroll;

    private int EditorDisplayWidth(CardDetailLayout layout) =>
        _editingField switch
        {
            CardDetailField.Title => layout.TitleTextWidth,
            CardDetailField.ExternalUrl => Math.Max(1, layout.AsideWidth - 3),
            _ => layout.DescriptionTextWidth
        };

    private CardDetailLayout CreateLayout(BoardCard card, TerminalViewport viewport) =>
        _layoutEngine.Create(
            _data,
            card,
            viewport.Width,
            viewport.Height,
            _editingField,
            _editor,
            showTimestamps: !_isNew,
            comments: _comments,
            commentDraft: _commentDraft,
            commentsLoading: _commentsLoading,
            commentsLoadFailed: _commentsLoadFailed);

    private static BoardCard CreateNewCardPreview(BoardData data, CardDraft draft)
    {
        data.CardTypes.TryGetValue(draft.CardTypeId, out var cardType);
        return new BoardCard(
            0,
            draft.BoardColumnId,
            draft.CardTypeId,
            cardType?.Name ?? "Card",
            cardType?.Emoji,
            draft.Title,
            draft.Description,
            string.Empty,
            [],
            draft.TagNames,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch,
            draft.AssignedUserId,
            null,
            null,
            null,
            draft.SlickName,
            draft.ExternalUrl);
    }

    private bool HasDirtyDraft()
    {
        if (_draft is null)
        {
            return false;
        }

        var baseline = _isNew ? _initialDraft! : CardDraft.From(_card);
        return !DraftValuesEqual(_draft, baseline);
    }

    private bool HasDirtyCommentDraft() =>
        !string.IsNullOrEmpty(_commentDraft);

    private static bool DraftValuesEqual(CardDraft left, CardDraft right) =>
        string.Equals(left.Title, right.Title, StringComparison.Ordinal)
        && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
        && left.TagNames.SequenceEqual(right.TagNames, StringComparer.Ordinal)
        && left.CardTypeId == right.CardTypeId
        && left.BoardColumnId == right.BoardColumnId
        && left.AssignedUserId == right.AssignedUserId
        && string.Equals(left.SlickName, right.SlickName, StringComparison.Ordinal)
        && string.Equals(left.ExternalUrl, right.ExternalUrl, StringComparison.Ordinal);

    private BoardCard CreateDisplayCard()
    {
        var title = _draft?.Title ?? _card.Title;
        var description = _draft?.Description ?? _card.Description;
        var tagNames = TagNames;
        var tags = tagNames
            .Select(FindTagByName)
            .Where(tag => tag is not null)
            .Cast<CardTag>()
            .OrderBy(tag => tag.Name, StringComparer.Ordinal)
            .ToArray();
        var externalUrl = _draft?.ExternalUrl ?? _card.ExternalUrl;
        var boardColumnId = BoardColumnId;
        var cardTypeId = CardTypeId;
        _data.CardTypes.TryGetValue(cardTypeId, out var cardType);
        var assignedUserId = AssignedUserId;
        var assignedMember = assignedUserId is int userId
            ? _data.Members.FirstOrDefault(member => member.UserId == userId)
            : null;
        var assignedUserDisplayName = assignedUserId is null
            ? null
            : assignedMember?.DisplayName
                ?? (assignedUserId == _card.AssignedUserId
                    ? _card.AssignedUserDisplayName
                    : $"User #{assignedUserId}");
        var slickName = SlickName;
        var slick = slickName is null ? null : FindSlickByName(slickName);
        var slickId = slick?.Id
            ?? (string.Equals(slickName, _card.SlickName, StringComparison.OrdinalIgnoreCase)
                ? _card.SlickId
                : null);

        return _card with
        {
            Title = title,
            Description = description,
            Tags = tags,
            TagNames = tags.Select(tag => tag.Name).ToArray(),
            ExternalUrl = externalUrl,
            BoardColumnId = boardColumnId,
            CardTypeId = cardTypeId,
            CardTypeName = cardType is null ? _card.CardTypeName : cardType.Name,
            CardTypeEmoji = cardType is null ? _card.CardTypeEmoji : cardType.Emoji,
            AssignedUserId = assignedUserId,
            AssignedUserDisplayName = assignedUserDisplayName,
            AssignedUserImageRelativePath = assignedMember?.ProfileImageRelativePath,
            SlickId = slickId,
            SlickName = slickName
        };
    }

    private SlickDefinition? FindSlickByName(string name) =>
        _data.Slicks.Values.FirstOrDefault(slick =>
            string.Equals(slick.Name, name, StringComparison.OrdinalIgnoreCase));

    private CardTag? FindTagByName(string name) =>
        _data.Tags.FirstOrDefault(tag =>
            string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase));

    private void SetLocalValidationError(CardDetailField field, string message)
    {
        Focus(field);
        _feedback = message;
        _feedbackIsError = true;
    }

    private static string? NormaliseExternalUrl(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static bool IsHttpOrHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string ResolveErrorMessage(Exception exception, out CardDetailField? field)
    {
        field = null;
        if (exception is not BoardOilRequestException requestException)
        {
            return exception.Message;
        }

        var candidates = new[]
        {
            (Name: "title", Field: CardDetailField.Title),
            (Name: "description", Field: CardDetailField.Description),
            (Name: "tagNames", Field: CardDetailField.Tags),
            (Name: "boardColumnId", Field: CardDetailField.BoardColumn),
            (Name: "cardTypeId", Field: CardDetailField.CardType),
            (Name: "assignedUserId", Field: CardDetailField.AssignedUser),
            (Name: "slickName", Field: CardDetailField.Slick),
            (Name: "externalUrl", Field: CardDetailField.ExternalUrl)
        };
        foreach (var candidate in candidates)
        {
            if (requestException.ValidationErrors.TryGetValue(candidate.Name, out var errors)
                && errors.Length > 0)
            {
                field = candidate.Field;
                return string.Join(" ", errors);
            }
        }

        return exception.Message;
    }

    private static string ResolveCommentErrorMessage(Exception exception)
    {
        if (exception is BoardOilRequestException requestException)
        {
            foreach (var name in new[] { "text", "comment", "commentText" })
            {
                if (requestException.ValidationErrors.TryGetValue(name, out var errors)
                    && errors.Length > 0)
                {
                    return string.Join(" ", errors);
                }
            }
        }

        return exception.Message;
    }

    private static ConsoleKeyInfo Key(char character, ConsoleKey key) =>
        new(character, key, shift: false, alt: false, control: false);

    private IInlineChoicePicker? ActiveChoicePicker =>
        _tagPicker
        ?? (IInlineChoicePicker?)_columnPicker
        ?? (IInlineChoicePicker?)_cardTypePicker
        ?? (IInlineChoicePicker?)_assigneePicker
        ?? _slickPicker;
}
