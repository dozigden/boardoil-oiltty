internal static class BoardCardRenderer
{
    public static void DrawCompact(
        TerminalCanvas canvas,
        BoardLayoutCard card,
        SurfaceStyle style,
        Rgb? slickColour,
        bool selected,
        bool moving)
    {
        Draw(canvas, card, style with { ShowBorder = false }, selected: false, moving: false);
        var surround = slickColour ?? BoardStyles.BoardBackground;
        var curved = slickColour is not null;
        var right = card.X + card.Width - 1;
        var bottom = card.Y + card.Height - 1;
        for (var x = card.X + 1; x < right; x++)
        {
            Edge(x, card.Y, "🬂", surround, style.BackgroundAt(x - card.X, card.Width));
            Edge(x, bottom, "🬎", style.BackgroundAt(x - card.X, card.Width), surround);
        }
        for (var y = card.Y + 1; y < Math.Min(bottom, canvas.Height - 2); y++)
        {
            Edge(card.X, y, "▌", surround, style.LeftBackground);
            Edge(right, y, "▐", surround, style.RightBackground);
        }
        // Square corners use sextants: the card occupies half the width and
        // two thirds of the height. Slick corners curve between the same ports.
        Edge(card.X, card.Y, curved ? "🭉" : "🬦", style.LeftBackground, surround);
        Edge(right, card.Y, curved ? "🬾" : "🬓", style.RightBackground, surround);
        Edge(card.X, bottom, curved ? "🭎" : "🬉",
            curved ? surround : style.LeftBackground, curved ? style.LeftBackground : surround);
        Edge(right, bottom, curved ? "🭃" : "🬄",
            curved ? surround : style.RightBackground, curved ? style.RightBackground : surround);

        if (selected || moving)
        {
            var y = card.Y + 1;
            if (y >= BoardLayoutEngine.ContentStartRow && y < canvas.Height - 2)
            {
                canvas.Put(card.X + 1, y, moving ? "↕" : "▸", BoardStyles.Selection,
                    style.BackgroundAt(1, card.Width), bold: true);
                var number = $"#{card.Card.Id}";
                canvas.Put(right - 1 - UnicodeDisplay.TextWidth(number), y, number,
                    BoardStyles.RootBackground, BoardStyles.Selection, bold: true);
            }
        }

        void Edge(int x, int y, string glyph, Rgb foreground, Rgb background)
        {
            if (x >= 0 && x < canvas.Width && y >= BoardLayoutEngine.ContentStartRow && y < canvas.Height - 2)
                canvas.SetCell(x, y, glyph, foreground, background);
        }
    }

    public static void DrawShadow(TerminalCanvas canvas, BoardLayoutCard layoutCard)
    {
        var rightX = layoutCard.X + layoutCard.Width;
        var bottomY = layoutCard.Y + layoutCard.Height;
        var contentBottom = canvas.Height - 2;
        for (var y = layoutCard.Y + 1; y < Math.Min(bottomY, contentBottom); y++)
        {
            canvas.Put(
                rightX,
                y,
                "█",
                BoardStyles.CardShadow,
                canvas.BackgroundAt(rightX, y));
        }

        if (bottomY >= contentBottom)
        {
            return;
        }

        for (var x = layoutCard.X + 1; x < rightX; x++)
        {
            canvas.Put(
                x,
                bottomY,
                "█",
                BoardStyles.CardShadow,
                canvas.BackgroundAt(x, bottomY));
        }

        canvas.Put(
            rightX,
            bottomY,
            "█",
            BoardStyles.CardShadow,
            canvas.BackgroundAt(rightX, bottomY));
    }

    public static void Draw(
        TerminalCanvas canvas,
        BoardLayoutCard layoutCard,
        SurfaceStyle style,
        bool selected,
        bool moving)
    {
        var card = layoutCard.Card;
        var x = layoutCard.X;
        var y = layoutCard.Y;
        var width = layoutCard.Width;
        var height = layoutCard.Height;
        var contentBottom = canvas.Height - 2;
        var visibleHeight = Math.Min(height, contentBottom - y);
        if (visibleHeight <= 0)
        {
            return;
        }

        canvas.FillGradient(x, y, width, visibleHeight, style);

        if (style.ShowBorder || selected || moving)
        {
            var emphasized = selected || moving;
            var border = emphasized ? BoardStyles.Selection : style.Border;
            var topLeft = moving ? "╔" : selected ? "┏" : "╭";
            var topRight = moving ? "╗" : selected ? "┓" : "╮";
            var bottomLeft = moving ? "╚" : selected ? "┗" : "╰";
            var bottomRight = moving ? "╝" : selected ? "┛" : "╯";
            var horizontal = moving ? "═" : selected ? "━" : "─";
            var vertical = moving ? "║" : selected ? "┃" : "│";
            PutBorderCell(canvas, x, y, topLeft, border, style, x, width, emphasized);
            PutBorderCell(canvas, x + width - 1, y, topRight, border, style, x, width, emphasized);
            var bottomBorderY = y + height - 1;
            var bottomBorderVisible = bottomBorderY < contentBottom;
            if (bottomBorderVisible)
            {
                PutBorderCell(canvas, x, bottomBorderY, bottomLeft, border, style, x, width, emphasized);
                PutBorderCell(canvas, x + width - 1, bottomBorderY, bottomRight, border, style, x, width, emphasized);
            }

            for (var px = x + 1; px < x + width - 1; px++)
            {
                PutBorderCell(canvas, px, y, horizontal, border, style, x, width, emphasized);
                if (bottomBorderVisible)
                {
                    PutBorderCell(canvas, px, bottomBorderY, horizontal, border, style, x, width, emphasized);
                }
            }

            for (var py = y + 1; py < Math.Min(bottomBorderY, contentBottom); py++)
            {
                PutBorderCell(canvas, x, py, vertical, border, style, x, width, emphasized);
                PutBorderCell(canvas, x + width - 1, py, vertical, border, style, x, width, emphasized);
            }
        }

        var number = $"#{card.Id}";
        var emoji = card.CardTypeEmoji;
        const int contentInset = 2;
        var titleX = x + contentInset;
        var titleY = y + 1;
        if (titleY < contentBottom && !string.IsNullOrWhiteSpace(emoji))
        {
            var emojiPrefix = UnicodeDisplay.EmojiLabelPrefix(emoji);
            var emojiPrefixWidth = UnicodeDisplay.TextWidth(emojiPrefix);
            PutStyledText(canvas, titleX, titleY, emojiPrefix, style.Foreground, style, x, width, emojiPrefixWidth);
            titleX += emojiPrefixWidth;
        }

        var numberX = x + width - UnicodeDisplay.TextWidth(number) - contentInset;
        for (var index = 0; index < layoutCard.TitleLines.Count; index++)
        {
            var lineY = titleY + index;
            if (lineY >= contentBottom)
            {
                break;
            }

            var titleWidth = index == 0
                ? Math.Max(1, numberX - titleX - 1)
                : Math.Max(1, (x + width - 2) - titleX);
            PutStyledText(
                canvas,
                titleX,
                lineY,
                layoutCard.TitleLines[index],
                style.Foreground,
                style,
                x,
                width,
                titleWidth,
                bold: true);
        }

        if (titleY < contentBottom)
        {
            PutStyledText(canvas, numberX, titleY, number, style.Foreground, style, x, width, UnicodeDisplay.TextWidth(number));
        }

        var contentY = y + 1 + layoutCard.TitleLines.Count;
        if (layoutCard.AssignedUserLabel is not null && contentY < contentBottom)
        {
            PutStyledText(
                canvas,
                x + contentInset,
                contentY,
                $"{UnicodeDisplay.EmojiLabelPrefix("👤")}{layoutCard.AssignedUserLabel}",
                style.Foreground,
                style,
                x,
                width,
                width - (contentInset * 2));
            contentY++;
        }

        var tagX = x + 1;
        var tagRowStart = tagX;
        var tagRowEnd = x + width - 2;
        var availableTagWidth = tagRowEnd - tagRowStart;
        foreach (var tag in card.Tags)
        {
            var label = BoardLayoutEngine.ResolveTagLabel(tag, availableTagWidth);
            var labelWidth = UnicodeDisplay.TextWidth(label);
            var tagWidth = labelWidth + 2;
            if (tagX > tagRowStart && tagX + tagWidth > tagRowEnd)
            {
                contentY++;
                tagX = tagRowStart;
            }

            if (contentY >= contentBottom)
            {
                break;
            }

            DrawTag(canvas, tagX, contentY, label, BoardStyles.ResolveTag(tag), style, x, width);
            tagX += tagWidth;
        }

        var checklistY = y + height - 2;
        if (layoutCard.ChecklistLabel is string checklistLabel && checklistY < contentBottom)
        {
            var checklistWidth = UnicodeDisplay.TextWidth(checklistLabel);
            PutStyledText(
                canvas,
                x + width - contentInset - checklistWidth,
                checklistY,
                checklistLabel,
                style.Foreground,
                style,
                x,
                width,
                checklistWidth);
        }
    }

    private static void PutBorderCell(
        TerminalCanvas canvas,
        int x,
        int y,
        string glyph,
        Rgb foreground,
        SurfaceStyle style,
        int surfaceX,
        int surfaceWidth,
        bool bold) =>
        canvas.Put(
            x,
            y,
            glyph,
            foreground,
            style.BackgroundAt(x - surfaceX, surfaceWidth),
            bold);

    private static void DrawTag(
        TerminalCanvas canvas,
        int x,
        int y,
        string label,
        SurfaceStyle tagStyle,
        SurfaceStyle cardStyle,
        int cardX,
        int cardWidth)
    {
        var labelWidth = UnicodeDisplay.TextWidth(label);
        var rightX = x + labelWidth + 1;
        canvas.Put(
            x,
            y,
            "▐",
            tagStyle.LeftBackground,
            cardStyle.BackgroundAt(x - cardX, cardWidth));
        PutStyledText(
            canvas,
            x + 1,
            y,
            label,
            tagStyle.Foreground,
            tagStyle,
            x + 1,
            labelWidth,
            labelWidth);
        canvas.Put(
            rightX,
            y,
            "▌",
            tagStyle.RightBackground,
            cardStyle.BackgroundAt(rightX - cardX, cardWidth));
    }

    private static void PutStyledText(
        TerminalCanvas canvas,
        int x,
        int y,
        string text,
        Rgb foreground,
        SurfaceStyle style,
        int surfaceX,
        int surfaceWidth,
        int maxWidth,
        bool bold = false)
    {
        var used = 0;
        foreach (var grapheme in UnicodeDisplay.Graphemes(text))
        {
            var graphemeWidth = UnicodeDisplay.Width(grapheme);
            if (used + graphemeWidth > maxWidth)
            {
                break;
            }

            var currentX = x + used;
            var background = style.BackgroundAt(currentX - surfaceX, surfaceWidth);
            canvas.Put(currentX, y, grapheme, foreground, background, bold, graphemeWidth);
            used += graphemeWidth;
        }
    }
}
