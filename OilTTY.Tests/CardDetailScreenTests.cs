using System.Net;
using System.Text;
using Xunit;

public sealed class CardDetailScreenTests
{
    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(81)]
    public void DetailHeader_CentresOilTtyAcrossTheTerminal(int width)
    {
        var (data, card) = DetailData();
        var canvas = new CardDetailScreen(data, card, "connected")
            .Render(new TerminalViewport(width, 24))
            .Canvas;
        var appNameX = (width - UnicodeDisplay.TextWidth("OilTTY")) / 2;

        Assert.Equal(
            "OilTTY",
            string.Concat(Enumerable.Range(appNameX, 6)
                .Select(column => canvas.CellAt(column, 0))
                .Where(cell => !cell.Continuation)
                .Select(cell => cell.Grapheme)));
    }

    [Fact]
    public void Layout_UsesWebsiteHierarchyWithWideOptionsRail()
    {
        var (data, card) = DetailData();

        var layout = new CardDetailLayoutEngine().Create(data, card, 120, 36);

        Assert.True(layout.IsWide);
        Assert.Contains("A detailed description", Text(layout.DescriptionLines));
        var options = Text(layout.OptionsLines);
        AssertInOrder(
            options,
            "TAGS",
            "UI",
            "COLUMN",
            "In progress",
            "TYPE",
            "OilTTY",
            "ASSIGNED TO",
            "Luke",
            "SLICK",
            "Editor flow",
            "EXTERNAL URL",
            "https://example.test/802",
            "CREATED",
            "UPDATED");
    }

    [Fact]
    public void DetailHeader_ExpandsTheCardTypeSurfaceAcrossTheFullWidth()
    {
        var (sourceData, card) = DetailData();
        var cardType = new CardTypeDefinition(
            card.CardTypeId,
            card.CardTypeName,
            card.CardTypeEmoji,
            "gradient",
            "{\"leftColor\":\"#35165A\",\"rightColor\":\"#286B78\",\"textColorMode\":\"auto\",\"borderMode\":\"auto\"}");
        var data = sourceData with
        {
            CardTypes = new Dictionary<int, CardTypeDefinition> { [cardType.Id] = cardType }
        };
        var viewport = new TerminalViewport(80, 24);
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var frame = new CardDetailScreen(data, card, "connected").Render(viewport);
        var style = BoardStyles.ResolveCard(cardType);

        Assert.Equal("╭", frame.Canvas.CellAt(0, 1).Grapheme);
        Assert.Equal("╮", frame.Canvas.CellAt(viewport.Width - 1, 1).Grapheme);
        Assert.Equal("│", frame.Canvas.CellAt(0, 2).Grapheme);
        Assert.Equal("╰", frame.Canvas.CellAt(0, layout.ContentTop - 1).Grapheme);
        Assert.Equal(style.BackgroundAt(0, viewport.Width), frame.Canvas.CellAt(0, 2).Background);
        Assert.Equal(
            style.BackgroundAt(viewport.Width - 1, viewport.Width),
            frame.Canvas.CellAt(viewport.Width - 1, 2).Background);
    }

    [Fact]
    public void TitleFocus_UsesAnAnchorAfterTheEmojiWithoutChangingTheCardBorder()
    {
        var (data, card) = DetailData();
        var viewport = new TerminalViewport(80, 24);
        var screen = new CardDetailScreen(data, card, "connected");
        var initialCanvas = screen.Render(viewport).Canvas;
        screen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);

        var canvas = screen.Render(viewport).Canvas;
        var prefixWidth = UnicodeDisplay.TextWidth(
            UnicodeDisplay.EmojiLabelPrefix(card.CardTypeEmoji ?? string.Empty));
        var anchorX = 2 + prefixWidth;

        Assert.Equal("╭", canvas.CellAt(0, 1).Grapheme);
        Assert.Equal(card.CardTypeEmoji, canvas.CellAt(2, 2).Grapheme);
        Assert.Equal("▌", initialCanvas.CellAt(anchorX, 2).Grapheme);
        Assert.Equal(
            BoardStyles.FieldAnchorPlaceholder,
            initialCanvas.CellAt(anchorX, 2).Foreground);
        Assert.Equal("▌", canvas.CellAt(anchorX, 2).Grapheme);
        Assert.Equal(BoardStyles.Selection, canvas.CellAt(anchorX, 2).Foreground);
        Assert.Equal("O", canvas.CellAt(anchorX + 2, 2).Grapheme);
    }

    [Fact]
    public void NarrowScreen_KeepsDescriptionAndOptionsVisibleTogether()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(40, 30);

        var initial = PlainText(screen.Render(viewport).Canvas);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        var focused = PlainText(screen.Render(viewport).Canvas);

        Assert.Contains("DESCRIPTION", initial);
        Assert.Contains("A detailed", initial);
        Assert.Contains("DETAILS", initial);
        Assert.Contains("TAGS", initial);
        Assert.Contains("A detailed", focused);
        Assert.Contains("EXTERNAL URL", focused);
    }

    [Fact]
    public void StandardWidth_KeepsBothPanesVisibleWhileEditingAField()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.ExternalUrl, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        var frame = screen.Render(viewport);
        var rendered = PlainText(frame.Canvas);

        Assert.Contains("DESCRIPTION", rendered);
        Assert.Contains("A detailed description", rendered);
        Assert.Contains("DETAILS · EDITING", rendered);
        Assert.Contains("EXTERNAL URL", rendered);
        Assert.NotNull(frame.Cursor);
    }

    [Fact]
    public void ExternalUrlFocus_AnchorsTheFieldRatherThanTheDetailsRail()
    {
        var (data, card) = DetailData();
        var viewport = new TerminalViewport(80, 30);
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var screen = new CardDetailScreen(data, card, "connected");
        var initialCanvas = screen.Render(viewport).Canvas;
        FocusField(screen, CardDetailField.ExternalUrl, viewport);

        var canvas = screen.Render(viewport).Canvas;
        var urlRow = layout.PaneContentTop + layout.ExternalUrlFirstRow;

        Assert.Equal("▌", initialCanvas.CellAt(layout.AsideX, urlRow).Grapheme);
        Assert.Equal(
            BoardStyles.FieldAnchorPlaceholder,
            initialCanvas.CellAt(layout.AsideX, urlRow).Foreground);
        Assert.Equal(" ", canvas.CellAt(layout.AsideX, layout.ContentTop).Grapheme);
        Assert.Equal("▌", canvas.CellAt(layout.MainX, layout.ContentTop).Grapheme);
        Assert.Equal("▌", canvas.CellAt(layout.AsideX, urlRow).Grapheme);
        Assert.Equal(BoardStyles.Selection, canvas.CellAt(layout.AsideX, urlRow).Foreground);
        Assert.Equal("E", canvas.CellAt(layout.AsideX + 2, urlRow).Grapheme);
    }

    [Fact]
    public void MovingTheAnchorDoesNotRetintInactiveDetailsFields()
    {
        var (data, card) = DetailData();
        var viewport = new TerminalViewport(80, 30);
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var screen = new CardDetailScreen(data, card, "connected");
        var unfocused = screen.Render(viewport).Canvas;
        FocusField(screen, CardDetailField.CardType, viewport);
        var focused = screen.Render(viewport).Canvas;
        var typeRow = layout.PaneContentTop + layout.CardTypeFirstRow;

        Assert.Equal(BoardStyles.RootBackground, unfocused.CellAt(layout.AsideX + 2, typeRow).Background);
        Assert.Equal(BoardStyles.RootBackground, focused.CellAt(layout.AsideX + 2, typeRow).Background);
        Assert.Equal("▌", focused.CellAt(layout.AsideX, typeRow).Grapheme);
        Assert.Equal(BoardStyles.Selection, focused.CellAt(layout.AsideX, typeRow).Foreground);
    }

    [Fact]
    public void EditingDoesNotRetintTheDescriptionOrDetailsPane()
    {
        var (data, card) = DetailData();
        var viewport = new TerminalViewport(80, 30);
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);

        var descriptionScreen = new CardDetailScreen(data, card, "connected");
        var descriptionIdle = descriptionScreen.Render(viewport).Canvas;
        descriptionScreen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var descriptionEditing = descriptionScreen.Render(viewport).Canvas;

        Assert.Equal(
            descriptionIdle.CellAt(layout.MainX + 2, layout.ContentTop).Background,
            descriptionEditing.CellAt(layout.MainX + 2, layout.ContentTop).Background);
        Assert.Equal(
            descriptionIdle.CellAt(layout.MainX + 2, layout.PaneContentTop).Background,
            descriptionEditing.CellAt(layout.MainX + 2, layout.PaneContentTop).Background);

        var urlScreen = new CardDetailScreen(data, card, "connected");
        FocusField(urlScreen, CardDetailField.ExternalUrl, viewport);
        var urlIdle = urlScreen.Render(viewport).Canvas;
        urlScreen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var urlEditing = urlScreen.Render(viewport).Canvas;
        var urlRow = layout.PaneContentTop + layout.ExternalUrlFirstRow - urlScreen.OptionsScroll;

        Assert.Equal(BoardStyles.RootBackground, urlIdle.CellAt(layout.AsideX + 2, urlRow).Background);
        Assert.Equal(BoardStyles.RootBackground, urlEditing.CellAt(layout.AsideX + 2, urlRow).Background);
    }

    [Fact]
    public void TitleAndDescriptionCursors_DoNotPreventOtherPanesFromRendering()
    {
        var (data, card) = DetailData();
        var viewport = new TerminalViewport(80, 24);

        var descriptionScreen = new CardDetailScreen(data, card, "connected");
        descriptionScreen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var descriptionFrame = descriptionScreen.Render(viewport);
        var descriptionRendered = PlainText(descriptionFrame.Canvas);

        var titleScreen = new CardDetailScreen(data, card, "connected");
        titleScreen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);
        titleScreen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var titleFrame = titleScreen.Render(viewport);
        var titleRendered = PlainText(titleFrame.Canvas);

        Assert.NotNull(descriptionFrame.Cursor);
        Assert.Contains("DETAILS", descriptionRendered);
        Assert.Contains("TAGS", descriptionRendered);
        Assert.NotNull(titleFrame.Cursor);
        Assert.Contains("DESCRIPTION", titleRendered);
        Assert.Contains("DETAILS", titleRendered);
        Assert.Contains("A detailed description", titleRendered);
    }

    [Fact]
    public void Screen_ScrollsDescriptionAndOptionsIndependently()
    {
        var (data, sourceCard) = DetailData();
        var card = sourceCard with
        {
            Description = string.Join('\n', Enumerable.Range(1, 30).Select(index => $"Description line {index}"))
        };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(60, 14);

        var initial = PlainText(screen.Render(viewport).Canvas);
        screen.HandleKey(Key(ConsoleKey.End), viewport);
        var descriptionScroll = screen.DescriptionScroll;
        var scrolledDescription = PlainText(screen.Render(viewport).Canvas);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        var initialOptions = PlainText(screen.Render(viewport).Canvas);
        screen.HandleKey(Key(ConsoleKey.End), viewport);
        var scrolledOptions = PlainText(screen.Render(viewport).Canvas);
        var closeUpdate = screen.HandleKey(Key(ConsoleKey.Escape), viewport);

        Assert.Contains("Description line 1", initial);
        Assert.True(descriptionScroll > 0);
        Assert.Contains("Description line 30", scrolledDescription);
        Assert.DoesNotContain("UPDATED", scrolledDescription);
        Assert.Contains("COLUMN", initialOptions);
        Assert.True(screen.OptionsScroll > 0);
        Assert.Equal(descriptionScroll, screen.DescriptionScroll);
        Assert.Contains("UPDATED", scrolledOptions);
        Assert.True(closeUpdate.IsComplete);
        Assert.Equal(CardDetailCommand.Close, closeUpdate.Result);
    }

    [Fact]
    public void DescriptionArrows_MoveFocusWhileJAndKScrollVertically()
    {
        var (data, sourceCard) = DetailData();
        var card = sourceCard with
        {
            Description = string.Join('\n', Enumerable.Range(1, 30).Select(index => $"Line {index}"))
        };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(60, 14);

        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);

        Assert.Equal(CardDetailField.Tags, screen.FocusedField);
        Assert.Equal(0, screen.DescriptionScroll);

        screen.HandleKey(Key(ConsoleKey.LeftArrow), viewport);
        Assert.Equal(CardDetailField.Description, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.UpArrow), viewport);
        Assert.Equal(CardDetailField.Title, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.RightArrow), viewport);
        Assert.Equal(CardDetailField.Description, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.J, 'j'), viewport);
        Assert.Equal(1, screen.DescriptionScroll);
        Assert.Equal(CardDetailField.Description, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.K, 'k'), viewport);
        Assert.Equal(0, screen.DescriptionScroll);
        Assert.Equal(CardDetailField.Description, screen.FocusedField);
    }

    [Fact]
    public void DescriptionScroll_IsPreservedWhenDiscardConfirmationIsCancelled()
    {
        var (data, sourceCard) = DetailData();
        var card = sourceCard with
        {
            Description = string.Join('\n',
            ["TOP marker", .. Enumerable.Range(1, 28).Select(index => $"Line {index}"), "BOTTOM marker"])
        };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(60, 14);
        screen.HandleKey(Key(ConsoleKey.End), viewport);
        var scroll = screen.DescriptionScroll;

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var editing = PlainText(screen.Render(viewport).Canvas);

        Assert.True(screen.IsEditing);
        Assert.Equal(scroll, screen.DescriptionScroll);
        Assert.Contains("BOTTOM marker", editing);
        Assert.DoesNotContain("TOP marker", editing);

        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);
        screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var cancelled = PlainText(screen.Render(viewport).Canvas);

        Assert.False(screen.IsEditing);
        Assert.Equal(scroll, screen.DescriptionScroll);
        Assert.Contains("BOTTOM marker", cancelled);
        Assert.DoesNotContain("TOP marker", cancelled);
    }

    [Fact]
    public void DetailTags_TouchWithoutAnExtraInterTagSpace()
    {
        var (data, sourceCard) = DetailData();
        var tags = data.Tags.Where(tag => tag.Name is "Feature" or "UI").ToArray();
        var card = sourceCard with
        {
            Tags = tags,
            TagNames = tags.Select(tag => tag.Name).ToArray()
        };

        var layout = new CardDetailLayoutEngine().Create(data, card, 120, 36);
        var options = Text(layout.OptionsLines);

        Assert.Contains("▌▐", options);
        Assert.DoesNotContain("▌ ▐", options);
    }

    [Fact]
    public void DescriptionEdit_FirstEscapeFinishesEditingAndSecondOffersDiscardConfirmation()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);
        var editingFrame = screen.Render(viewport);
        var editedDescription = screen.Description;
        var finishEditing = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var finishedFrame = screen.Render(viewport);
        var warning = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var warningText = PlainText(screen.Render(viewport).Canvas);

        Assert.NotNull(editingFrame.Cursor);
        Assert.StartsWith("X", editedDescription);
        Assert.False(finishEditing.IsComplete);
        Assert.False(screen.IsEditing);
        Assert.Null(finishedFrame.Cursor);
        Assert.Equal(editedDescription, screen.Description);
        Assert.False(warning.IsComplete);
        Assert.True(screen.IsConfirmingDiscard);
        Assert.Contains("Discard changes?", warningText);
    }

    [Fact]
    public void DescriptionSave_ProducesPreservationFirstDraft()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);

        var save = screen.HandleKey(
            new ConsoleKeyInfo('s', ConsoleKey.S, shift: false, alt: false, control: true),
            viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);

        Assert.True(save.IsComplete);
        Assert.Equal(CardDetailCommand.Save, save.Result);
        Assert.StartsWith("X", draft.Description);
        Assert.Equal(card.Title, draft.Title);
        Assert.Equal(card.TagNames, draft.TagNames);
        Assert.Equal(card.CardTypeId, draft.CardTypeId);
        Assert.Equal(card.BoardColumnId, draft.BoardColumnId);
        Assert.Equal(card.AssignedUserId, draft.AssignedUserId);
        Assert.Equal(card.SlickName, draft.SlickName);
        Assert.Equal(card.ExternalUrl, draft.ExternalUrl);
    }

    [Fact]
    public void DescriptionValidationError_RemainsVisibleWithoutDiscardingDraft()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(120, 30);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);

        screen.SetSaveError(new BoardOilRequestException(
            HttpStatusCode.UnprocessableEntity,
            "Validation failed.",
            new Dictionary<string, string[]>
            {
                ["description"] = ["Description is too long."]
            }));
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.True(screen.IsEditing);
        Assert.StartsWith("X", screen.Description);
        Assert.Contains("Description is too long.", rendered);
    }

    [Fact]
    public void SuccessfulSave_ReplacesTheBoardCardAndLeavesTheEditor()
    {
        var (data, card) = DetailData();
        var boardScreen = new BoardScreen(data, "connected");
        var detailScreen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        detailScreen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        detailScreen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);
        var updatedCard = card with
        {
            Description = detailScreen.Description,
            CardUpdatedUtc = card.CardUpdatedUtc.AddMinutes(1)
        };

        boardScreen.ReplaceCard(updatedCard);
        detailScreen.ApplySaved(boardScreen.Data, updatedCard);
        var rendered = PlainText(detailScreen.Render(viewport).Canvas);

        Assert.False(detailScreen.IsEditing);
        Assert.Equal(updatedCard, boardScreen.SelectedCard);
        Assert.Equal(updatedCard.Description, detailScreen.Description);
        Assert.Contains("Saved.", rendered);
    }

    [Fact]
    public void ReplaceCard_MovesAndReselectsTheServerPositionedCard()
    {
        var (sourceData, card) = DetailData();
        var destinationCards = Enumerable.Range(0, 5)
            .Select(index => TestBoardFactory.Card(900 + index, 3) with
            {
                SortKey = ((char)('A' + index)).ToString()
            })
            .ToArray();
        var columns = sourceData.Board.Columns
            .Select(column => column.Id == 3
                ? column with { Cards = destinationCards }
                : column)
            .ToArray();
        var data = sourceData with { Board = sourceData.Board with { Columns = columns } };
        var screen = new BoardScreen(data, "connected");
        var movedCard = card with { BoardColumnId = 3, SortKey = "Z" };

        screen.ReplaceCard(movedCard);
        var rendered = PlainText(screen.Render(new TerminalViewport(80, 12)).Canvas);

        Assert.DoesNotContain(screen.Data.Board.Columns[0].Cards, item => item.Id == card.Id);
        Assert.Equal(
            destinationCards.Select(item => item.Id).Append(movedCard.Id),
            screen.Data.Board.Columns[1].Cards.Select(item => item.Id));
        Assert.Equal(movedCard.Id, screen.SelectedCardId);
        Assert.Equal(3, Assert.IsType<BoardCard>(screen.SelectedCard).BoardColumnId);
        Assert.Contains("Open and edit", rendered);
    }

    [Fact]
    public void BoardEnterAndDetailEscape_PreserveBoardSelectionAndViewport()
    {
        var board = TestBoardFactory.Board(
            TestBoardFactory.Column(
                1,
                TestBoardFactory.Card(1, 1),
                TestBoardFactory.Card(2, 1),
                TestBoardFactory.Card(3, 1),
                TestBoardFactory.Card(4, 1),
                TestBoardFactory.Card(5, 1)));
        var data = new BoardData(board, new Dictionary<int, CardTypeDefinition>(), new Dictionary<int, SlickDefinition>(), [], []);
        var boardScreen = new BoardScreen(data, "connected");
        var viewport = new TerminalViewport(80, 12);
        boardScreen.HandleKey(Key(ConsoleKey.J, 'j'), viewport);
        boardScreen.HandleKey(Key(ConsoleKey.J, 'j'), viewport);
        boardScreen.HandleKey(Key(ConsoleKey.J, 'j'), viewport);

        var open = boardScreen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var before = PlainText(boardScreen.Render(viewport).Canvas);
        var selectedCard = Assert.IsType<BoardCard>(boardScreen.SelectedCard);
        var detail = new CardDetailScreen(data, selectedCard, "connected");
        var close = detail.HandleKey(Key(ConsoleKey.Escape), viewport);
        var after = PlainText(boardScreen.Render(viewport).Canvas);

        Assert.True(open.IsComplete);
        Assert.Equal(BoardCommand.OpenCard, open.Result);
        Assert.Equal(4, boardScreen.SelectedCardId);
        Assert.True(close.IsComplete);
        Assert.Equal(before, after);
    }

    [Fact]
    public void DetailControlC_RequestsApplicationQuit()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");

        var result = screen.HandleKey(
            new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: true),
            new TerminalViewport(80, 24));

        Assert.True(result.IsComplete);
        Assert.Equal(CardDetailCommand.Quit, result.Result);
    }

    [Fact]
    public void TabAndShiftTab_CycleOneFieldFocusOrder()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(60, 24);

        Assert.Equal(CardDetailField.Description, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        Assert.Equal(CardDetailField.Tags, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        Assert.Equal(CardDetailField.BoardColumn, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        Assert.Equal(CardDetailField.CardType, screen.FocusedField);
        Assert.Equal(CardDetailPane.Options, screen.ActivePane);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        Assert.Equal(CardDetailField.AssignedUser, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        Assert.Equal(CardDetailField.Slick, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        Assert.Equal(CardDetailField.ExternalUrl, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        Assert.Equal(CardDetailField.Title, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        Assert.Equal(CardDetailField.Description, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);
        Assert.Equal(CardDetailField.Title, screen.FocusedField);
    }

    [Fact]
    public void ArrowKeys_MoveFieldFocusWithoutWrapping()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);

        screen.HandleKey(Key(ConsoleKey.LeftArrow), viewport);
        Assert.Equal(CardDetailField.Title, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.UpArrow), viewport);
        Assert.Equal(CardDetailField.Title, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.RightArrow), viewport);
        Assert.Equal(CardDetailField.Description, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        Assert.Equal(CardDetailField.Tags, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        Assert.Equal(CardDetailField.BoardColumn, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        Assert.Equal(CardDetailField.CardType, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        Assert.Equal(CardDetailField.AssignedUser, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        Assert.Equal(CardDetailField.Slick, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        Assert.Equal(CardDetailField.ExternalUrl, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        Assert.Equal(CardDetailField.ExternalUrl, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.UpArrow), viewport);
        Assert.Equal(CardDetailField.Slick, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.UpArrow), viewport);
        Assert.Equal(CardDetailField.AssignedUser, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.UpArrow), viewport);
        Assert.Equal(CardDetailField.CardType, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.UpArrow), viewport);
        Assert.Equal(CardDetailField.BoardColumn, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.UpArrow), viewport);
        Assert.Equal(CardDetailField.Tags, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.UpArrow), viewport);
        Assert.Equal(CardDetailField.Description, screen.FocusedField);
        screen.HandleKey(Key(ConsoleKey.UpArrow), viewport);
        Assert.Equal(CardDetailField.Title, screen.FocusedField);
    }

    [Theory]
    [InlineData((int)CardDetailField.Tags)]
    [InlineData((int)CardDetailField.BoardColumn)]
    [InlineData((int)CardDetailField.CardType)]
    [InlineData((int)CardDetailField.AssignedUser)]
    [InlineData((int)CardDetailField.Slick)]
    [InlineData((int)CardDetailField.ExternalUrl)]
    public void LeftArrow_FromAnyDetailsFieldReturnsToDescription(int fieldValue)
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        var field = (CardDetailField)fieldValue;
        FocusField(screen, field, viewport);

        screen.HandleKey(Key(ConsoleKey.LeftArrow), viewport);

        Assert.Equal(CardDetailField.Description, screen.FocusedField);
        Assert.Equal(CardDetailPane.Description, screen.ActivePane);
    }

    [Fact]
    public void TagPicker_PreviewsAndSavesMultipleExistingTagsWithTheirStyles()
    {
        var (data, card) = DetailData();
        var featureTag = Assert.Single(data.Tags, tag => tag.Name == "Feature");
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 30);
        FocusField(screen, CardDetailField.Tags, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.Home), viewport);

        var opened = PlainText(screen.Render(viewport).Canvas);
        screen.HandleKey(Key(ConsoleKey.Spacebar, ' '), viewport);
        var preview = screen.Render(viewport);
        var featureStyle = BoardStyles.ResolveTag(featureTag);

        Assert.True(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Contains("TAGS", opened);
        Assert.Contains("Feature", opened);
        Assert.Contains("Tech Debt", opened);
        Assert.Contains("UI", opened);
        Assert.Contains("space", opened);
        Assert.Equal(["Feature", "UI"], screen.TagNames);
        Assert.True(CanvasContainsBackground(preview.Canvas, featureStyle.BackgroundAt(0, 1)));
        Assert.True(CanvasHasCheckBeforeTagPill(preview.Canvas));
        Assert.Contains("Feature", PlainText(preview.Canvas));
        Assert.Contains("UI", PlainText(preview.Canvas));

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);
        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);

        Assert.False(screen.IsEditing);
        Assert.True(save.IsComplete);
        Assert.Equal(CardDetailCommand.Save, save.Result);
        Assert.Equal(["Feature", "UI"], draft.TagNames);
        Assert.Equal(card.Title, draft.Title);
        Assert.Equal(card.Description, draft.Description);
        Assert.Equal(card.CardTypeId, draft.CardTypeId);
        Assert.Equal(card.BoardColumnId, draft.BoardColumnId);
        Assert.Equal(card.AssignedUserId, draft.AssignedUserId);
        Assert.Equal(card.SlickName, draft.SlickName);
        Assert.Equal(card.ExternalUrl, draft.ExternalUrl);
    }

    [Fact]
    public void TagPicker_CanRemoveTheLastTag()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.Tags, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        screen.HandleKey(Key(ConsoleKey.Spacebar, ' '), viewport);

        Assert.Empty(screen.TagNames);
        Assert.False(screen.HasDraft);

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.Empty(draft.TagNames);
        Assert.Contains("None", rendered);
    }

    [Fact]
    public void TagPicker_EscapeCancelsAllTogglesWithoutCreatingADraft()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.Tags, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.Spacebar, ' '), viewport);

        var cancel = screen.HandleKey(Key(ConsoleKey.Escape), viewport);

        Assert.False(cancel.IsComplete);
        Assert.False(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Equal(card.TagNames, screen.TagNames);
    }

    [Fact]
    public void TagPicker_TabAcceptsAndAdvancesToColumn()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.Tags, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.Home), viewport);
        screen.HandleKey(Key(ConsoleKey.Spacebar, ' '), viewport);

        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);

        Assert.False(screen.IsEditing);
        Assert.Equal(CardDetailField.BoardColumn, screen.FocusedField);
        Assert.Equal(
            ["Feature", "UI"],
            Assert.IsType<CardDraft>(screen.PendingDraft).TagNames);
    }

    [Fact]
    public void TagValidationErrorFocusesTheTagsAnchor()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 30);

        screen.SetSaveError(new BoardOilRequestException(
            HttpStatusCode.UnprocessableEntity,
            "Validation failed.",
            new Dictionary<string, string[]>
            {
                ["tagNames"] = ["A selected tag is no longer available."]
            }));
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var canvas = screen.Render(viewport).Canvas;
        var tagsRow = layout.PaneContentTop + layout.TagsFirstRow - screen.OptionsScroll;

        Assert.Equal(CardDetailField.Tags, screen.FocusedField);
        Assert.Equal("▌", canvas.CellAt(layout.AsideX, tagsRow).Grapheme);
        Assert.Equal(BoardStyles.Selection, canvas.CellAt(layout.AsideX, tagsRow).Foreground);
        Assert.Contains("A selected tag is no longer available.", PlainText(canvas));
    }

    [Fact]
    public void SaveRejectsATagThatIsNoLongerLoadedForTheBoard()
    {
        var (sourceData, card) = DetailData();
        var data = sourceData with
        {
            Tags = sourceData.Tags.Where(tag => tag.Name != "UI").ToArray()
        };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);

        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.False(save.IsComplete);
        Assert.Equal(CardDetailField.Tags, screen.FocusedField);
        Assert.Contains("no longer available", rendered);
    }

    [Fact]
    public void ColumnPicker_PreviewsAndSavesAColumnWithoutReplacingTheCardView()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 30);
        FocusField(screen, CardDetailField.BoardColumn, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        var opened = PlainText(screen.Render(viewport).Canvas);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        var preview = PlainText(screen.Render(viewport).Canvas);

        Assert.True(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Contains("COLUMN", opened);
        Assert.Contains("In progress", opened);
        Assert.Contains("Done", opened);
        Assert.Contains("A detailed description", opened);
        Assert.Equal(3, screen.BoardColumnId);
        Assert.Contains("Done", preview);

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);
        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);

        Assert.False(screen.IsEditing);
        Assert.True(save.IsComplete);
        Assert.Equal(CardDetailCommand.Save, save.Result);
        Assert.Equal(3, draft.BoardColumnId);
        Assert.Equal(card.Title, draft.Title);
        Assert.Equal(card.Description, draft.Description);
        Assert.Equal(card.TagNames, draft.TagNames);
        Assert.Equal(card.CardTypeId, draft.CardTypeId);
        Assert.Equal(card.AssignedUserId, draft.AssignedUserId);
        Assert.Equal(card.SlickName, draft.SlickName);
        Assert.Equal(card.ExternalUrl, draft.ExternalUrl);
    }

    [Fact]
    public void ColumnPicker_EscapeCancelsWithoutCreatingADraft()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.BoardColumn, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);

        var cancel = screen.HandleKey(Key(ConsoleKey.Escape), viewport);

        Assert.False(cancel.IsComplete);
        Assert.False(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Equal(card.BoardColumnId, screen.BoardColumnId);
    }

    [Fact]
    public void ColumnPicker_TabAcceptsAndAdvancesToCardType()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.BoardColumn, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.End), viewport);

        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);

        Assert.False(screen.IsEditing);
        Assert.Equal(CardDetailField.CardType, screen.FocusedField);
        Assert.Equal(3, Assert.IsType<CardDraft>(screen.PendingDraft).BoardColumnId);
    }

    [Fact]
    public void ColumnValidationErrorFocusesTheColumnAnchor()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 30);

        screen.SetSaveError(new BoardOilRequestException(
            HttpStatusCode.UnprocessableEntity,
            "Validation failed.",
            new Dictionary<string, string[]>
            {
                ["boardColumnId"] = ["Column does not exist in board."]
            }));
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var canvas = screen.Render(viewport).Canvas;
        var columnRow = layout.PaneContentTop + layout.ColumnFirstRow;

        Assert.Equal(CardDetailField.BoardColumn, screen.FocusedField);
        Assert.Equal("▌", canvas.CellAt(layout.AsideX, columnRow).Grapheme);
        Assert.Equal(BoardStyles.Selection, canvas.CellAt(layout.AsideX, columnRow).Foreground);
        Assert.Contains("Column does not exist in board.", PlainText(canvas));
    }

    [Fact]
    public void SaveRejectsAColumnThatIsNoLongerOnTheBoard()
    {
        var (sourceData, card) = DetailData();
        var data = sourceData with
        {
            Board = sourceData.Board with
            {
                Columns = sourceData.Board.Columns.Where(column => column.Id != card.BoardColumnId).ToArray()
            }
        };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);

        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.False(save.IsComplete);
        Assert.Equal(CardDetailField.BoardColumn, screen.FocusedField);
        Assert.Contains("Column is no longer available", rendered);
    }

    [Fact]
    public void CardTypePicker_PreviewsAndAcceptsATypeWithoutReplacingTheCardView()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.CardType, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        var opened = PlainText(screen.Render(viewport).Canvas);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        var preview = screen.Render(viewport);
        var bugStyle = BoardStyles.ResolveCard(data.CardTypes[29]);

        Assert.True(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Contains("CARD TYPE", opened);
        Assert.Contains("OilTTY", opened);
        Assert.Contains("Bug", opened);
        Assert.Contains("A detailed description", opened);
        Assert.Equal(29, screen.CardTypeId);
        Assert.Equal(bugStyle.BackgroundAt(40, viewport.Width), preview.Canvas.CellAt(40, 2).Background);

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);
        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);

        Assert.False(screen.IsEditing);
        Assert.True(save.IsComplete);
        Assert.Equal(CardDetailCommand.Save, save.Result);
        Assert.Equal(29, draft.CardTypeId);
        Assert.Equal(card.Title, draft.Title);
        Assert.Equal(card.Description, draft.Description);
        Assert.Equal(card.ExternalUrl, draft.ExternalUrl);
    }

    [Fact]
    public void CardTypePicker_EscapeCancelsWithoutCreatingADraft()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.CardType, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);

        var cancel = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var close = screen.HandleKey(Key(ConsoleKey.Escape), viewport);

        Assert.False(cancel.IsComplete);
        Assert.False(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Equal(card.CardTypeId, screen.CardTypeId);
        Assert.True(close.IsComplete);
        Assert.Equal(CardDetailCommand.Close, close.Result);
    }

    [Fact]
    public void CardTypePicker_TabAcceptsAndAdvancesToAssignedUser()
    {
        var (data, _) = DetailData();
        var screen = new CardDetailScreen(data, data.Board.Columns[0].Cards[0], "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.CardType, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.End), viewport);

        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);

        Assert.False(screen.IsEditing);
        Assert.Equal(CardDetailField.AssignedUser, screen.FocusedField);
        Assert.Equal(29, Assert.IsType<CardDraft>(screen.PendingDraft).CardTypeId);
    }

    [Fact]
    public void CardTypeValidationErrorFocusesTheTypeAnchor()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.CardType, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        screen.SetSaveError(new BoardOilRequestException(
            HttpStatusCode.UnprocessableEntity,
            "Validation failed.",
            new Dictionary<string, string[]>
            {
                ["cardTypeId"] = ["That card type is no longer available."]
            }));
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var canvas = screen.Render(viewport).Canvas;
        var typeRow = layout.PaneContentTop + layout.CardTypeFirstRow;

        Assert.Equal(CardDetailField.CardType, screen.FocusedField);
        Assert.Equal("▌", canvas.CellAt(layout.AsideX, typeRow).Grapheme);
        Assert.Equal(BoardStyles.Selection, canvas.CellAt(layout.AsideX, typeRow).Foreground);
        Assert.Contains("That card type is no longer available.", PlainText(canvas));
    }

    [Fact]
    public void AssigneePicker_PreviewsAndSavesAMemberWithoutReplacingTheCardView()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 30);
        FocusField(screen, CardDetailField.AssignedUser, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        var opened = PlainText(screen.Render(viewport).Canvas);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        var preview = PlainText(screen.Render(viewport).Canvas);

        Assert.True(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Contains("ASSIGNED TO", opened);
        Assert.Contains("Unassigned", opened);
        Assert.Contains("Luke", opened);
        Assert.Contains("Ada", opened);
        Assert.Contains("A detailed description", opened);
        Assert.Equal(8, screen.AssignedUserId);
        Assert.Contains("Ada", preview);

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);
        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);

        Assert.False(screen.IsEditing);
        Assert.True(save.IsComplete);
        Assert.Equal(CardDetailCommand.Save, save.Result);
        Assert.Equal(8, draft.AssignedUserId);
        Assert.Equal(card.Title, draft.Title);
        Assert.Equal(card.Description, draft.Description);
        Assert.Equal(card.TagNames, draft.TagNames);
        Assert.Equal(card.CardTypeId, draft.CardTypeId);
        Assert.Equal(card.BoardColumnId, draft.BoardColumnId);
        Assert.Equal(card.SlickName, draft.SlickName);
        Assert.Equal(card.ExternalUrl, draft.ExternalUrl);
    }

    [Fact]
    public void AssigneePicker_CanExplicitlyUnassignTheCard()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.AssignedUser, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.Home), viewport);

        Assert.Null(screen.AssignedUserId);
        Assert.False(screen.HasDraft);

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.Null(draft.AssignedUserId);
        Assert.Contains("Unassigned", rendered);
    }

    [Fact]
    public void AssigneePicker_EscapeCancelsWithoutCreatingADraft()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.AssignedUser, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);

        var cancel = screen.HandleKey(Key(ConsoleKey.Escape), viewport);

        Assert.False(cancel.IsComplete);
        Assert.False(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Equal(card.AssignedUserId, screen.AssignedUserId);
    }

    [Fact]
    public void AssigneePicker_TabAcceptsAndAdvancesToSlick()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.AssignedUser, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.End), viewport);

        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);

        Assert.False(screen.IsEditing);
        Assert.Equal(CardDetailField.Slick, screen.FocusedField);
        Assert.Equal(8, Assert.IsType<CardDraft>(screen.PendingDraft).AssignedUserId);
    }

    [Fact]
    public void SlickPicker_PreviewsAndSavesAnExistingSlickWithItsColour()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 30);
        FocusSlick(screen, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        var opened = PlainText(screen.Render(viewport).Canvas);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);
        var preview = screen.Render(viewport);
        var expectedColour = BoardStyles.ResolveSlick(data.Slicks[13], 13);

        Assert.True(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Contains("No slick", opened);
        Assert.Contains("Editor flow", opened);
        Assert.Contains("Release train", opened);
        Assert.Contains("A detailed description", opened);
        Assert.Equal("Release train", screen.SlickName);
        Assert.True(CanvasContains(preview.Canvas, "●", expectedColour));

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);
        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);

        Assert.False(screen.IsEditing);
        Assert.True(save.IsComplete);
        Assert.Equal(CardDetailCommand.Save, save.Result);
        Assert.Equal("Release train", draft.SlickName);
        Assert.Equal(card.Title, draft.Title);
        Assert.Equal(card.Description, draft.Description);
        Assert.Equal(card.TagNames, draft.TagNames);
        Assert.Equal(card.CardTypeId, draft.CardTypeId);
        Assert.Equal(card.BoardColumnId, draft.BoardColumnId);
        Assert.Equal(card.AssignedUserId, draft.AssignedUserId);
        Assert.Equal(card.ExternalUrl, draft.ExternalUrl);
    }

    [Fact]
    public void SlickPicker_CanExplicitlyClearTheSlick()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusSlick(screen, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.Home), viewport);

        Assert.Null(screen.SlickName);
        Assert.False(screen.HasDraft);

        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.Null(draft.SlickName);
        Assert.Contains("None", rendered);
    }

    [Fact]
    public void SlickPicker_EscapeCancelsWithoutCreatingADraft()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusSlick(screen, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.DownArrow), viewport);

        var cancel = screen.HandleKey(Key(ConsoleKey.Escape), viewport);

        Assert.False(cancel.IsComplete);
        Assert.False(screen.IsEditing);
        Assert.False(screen.HasDraft);
        Assert.Equal(card.SlickName, screen.SlickName);
    }

    [Fact]
    public void SlickPicker_TabAcceptsAndAdvancesToExternalUrl()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusSlick(screen, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.End), viewport);

        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);

        Assert.False(screen.IsEditing);
        Assert.Equal(CardDetailField.ExternalUrl, screen.FocusedField);
        Assert.Equal("Release train", Assert.IsType<CardDraft>(screen.PendingDraft).SlickName);
    }

    [Fact]
    public void SlickValidationErrorFocusesTheSlickAnchor()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 30);

        screen.SetSaveError(new BoardOilRequestException(
            HttpStatusCode.UnprocessableEntity,
            "Validation failed.",
            new Dictionary<string, string[]>
            {
                ["slickName"] = ["That slick is no longer available."]
            }));
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var canvas = screen.Render(viewport).Canvas;
        var slickRow = layout.PaneContentTop + layout.SlickFirstRow - screen.OptionsScroll;

        Assert.Equal(CardDetailField.Slick, screen.FocusedField);
        Assert.Equal("▌", canvas.CellAt(layout.AsideX, slickRow).Grapheme);
        Assert.Equal(BoardStyles.Selection, canvas.CellAt(layout.AsideX, slickRow).Foreground);
        Assert.Contains("That slick is no longer available.", PlainText(canvas));
    }

    [Fact]
    public void SaveRejectsASlickThatIsNoLongerLoadedForTheBoard()
    {
        var (sourceData, card) = DetailData();
        var data = sourceData with { Slicks = new Dictionary<int, SlickDefinition>() };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);

        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.False(save.IsComplete);
        Assert.Equal(CardDetailField.Slick, screen.FocusedField);
        Assert.Contains("no longer available", rendered);
    }

    [Fact]
    public void AssignedUserValidationErrorFocusesTheAssigneeAnchor()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 30);

        screen.SetSaveError(new BoardOilRequestException(
            HttpStatusCode.UnprocessableEntity,
            "Validation failed.",
            new Dictionary<string, string[]>
            {
                ["assignedUserId"] = ["Assigned user must be an active board member."]
            }));
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var canvas = screen.Render(viewport).Canvas;
        var assignedUserRow = layout.PaneContentTop + layout.AssignedUserFirstRow;

        Assert.Equal(CardDetailField.AssignedUser, screen.FocusedField);
        Assert.Equal("▌", canvas.CellAt(layout.AsideX, assignedUserRow).Grapheme);
        Assert.Equal(BoardStyles.Selection, canvas.CellAt(layout.AsideX, assignedUserRow).Foreground);
        Assert.Contains("Assigned user must be an active board member.", PlainText(canvas));
    }

    [Fact]
    public void SaveRejectsAnAssigneeWhoIsNoLongerAnActiveBoardMember()
    {
        var (sourceData, card) = DetailData();
        var data = sourceData with { Members = [] };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);

        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.False(save.IsComplete);
        Assert.Equal(CardDetailField.AssignedUser, screen.FocusedField);
        Assert.Contains("no longer an active member", rendered);
    }

    [Fact]
    public void TitleEdit_PreviewsAndSavesWithinTheSharedDraft()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);
        var editingFrame = screen.Render(viewport);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        var preview = PlainText(screen.Render(viewport).Canvas);
        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);

        Assert.NotNull(editingFrame.Cursor);
        Assert.EndsWith("X", draft.Title);
        Assert.Contains(draft.Title, preview);
        Assert.Equal(card.Description, draft.Description);
        Assert.True(save.IsComplete);
        Assert.Equal(CardDetailCommand.Save, save.Result);
    }

    [Fact]
    public void ExternalUrlEdit_NormalisesBlankSpaceAndAcceptsHttpUrls()
    {
        var (data, sourceCard) = DetailData();
        var card = sourceCard with { ExternalUrl = null };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.ExternalUrl, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        Type(screen, " https://boardoil.test/card/802 ", viewport);
        var editingFrame = screen.Render(viewport);
        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);
        var draft = Assert.IsType<CardDraft>(screen.PendingDraft);

        Assert.NotNull(editingFrame.Cursor);
        Assert.True(save.IsComplete);
        Assert.Equal("https://boardoil.test/card/802", draft.ExternalUrl);
        Assert.Equal(card.Title, draft.Title);
        Assert.Equal(card.Description, draft.Description);
    }

    [Fact]
    public void ExternalUrlEdit_FirstEscapeFinishesEditingAndPreservesTheDraft()
    {
        var (data, sourceCard) = DetailData();
        var card = sourceCard with { ExternalUrl = null };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.ExternalUrl, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        Type(screen, "https://boardoil.test/card/802", viewport);

        var finishEditing = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var warning = screen.HandleKey(Key(ConsoleKey.Escape), viewport);

        Assert.False(finishEditing.IsComplete);
        Assert.False(screen.IsEditing);
        Assert.Equal("https://boardoil.test/card/802", screen.ExternalUrl);
        Assert.False(warning.IsComplete);
        Assert.True(screen.IsConfirmingDiscard);
    }

    [Fact]
    public void InvalidExternalUrl_StaysInTheDraftAndFocusesTheField()
    {
        var (data, sourceCard) = DetailData();
        var card = sourceCard with { ExternalUrl = null };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        FocusField(screen, CardDetailField.ExternalUrl, viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        Type(screen, "boardoil.test/card/802", viewport);

        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.False(save.IsComplete);
        Assert.True(screen.HasDraft);
        Assert.Equal(CardDetailField.ExternalUrl, screen.FocusedField);
        Assert.Contains("External URL must", rendered);
    }

    [Fact]
    public void BlankTitle_CannotBeSavedAndFocusesTheTitle()
    {
        var (data, sourceCard) = DetailData();
        var card = sourceCard with { Title = "   " };
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);
        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.False(save.IsComplete);
        Assert.True(screen.HasDraft);
        Assert.Equal(CardDetailField.Title, screen.FocusedField);
        Assert.Contains("Card title is required.", rendered);
    }

    [Fact]
    public void Escape_WithEditedCardDraft_RequiresConfirmationBeforeClosing()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);
        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);
        screen.HandleKey(Key(ConsoleKey.Tab, '\t', shift: true), viewport);
        screen.HandleKey(Key(ConsoleKey.X, 'X'), viewport);

        var warning = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var warningFrame = screen.Render(viewport);
        var keepEditing = screen.HandleKey(Key(ConsoleKey.N, 'n'), viewport);
        var warningAgain = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var close = screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        Assert.False(warning.IsComplete);
        Assert.True(screen.HasDraft);
        Assert.Contains("Discard changes?", PlainText(warningFrame.Canvas));
        Assert.Contains("Unsaved changes will be lost.", PlainText(warningFrame.Canvas));
        Assert.Null(warningFrame.Cursor);
        Assert.False(keepEditing.IsComplete);
        Assert.False(warningAgain.IsComplete);
        Assert.True(close.IsComplete);
        Assert.Equal(CardDetailCommand.Close, close.Result);
    }

    [Fact]
    public void Escape_WithUnchangedEditDraft_FinishesEditingThenClosesWithoutConfirmation()
    {
        var (data, card) = DetailData();
        var screen = new CardDetailScreen(data, card, "connected");
        var viewport = new TerminalViewport(80, 24);
        screen.HandleKey(Key(ConsoleKey.Enter, '\r'), viewport);

        var finishEditing = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var close = screen.HandleKey(Key(ConsoleKey.Escape), viewport);

        Assert.False(finishEditing.IsComplete);
        Assert.True(close.IsComplete);
        Assert.Equal(CardDetailCommand.Close, close.Result);
        Assert.False(screen.IsConfirmingDiscard);
        Assert.False(screen.HasUnsavedChanges);
    }

    [Fact]
    public void CreateNew_UsesTheSystemCardTypeAndSelectedColumnDefaults()
    {
        var (data, _) = DetailData();

        var draft = CardDraft.CreateNew(data, 3);

        Assert.NotNull(draft);
        Assert.Equal(string.Empty, draft.Title);
        Assert.Equal(string.Empty, draft.Description);
        Assert.Empty(draft.TagNames);
        Assert.Equal(28, draft.CardTypeId);
        Assert.Equal(3, draft.BoardColumnId);
        Assert.Null(draft.AssignedUserId);
        Assert.Null(draft.SlickName);
        Assert.Null(draft.ExternalUrl);
    }

    [Fact]
    public void CreateScreen_StartsEditingTitleAndOmitsSyntheticIdentityAndTimestamps()
    {
        var (data, _) = DetailData();
        var draft = CardDraft.CreateNew(data, 3)!;
        var screen = new CardDetailScreen(data, draft, "connected");
        var viewport = new TerminalViewport(80, 24);

        var rendered = PlainText(screen.Render(viewport).Canvas);

        Assert.True(screen.IsNew);
        Assert.True(screen.IsEditing);
        Assert.Equal(CardDetailField.Title, screen.FocusedField);
        Assert.Equal(CardDetailField.Title, screen.EditingField);
        Assert.Contains("New card", rendered);
        Assert.DoesNotContain("Card #0", rendered);
        Assert.DoesNotContain("CREATED", rendered);
        Assert.DoesNotContain("UPDATED", rendered);
        Assert.Contains("Done", rendered);
        Assert.Contains("OilTTY", rendered);
    }

    [Fact]
    public void CreateScreen_CtrlSReturnsCompleteValidatedDraft()
    {
        var (data, _) = DetailData();
        var screen = new CardDetailScreen(data, CardDraft.CreateNew(data, 2)!, "connected");
        var viewport = new TerminalViewport(80, 24);
        Type(screen, "  Fresh card  ", viewport);

        var save = screen.HandleKey(Key(ConsoleKey.S, 's', control: true), viewport);

        Assert.True(save.IsComplete);
        Assert.Equal(CardDetailCommand.Save, save.Result);
        Assert.Equal("Fresh card", screen.PendingDraft!.Title);
        Assert.Equal(2, screen.PendingDraft.BoardColumnId);
        Assert.Equal(28, screen.PendingDraft.CardTypeId);
    }

    [Fact]
    public void CreateScreen_EscapeWithDirtyDraftRequiresConfirmation()
    {
        var (data, _) = DetailData();
        var screen = new CardDetailScreen(data, CardDraft.CreateNew(data, 2)!, "connected");
        var viewport = new TerminalViewport(80, 24);
        Type(screen, "Unsaved", viewport);

        var finishEditing = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var warning = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var cancel = screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        screen.HandleKey(Key(ConsoleKey.Escape), viewport);
        var close = screen.HandleKey(Key(ConsoleKey.Y, 'y'), viewport);

        Assert.False(finishEditing.IsComplete);
        Assert.False(warning.IsComplete);
        Assert.False(cancel.IsComplete);
        Assert.True(screen.HasUnsavedChanges);
        Assert.True(close.IsComplete);
        Assert.Equal(CardDetailCommand.Close, close.Result);
        Assert.Equal("Unsaved", screen.PendingDraft!.Title);
    }

    [Fact]
    public void CreateScreen_EscapeWithoutChangesFinishesEditingThenCloses()
    {
        var (data, _) = DetailData();
        var screen = new CardDetailScreen(data, CardDraft.CreateNew(data, 2)!, "connected");

        var finishEditing = screen.HandleKey(
            Key(ConsoleKey.Escape),
            new TerminalViewport(80, 24));
        var close = screen.HandleKey(
            Key(ConsoleKey.Escape),
            new TerminalViewport(80, 24));

        Assert.False(finishEditing.IsComplete);
        Assert.True(close.IsComplete);
        Assert.Equal(CardDetailCommand.Close, close.Result);
        Assert.False(screen.IsConfirmingDiscard);
    }

    [Fact]
    public void CreateScreen_ServerValidationFocusesTheRelevantField()
    {
        var (data, _) = DetailData();
        var screen = new CardDetailScreen(data, CardDraft.CreateNew(data, 2)!, "connected");
        screen.SetSaveError(new BoardOilRequestException(
            HttpStatusCode.UnprocessableEntity,
            "Validation failed.",
            new Dictionary<string, string[]>
            {
                ["boardColumnId"] = ["Column does not exist in board."]
            }));

        Assert.Equal(CardDetailField.BoardColumn, screen.FocusedField);
        Assert.Contains(
            "Column does not exist in board.",
            PlainText(screen.Render(new TerminalViewport(80, 24)).Canvas));
    }

    private static (BoardData Data, BoardCard Card) DetailData()
    {
        var tag = new CardTag(
            4,
            "UI",
            "solid",
            "{\"backgroundColor\":\"#26A269\",\"textColorMode\":\"auto\"}",
            "✨");
        var featureTag = new CardTag(
            5,
            "Feature",
            "solid",
            "{\"backgroundColor\":\"#385688\",\"textColorMode\":\"auto\"}",
            "🎬");
        var techDebtTag = new CardTag(
            6,
            "Tech Debt",
            "solid",
            "{\"backgroundColor\":\"#8B5C2A\",\"textColorMode\":\"auto\"}",
            "💰️");
        var card = TestBoardFactory.Card(802, 2, "Open and edit a selected card", [tag], 12) with
        {
            CardTypeId = 28,
            CardTypeName = "OilTTY",
            CardTypeEmoji = "⌨️",
            Description = "A detailed description that remains the primary content.",
            CardCreatedUtc = new DateTime(2026, 8, 20, 10, 15, 0, DateTimeKind.Utc),
            CardUpdatedUtc = new DateTime(2026, 8, 28, 17, 0, 0, DateTimeKind.Utc),
            AssignedUserId = 7,
            AssignedUserDisplayName = "Luke",
            ExternalUrl = "https://example.test/802",
            SlickName = "Editor flow"
        };
        var board = new BoardSnapshot(
            1,
            "Test board",
            string.Empty,
            true,
            "Owner",
            [
                new BoardColumn(2, "In progress", "2", [card]),
                new BoardColumn(3, "Done", "3", [])
            ]);
        var data = new BoardData(
            board,
            new Dictionary<int, CardTypeDefinition>
            {
                [28] = new(28, "OilTTY", "⌨️", "auto", "{}", IsSystem: true),
                [29] = new(
                    29,
                    "Bug",
                    "🐞",
                    "solid",
                    "{\"backgroundColor\":\"#813D4B\",\"textColorMode\":\"auto\",\"borderMode\":\"none\"}")
            },
            new Dictionary<int, SlickDefinition>
            {
                [12] = new(12, "Editor flow", "solid", "{\"backgroundColor\":\"#385688\"}"),
                [13] = new(13, "Release train", "solid", "{\"backgroundColor\":\"#8B5C2A\"}")
            },
            [tag, featureTag, techDebtTag],
            [
                new BoardMember(
                    7,
                    "luke",
                    "Luke",
                    null,
                    "Owner",
                    new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc)),
                new BoardMember(
                    8,
                    "ada",
                    "Ada",
                    null,
                    "Contributor",
                    new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc))
            ]);
        return (data, card);
    }

    private static void FocusSlick(CardDetailScreen screen, TerminalViewport viewport)
    {
        FocusField(screen, CardDetailField.Slick, viewport);
    }

    private static void FocusField(
        CardDetailScreen screen,
        CardDetailField field,
        TerminalViewport viewport)
    {
        for (var count = 0; count < 8 && screen.FocusedField != field; count++)
        {
            screen.HandleKey(Key(ConsoleKey.Tab, '\t'), viewport);
        }

        Assert.Equal(field, screen.FocusedField);
    }

    private static bool CanvasContains(TerminalCanvas canvas, string grapheme, Rgb foreground)
    {
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var cell = canvas.CellAt(x, y);
                if (cell.Grapheme == grapheme && cell.Foreground == foreground)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CanvasContainsBackground(TerminalCanvas canvas, Rgb background)
    {
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                if (canvas.CellAt(x, y).Background == background)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CanvasHasCheckBeforeTagPill(TerminalCanvas canvas)
    {
        for (var y = 0; y < canvas.Height; y++)
        {
            var checkX = -1;
            for (var x = 0; x < canvas.Width; x++)
            {
                var grapheme = canvas.CellAt(x, y).Grapheme;
                if (grapheme == "✓")
                {
                    checkX = x;
                }
                else if (grapheme == "▐" && checkX >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Text(IReadOnlyList<CardDetailLine> lines) =>
        string.Join('\n', lines.Select(line => string.Concat(line.Spans.Select(span => span.Text))));

    private static string PlainText(TerminalCanvas canvas)
    {
        var result = new StringBuilder();
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var cell = canvas.CellAt(x, y);
                if (!cell.Continuation)
                {
                    result.Append(cell.Grapheme);
                }
            }

            result.AppendLine();
        }

        return result.ToString();
    }

    private static void AssertInOrder(string source, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var index = source.IndexOf(value, previous + 1, StringComparison.Ordinal);
            Assert.True(index > previous, $"Expected '{value}' after index {previous}.\n{source}");
            previous = index;
        }
    }

    private static void Type(
        CardDetailScreen screen,
        string value,
        TerminalViewport viewport)
    {
        foreach (var character in value)
        {
            screen.HandleKey(Key(ConsoleKey.A, character), viewport);
        }
    }

    private static ConsoleKeyInfo Key(
        ConsoleKey key,
        char character = '\0',
        bool shift = false,
        bool control = false) =>
        new(character, key, shift, alt: false, control);
}
