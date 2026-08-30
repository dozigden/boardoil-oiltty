internal sealed class CardDetailRenderer
{
    public TerminalFrame Render(
        BoardData data,
        BoardCard card,
        CardDetailLayout layout,
        CardDetailPane activePane,
        CardDetailField focusedField,
        CardDetailField? editingField,
        InlineMultiChoicePicker<CardTag>? tagPicker,
        InlineChoicePicker<BoardColumn>? columnPicker,
        InlineChoicePicker<CardTypeDefinition>? cardTypePicker,
        InlineChoicePicker<AssigneeChoice>? assigneePicker,
        InlineChoicePicker<SlickChoice>? slickPicker,
        bool hasDraft,
        bool isNew,
        bool confirmingDiscard,
        int requestedDescriptionScroll,
        int requestedOptionsScroll,
        string status,
        string? feedback,
        bool feedbackIsError)
    {
        var canvas = new TerminalCanvas(
            layout.Width,
            layout.Height,
            BoardStyles.TextStrong,
            BoardStyles.RootBackground);
        canvas.Fill(0, 0, canvas.Width, canvas.Height, BoardStyles.RootBackground);
        var cursor = DrawChrome(
            canvas,
            data,
            card,
            layout,
            status,
            focusedField,
            editingField,
            tagPicker is not null
                || columnPicker is not null
                || cardTypePicker is not null
                || assigneePicker is not null
                || slickPicker is not null,
            tagPicker is not null,
            hasDraft,
            isNew,
            feedback,
            feedbackIsError);

        var descriptionScroll = Math.Clamp(
            requestedDescriptionScroll,
            0,
            layout.DescriptionMaxScroll);
        var optionsScroll = Math.Clamp(requestedOptionsScroll, 0, layout.OptionsMaxScroll);
        if (layout.IsWide)
        {
            var descriptionCursor = DrawDescriptionPane(
                canvas,
                layout,
                descriptionScroll,
                focusedField == CardDetailField.Description,
                editingField == CardDetailField.Description,
                feedback: null,
                feedbackIsError);
            cursor ??= descriptionCursor;
            var optionsCursor = DrawOptionsPane(
                canvas,
                layout,
                optionsScroll,
                focusedField,
                editingField,
                feedback: null,
                feedbackIsError);
            cursor ??= optionsCursor;

            var dividerX = layout.AsideX - 2;
            for (var y = layout.ContentTop; y < layout.ContentBottom; y++)
            {
                canvas.Put(dividerX, y, "│", BoardStyles.BorderSoft);
            }
        }
        else if (activePane == CardDetailPane.Description)
        {
            var descriptionCursor = DrawDescriptionPane(
                canvas,
                layout,
                descriptionScroll,
                focusedField == CardDetailField.Description,
                editingField == CardDetailField.Description,
                feedback,
                feedbackIsError);
            cursor ??= descriptionCursor;
        }
        else
        {
            var optionsCursor = DrawOptionsPane(
                canvas,
                layout,
                optionsScroll,
                focusedField,
                editingField,
                feedback,
                feedbackIsError);
            cursor ??= optionsCursor;
        }

        if (tagPicker is not null)
        {
            DrawMultiChoicePicker(
                canvas,
                layout,
                tagPicker,
                optionsScroll,
                layout.TagsFirstRow,
                layout.TagsLastRow,
                "TAGS",
                item => item.Name,
                spansFor: BuildTagChoiceSpans);
        }
        else if (columnPicker is not null)
        {
            DrawChoicePicker(
                canvas,
                layout,
                columnPicker,
                optionsScroll,
                layout.ColumnFirstRow,
                layout.ColumnLastRow,
                "COLUMN",
                item => item.Title);
        }
        else if (cardTypePicker is not null)
        {
            DrawChoicePicker(
                canvas,
                layout,
                cardTypePicker,
                optionsScroll,
                layout.CardTypeFirstRow,
                layout.CardTypeLastRow,
                "CARD TYPE",
                item => $"{UnicodeDisplay.EmojiLabelPrefix(item.Emoji ?? string.Empty)}{item.Name}");
        }
        else if (assigneePicker is not null)
        {
            DrawChoicePicker(
                canvas,
                layout,
                assigneePicker,
                optionsScroll,
                layout.AssignedUserFirstRow,
                layout.AssignedUserLastRow,
                "ASSIGNED TO",
                item => item.UserId is null
                    ? item.DisplayName
                    : $"{UnicodeDisplay.EmojiLabelPrefix("👤")}{item.DisplayName}");
        }
        else if (slickPicker is not null)
        {
            DrawChoicePicker(
                canvas,
                layout,
                slickPicker,
                optionsScroll,
                layout.SlickFirstRow,
                layout.SlickLastRow,
                "SLICK",
                item => item.Name ?? "No slick",
                item => item.Id is int slickId && item.Definition is not null
                    ? BoardStyles.ResolveSlick(item.Definition, slickId)
                    : null);
        }

        if (confirmingDiscard)
        {
            DrawDiscardConfirmation(canvas);
            cursor = null;
        }

        return new TerminalFrame(canvas, cursor);
    }

    private static void DrawDiscardConfirmation(TerminalCanvas canvas)
    {
        var width = Math.Min(50, canvas.Width - 4);
        const int height = 8;
        var x = (canvas.Width - width) / 2;
        var top = Math.Clamp((canvas.Height - height) / 2, 1, canvas.Height - height - 1);
        var bottom = top + height - 1;
        var right = x + width - 1;

        canvas.Fill(x, top, width, height, BoardStyles.PanelBackground);
        canvas.Put(x, top, "╭", BoardStyles.Danger, BoardStyles.PanelBackground);
        canvas.Put(right, top, "╮", BoardStyles.Danger, BoardStyles.PanelBackground);
        canvas.Put(x, bottom, "╰", BoardStyles.Danger, BoardStyles.PanelBackground);
        canvas.Put(right, bottom, "╯", BoardStyles.Danger, BoardStyles.PanelBackground);
        for (var offset = 1; offset < width - 1; offset++)
        {
            canvas.Put(x + offset, top, "─", BoardStyles.Danger, BoardStyles.PanelBackground);
            canvas.Put(x + offset, bottom, "─", BoardStyles.Danger, BoardStyles.PanelBackground);
        }

        for (var row = top + 1; row < bottom; row++)
        {
            canvas.Put(x, row, "│", BoardStyles.Danger, BoardStyles.PanelBackground);
            canvas.Put(right, row, "│", BoardStyles.Danger, BoardStyles.PanelBackground);
        }

        canvas.Put(
            x + 2,
            top,
            " Discard changes? ",
            BoardStyles.Danger,
            BoardStyles.PanelBackground,
            bold: true,
            maxWidth: width - 4);
        canvas.Put(
            x + 2,
            top + 2,
            "Unsaved changes will be lost.",
            BoardStyles.TextStrong,
            BoardStyles.PanelBackground,
            maxWidth: width - 4);
        canvas.Put(x + 2, top + 4, "enter/y", BoardStyles.Selection, BoardStyles.PanelBackground, bold: true);
        canvas.Put(x + 10, top + 4, "discard", BoardStyles.Danger, BoardStyles.PanelBackground);
        canvas.Put(x + 2, top + 5, "esc/n", BoardStyles.Selection, BoardStyles.PanelBackground, bold: true);
        canvas.Put(x + 8, top + 5, "keep editing", BoardStyles.TextMuted, BoardStyles.PanelBackground);
    }

    private static TerminalCursor? DrawDescriptionPane(
        TerminalCanvas canvas,
        CardDetailLayout layout,
        int scroll,
        bool focused,
        bool editing,
        string? feedback,
        bool feedbackIsError)
    {
        var x = layout.MainX;
        var width = layout.MainWidth;
        var background = layout.IsWide ? BoardStyles.PanelBackground : BoardStyles.RootBackground;
        canvas.Fill(x, layout.ContentTop, width, layout.ContentBottom - layout.ContentTop, background);
        DrawPaneHeading(
            canvas,
            x,
            layout.ContentTop,
            width,
            editing ? "DESCRIPTION · EDITING" : "DESCRIPTION",
            focused,
            showAnchor: true,
            background,
            feedback,
            feedbackIsError);

        var contentX = layout.IsWide ? x + 1 : x;
        var contentWidth = layout.IsWide ? width - 2 : width;
        DrawLines(
            canvas,
            layout.DescriptionLines,
            contentX,
            contentWidth,
            layout.PaneContentTop,
            layout.ContentBottom,
            scroll,
            background);
        DrawScrollIndicator(
            canvas,
            x + width - 1,
            layout.PaneContentTop,
            layout.PaneViewportRows,
            layout.DescriptionLines.Count,
            layout.DescriptionMaxScroll,
            scroll);

        if (!editing
            || layout.DescriptionCursorRow is not int cursorRow
            || layout.DescriptionCursorColumn is not int cursorColumn)
        {
            return null;
        }

        var cursorY = layout.PaneContentTop + cursorRow - scroll;
        if (cursorY < layout.PaneContentTop || cursorY >= layout.ContentBottom)
        {
            return null;
        }

        return new TerminalCursor(contentX + cursorColumn, cursorY);
    }

    private static TerminalCursor? DrawOptionsPane(
        TerminalCanvas canvas,
        CardDetailLayout layout,
        int scroll,
        CardDetailField focusedField,
        CardDetailField? editingField,
        string? feedback,
        bool feedbackIsError)
    {
        var x = layout.AsideX;
        var width = layout.AsideWidth;
        var selectedField = focusedField is CardDetailField.Tags
            or CardDetailField.BoardColumn
            or CardDetailField.CardType
            or CardDetailField.AssignedUser
            or CardDetailField.Slick
            or CardDetailField.ExternalUrl
            ? focusedField
            : (CardDetailField?)null;
        var urlEditing = editingField == CardDetailField.ExternalUrl;
        var choiceEditing = editingField is CardDetailField.Tags
            or CardDetailField.BoardColumn
            or CardDetailField.CardType
            or CardDetailField.AssignedUser
            or CardDetailField.Slick;
        DrawPaneHeading(
            canvas,
            x,
            layout.ContentTop,
            width,
            choiceEditing ? "DETAILS · CHOOSING" : urlEditing ? "DETAILS · EDITING" : "DETAILS",
            focused: false,
            showAnchor: false,
            BoardStyles.RootBackground,
            feedback,
            feedbackIsError);
        DrawLines(
            canvas,
            layout.OptionsLines,
            x,
            width,
            layout.PaneContentTop,
            layout.ContentBottom,
            scroll,
            BoardStyles.RootBackground,
            selectedField);
        DrawScrollIndicator(
            canvas,
            x + width - 1,
            layout.PaneContentTop,
            layout.PaneViewportRows,
            layout.OptionsLines.Count,
            layout.OptionsMaxScroll,
            scroll);

        if (!urlEditing
            || layout.ExternalUrlCursorRow is not int cursorRow
            || layout.ExternalUrlCursorColumn is not int cursorColumn)
        {
            return null;
        }

        var cursorY = layout.PaneContentTop + cursorRow - scroll;
        if (cursorY < layout.PaneContentTop || cursorY >= layout.ContentBottom)
        {
            return null;
        }

        return new TerminalCursor(x + 2 + cursorColumn, cursorY);
    }

    private static void DrawPaneHeading(
        TerminalCanvas canvas,
        int x,
        int y,
        int width,
        string label,
        bool focused,
        bool showAnchor,
        Rgb background,
        string? feedback,
        bool feedbackIsError)
    {
        canvas.Put(
            x,
            y,
            showAnchor ? "▌" : " ",
            focused ? BoardStyles.Selection : BoardStyles.FieldAnchorPlaceholder,
            background,
            bold: focused);
        canvas.Put(x + 2, y, label, BoardStyles.TextStrong, background, bold: true, maxWidth: width - 2);
        if (string.IsNullOrWhiteSpace(feedback))
        {
            return;
        }

        var labelEnd = x + 2 + UnicodeDisplay.TextWidth(label) + 2;
        var available = Math.Max(0, x + width - labelEnd - 1);
        if (available > 0)
        {
            canvas.Put(
                labelEnd,
                y,
                UnicodeDisplay.Truncate(feedback, available),
                feedbackIsError ? BoardStyles.Danger : BoardStyles.Connected,
                background,
                bold: true,
                maxWidth: available);
        }
    }

    private static TerminalCursor? DrawChrome(
        TerminalCanvas canvas,
        BoardData data,
        BoardCard card,
        CardDetailLayout layout,
        string status,
        CardDetailField focusedField,
        CardDetailField? editingField,
        bool choosing,
        bool multiChoosing,
        bool hasDraft,
        bool isNew,
        string? feedback,
        bool feedbackIsError)
    {
        canvas.HorizontalLine(0, canvas.Height - 2, canvas.Width, "─", BoardStyles.BorderSoft);
        ScreenChromeRenderer.DrawTopRow(
            canvas,
            isNew ? "New card" : $"Card #{card.Id}",
            status);

        var titleFocused = focusedField == CardDetailField.Title;
        var titleEditing = editingField == CardDetailField.Title;
        data.CardTypes.TryGetValue(card.CardTypeId, out var cardType);
        var cardTypeStyle = BoardStyles.ResolveCard(cardType);
        DrawExpandedCardHeader(canvas, layout, cardTypeStyle);
        for (var index = 0; index < layout.TitleLines.Count; index++)
        {
            var row = 2 + index;
            var x = 2;
            var logicalRow = layout.TitleWindowStart + index;
            if (logicalRow == 0 && !string.IsNullOrWhiteSpace(card.CardTypeEmoji))
            {
                var prefix = UnicodeDisplay.EmojiLabelPrefix(card.CardTypeEmoji);
                canvas.Put(x, row, prefix, cardTypeStyle.Foreground, bold: true);
                x += UnicodeDisplay.TextWidth(prefix);
            }

            canvas.Put(
                x,
                row,
                logicalRow == 0 ? "▌" : " ",
                titleFocused ? BoardStyles.Selection : BoardStyles.FieldAnchorPlaceholder,
                bold: titleFocused && logicalRow == 0);
            x += 2;

            canvas.Put(
                x,
                row,
                layout.TitleLines[index],
                cardTypeStyle.Foreground,
                bold: true,
                maxWidth: canvas.Width - x - 2);
        }

        DrawFooter(
            canvas,
            focusedField,
            editingField is not null,
            choosing,
            multiChoosing,
            hasDraft,
            feedback,
            feedbackIsError);

        if (!titleEditing
            || layout.TitleCursorRow is not int cursorRow
            || layout.TitleCursorColumn is not int cursorColumn)
        {
            return null;
        }

        var cursorX = 4 + cursorColumn;
        if (layout.TitleWindowStart + cursorRow == 0
            && !string.IsNullOrWhiteSpace(card.CardTypeEmoji))
        {
            cursorX += UnicodeDisplay.TextWidth(UnicodeDisplay.EmojiLabelPrefix(card.CardTypeEmoji));
        }

        return new TerminalCursor(cursorX, 2 + cursorRow);
    }

    private static void DrawExpandedCardHeader(
        TerminalCanvas canvas,
        CardDetailLayout layout,
        SurfaceStyle style)
    {
        const int top = 1;
        var bottom = layout.ContentTop - 1;
        var height = bottom - top + 1;
        canvas.FillGradient(0, top, canvas.Width, height, style);
        if (!style.ShowBorder)
        {
            return;
        }

        var foreground = style.Border;
        canvas.Put(0, top, "╭", foreground);
        canvas.Put(canvas.Width - 1, top, "╮", foreground);
        canvas.Put(0, bottom, "╰", foreground);
        canvas.Put(canvas.Width - 1, bottom, "╯", foreground);
        for (var x = 1; x < canvas.Width - 1; x++)
        {
            canvas.Put(x, top, "─", foreground);
            canvas.Put(x, bottom, "─", foreground);
        }

        for (var y = top + 1; y < bottom; y++)
        {
            canvas.Put(0, y, "│", foreground);
            canvas.Put(canvas.Width - 1, y, "│", foreground);
        }
    }

    private static void DrawChoicePicker<T>(
        TerminalCanvas canvas,
        CardDetailLayout layout,
        InlineChoicePicker<T> picker,
        int optionsScroll,
        int fieldFirstRow,
        int fieldLastRow,
        string title,
        Func<T, string> labelFor,
        Func<T, Rgb?>? accentFor = null) =>
        DrawChoicePickerCore(
            canvas,
            layout,
            picker,
            picker.Items,
            index => index == picker.SelectedIndex,
            optionsScroll,
            fieldFirstRow,
            fieldLastRow,
            title,
            labelFor,
            accentFor);

    private static void DrawMultiChoicePicker<T>(
        TerminalCanvas canvas,
        CardDetailLayout layout,
        InlineMultiChoicePicker<T> picker,
        int optionsScroll,
        int fieldFirstRow,
        int fieldLastRow,
        string title,
        Func<T, string> labelFor,
        Func<T, int, IReadOnlyList<CardDetailSpan>>? spansFor = null) =>
        DrawChoicePickerCore(
            canvas,
            layout,
            picker,
            picker.Items,
            picker.IsSelected,
            optionsScroll,
            fieldFirstRow,
            fieldLastRow,
            title,
            labelFor,
            spansFor: spansFor,
            selectionMarkerOnLeft: true);

    private static void DrawChoicePickerCore<T>(
        TerminalCanvas canvas,
        CardDetailLayout layout,
        IInlineChoicePicker picker,
        IReadOnlyList<T> items,
        Func<int, bool> isSelected,
        int optionsScroll,
        int fieldFirstRow,
        int fieldLastRow,
        string title,
        Func<T, string> labelFor,
        Func<T, Rgb?>? accentFor = null,
        Func<T, int, IReadOnlyList<CardDetailSpan>>? spansFor = null,
        bool selectionMarkerOnLeft = false)
    {
        var fieldTop = layout.PaneContentTop + fieldFirstRow - optionsScroll;
        var fieldBottom = layout.PaneContentTop + fieldLastRow - optionsScroll;
        var desiredRows = Math.Min(6, items.Count);
        var top = fieldBottom + 1;
        var availableBelow = layout.ContentBottom - top;
        int visibleRows;
        if (availableBelow >= 3)
        {
            visibleRows = Math.Min(desiredRows, availableBelow - 2);
        }
        else
        {
            visibleRows = Math.Min(
                desiredRows,
                Math.Max(1, fieldTop - layout.ContentTop - 2));
            top = Math.Max(layout.ContentTop, fieldTop - visibleRows - 2);
        }

        visibleRows = Math.Max(1, visibleRows);
        var x = layout.AsideX;
        var width = layout.AsideWidth;
        var height = visibleRows + 2;
        canvas.Fill(x, top, width, height, BoardStyles.PanelBackground);
        canvas.Put(x, top, "╭", BoardStyles.BorderSoft, BoardStyles.PanelBackground);
        canvas.Put(x + width - 1, top, "╮", BoardStyles.BorderSoft, BoardStyles.PanelBackground);
        canvas.Put(x, top + height - 1, "╰", BoardStyles.BorderSoft, BoardStyles.PanelBackground);
        canvas.Put(
            x + width - 1,
            top + height - 1,
            "╯",
            BoardStyles.BorderSoft,
            BoardStyles.PanelBackground);
        for (var offset = 1; offset < width - 1; offset++)
        {
            canvas.Put(x + offset, top, "─", BoardStyles.BorderSoft, BoardStyles.PanelBackground);
            canvas.Put(
                x + offset,
                top + height - 1,
                "─",
                BoardStyles.BorderSoft,
                BoardStyles.PanelBackground);
        }

        canvas.Put(
            x + 2,
            top,
            $" {title} ",
            BoardStyles.TextMuted,
            BoardStyles.PanelBackground,
            bold: true,
            maxWidth: Math.Max(1, width - 4));

        var firstItem = picker.VisibleStart(visibleRows);
        for (var row = 0; row < visibleRows; row++)
        {
            var itemIndex = firstItem + row;
            if (itemIndex >= items.Count)
            {
                break;
            }

            var item = items[itemIndex];
            var highlighted = itemIndex == picker.HighlightedIndex;
            var background = highlighted
                ? BoardStyles.InputActiveBackground
                : BoardStyles.PanelBackground;
            var y = top + 1 + row;
            canvas.Fill(x + 1, y, width - 2, 1, background);
            canvas.Put(
                x + 1,
                y,
                "▌",
                highlighted ? BoardStyles.Selection : BoardStyles.FieldAnchorPlaceholder,
                background,
                bold: highlighted);
            var selected = isSelected(itemIndex);
            var labelX = x + 3;
            if (selectionMarkerOnLeft)
            {
                canvas.Put(
                    labelX,
                    y,
                    selected ? "✓" : " ",
                    selected ? BoardStyles.Connected : BoardStyles.TextMuted,
                    background,
                    bold: selected);
                labelX += 2;
            }

            var accent = accentFor?.Invoke(item);
            if (accent is Rgb accentColour)
            {
                canvas.Put(labelX, y, "●", accentColour, background, bold: true);
                labelX += 2;
            }

            var label = labelFor(item);
            var currentMarkerWidth = selected && !selectionMarkerOnLeft ? 2 : 0;
            var labelWidth = Math.Max(
                1,
                width - (labelX - x) - 2 - currentMarkerWidth);
            if (spansFor is null)
            {
                canvas.Put(
                    labelX,
                    y,
                    UnicodeDisplay.Truncate(label, labelWidth),
                    BoardStyles.TextStrong,
                    background,
                    bold: highlighted,
                    maxWidth: labelWidth);
            }
            else
            {
                DrawLine(
                    canvas,
                    new CardDetailLine(spansFor(item, labelWidth)),
                    labelX,
                    y,
                    labelWidth,
                    background);
            }

            if (selected && !selectionMarkerOnLeft)
            {
                canvas.Put(x + width - 2, y, "✓", BoardStyles.Connected, background, bold: true);
            }
        }
    }

    private static IReadOnlyList<CardDetailSpan> BuildTagChoiceSpans(CardTag tag, int width)
    {
        var style = BoardStyles.ResolveTag(tag);
        var label = $"{UnicodeDisplay.EmojiLabelPrefix(tag.Emoji ?? string.Empty)}{tag.Name}";
        label = UnicodeDisplay.Truncate(label, Math.Max(1, width - 2));
        return
        [
            new CardDetailSpan("▐", style.LeftBackground),
            new CardDetailSpan(label, style.Foreground, Surface: style),
            new CardDetailSpan("▌", style.RightBackground)
        ];
    }

    private static void DrawFooter(
        TerminalCanvas canvas,
        CardDetailField focusedField,
        bool editing,
        bool choosing,
        bool multiChoosing,
        bool hasDraft,
        string? feedback,
        bool feedbackIsError)
    {
        if (!string.IsNullOrWhiteSpace(feedback))
        {
            canvas.Put(
                2,
                canvas.Height - 1,
                UnicodeDisplay.Truncate(feedback, Math.Max(1, canvas.Width - 4)),
                feedbackIsError ? BoardStyles.Danger : BoardStyles.Connected,
                bold: true,
                maxWidth: canvas.Width - 4);
            return;
        }

        if (editing)
        {
            if (multiChoosing)
            {
                canvas.Put(2, canvas.Height - 1, "arrows", BoardStyles.Selection, bold: true);
                canvas.Put(9, canvas.Height - 1, "select", BoardStyles.TextMuted);
                canvas.Put(17, canvas.Height - 1, "space", BoardStyles.Selection, bold: true);
                canvas.Put(23, canvas.Height - 1, "toggle", BoardStyles.TextMuted);
                canvas.Put(31, canvas.Height - 1, "enter", BoardStyles.Selection, bold: true);
                canvas.Put(37, canvas.Height - 1, "done", BoardStyles.TextMuted);
                canvas.Put(42, canvas.Height - 1, "tab", BoardStyles.Selection, bold: true);
                canvas.Put(46, canvas.Height - 1, "next", BoardStyles.TextMuted);
                canvas.Put(52, canvas.Height - 1, "ctrl+s", BoardStyles.Selection, bold: true);
                canvas.Put(59, canvas.Height - 1, "save", BoardStyles.TextMuted);
                canvas.Put(64, canvas.Height - 1, "esc", BoardStyles.Selection, bold: true);
                canvas.Put(68, canvas.Height - 1, "cancel", BoardStyles.TextMuted);
                return;
            }

            canvas.Put(2, canvas.Height - 1, "tab", BoardStyles.Selection, bold: true);
            canvas.Put(6, canvas.Height - 1, "next", BoardStyles.TextMuted);
            canvas.Put(13, canvas.Height - 1, "arrows", BoardStyles.Selection, bold: true);
            canvas.Put(20, canvas.Height - 1, choosing ? "choose" : "cursor", BoardStyles.TextMuted);
            canvas.Put(27, canvas.Height - 1, "ctrl+s", BoardStyles.Selection, bold: true);
            canvas.Put(34, canvas.Height - 1, "save", BoardStyles.TextMuted);
            canvas.Put(41, canvas.Height - 1, "esc", BoardStyles.Selection, bold: true);
            canvas.Put(45, canvas.Height - 1, "discard", BoardStyles.TextMuted);
            return;
        }

        if (focusedField == CardDetailField.Description)
        {
            canvas.Put(2, canvas.Height - 1, "↑/↓", BoardStyles.Selection, bold: true);
            canvas.Put(6, canvas.Height - 1, "scroll", BoardStyles.TextMuted);
            canvas.Put(13, canvas.Height - 1, "←/→", BoardStyles.Selection, bold: true);
            canvas.Put(17, canvas.Height - 1, "field", BoardStyles.TextMuted);
            canvas.Put(23, canvas.Height - 1, "enter", BoardStyles.Selection, bold: true);
            canvas.Put(29, canvas.Height - 1, "edit", BoardStyles.TextMuted);
        }
        else
        {
            canvas.Put(2, canvas.Height - 1, "tab/arrows", BoardStyles.Selection, bold: true);
            canvas.Put(13, canvas.Height - 1, "field", BoardStyles.TextMuted);
            canvas.Put(20, canvas.Height - 1, "enter", BoardStyles.Selection, bold: true);
            canvas.Put(26, canvas.Height - 1, "edit", BoardStyles.TextMuted);
        }

        if (hasDraft)
        {
            canvas.Put(35, canvas.Height - 1, "ctrl+s", BoardStyles.Selection, bold: true);
            canvas.Put(42, canvas.Height - 1, "save", BoardStyles.TextMuted);
            canvas.Put(48, canvas.Height - 1, "esc", BoardStyles.Selection, bold: true);
            canvas.Put(52, canvas.Height - 1, "discard", BoardStyles.TextMuted);
        }
        else
        {
            canvas.Put(35, canvas.Height - 1, "pgup/dn", BoardStyles.Selection, bold: true);
            canvas.Put(43, canvas.Height - 1, "page", BoardStyles.TextMuted);
            canvas.Put(49, canvas.Height - 1, "esc/q", BoardStyles.Selection, bold: true);
            canvas.Put(55, canvas.Height - 1, "board", BoardStyles.TextMuted);
        }
    }

    private static void DrawLines(
        TerminalCanvas canvas,
        IReadOnlyList<CardDetailLine> lines,
        int x,
        int width,
        int top,
        int bottom,
        int scroll,
        Rgb defaultBackground,
        CardDetailField? highlightedField = null)
    {
        for (var index = scroll; index < lines.Count; index++)
        {
            var y = top + index - scroll;
            if (y >= bottom)
            {
                break;
            }

            var line = lines[index];
            var fieldHighlighted = line.Field == highlightedField;
            var background = defaultBackground;

            if (line.Field is not null)
            {
                var firstFieldRow = index == 0 || lines[index - 1].Field != line.Field;
                canvas.Put(
                    x,
                    y,
                    firstFieldRow ? "▌" : " ",
                    fieldHighlighted ? BoardStyles.Selection : BoardStyles.FieldAnchorPlaceholder,
                    background,
                    bold: fieldHighlighted && firstFieldRow);
                DrawLine(canvas, line, x + 2, y, Math.Max(1, width - 2), background);
            }
            else
            {
                DrawLine(canvas, line, x, y, width, background);
            }
        }
    }

    private static void DrawLine(
        TerminalCanvas canvas,
        CardDetailLine line,
        int x,
        int y,
        int width,
        Rgb defaultBackground)
    {
        var used = 0;
        foreach (var span in line.Spans)
        {
            var spanWidth = Math.Max(1, UnicodeDisplay.TextWidth(span.Text));
            var spanUsed = 0;
            foreach (var grapheme in UnicodeDisplay.Graphemes(span.Text))
            {
                var graphemeWidth = UnicodeDisplay.Width(grapheme);
                if (used + graphemeWidth > width)
                {
                    return;
                }

                var background = span.Surface?.BackgroundAt(spanUsed, spanWidth)
                    ?? span.Background
                    ?? defaultBackground;
                canvas.Put(
                    x + used,
                    y,
                    grapheme,
                    span.Foreground,
                    background,
                    span.Bold,
                    graphemeWidth);
                used += graphemeWidth;
                spanUsed += graphemeWidth;
            }
        }
    }

    private static void DrawScrollIndicator(
        TerminalCanvas canvas,
        int x,
        int top,
        int viewportRows,
        int totalRows,
        int maxScroll,
        int scroll)
    {
        if (maxScroll == 0 || viewportRows == 0)
        {
            return;
        }

        var thumbHeight = Math.Max(
            1,
            (int)Math.Round(viewportRows * (viewportRows / (double)totalRows)));
        thumbHeight = Math.Min(viewportRows, thumbHeight);
        var travel = viewportRows - thumbHeight;
        var thumbOffset = (int)Math.Round(travel * (scroll / (double)maxScroll));
        for (var y = top + thumbOffset; y < top + thumbOffset + thumbHeight; y++)
        {
            canvas.Put(x, y, "▐", BoardStyles.ScrollIndicator, canvas.BackgroundAt(x, y));
        }
    }
}
