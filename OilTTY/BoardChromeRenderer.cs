internal static class BoardChromeRenderer
{
    private const int HeaderSeparatorRow = 1;
    private const int ColumnHeaderRow = 2;

    public static void DrawFrame(
        TerminalCanvas canvas,
        BoardData data,
        string status,
        bool movingCard)
    {
        canvas.Fill(0, 0, canvas.Width, canvas.Height, BoardStyles.RootBackground);
        canvas.HorizontalLine(0, HeaderSeparatorRow, canvas.Width, "─", BoardStyles.BorderSoft);
        canvas.HorizontalLine(0, canvas.Height - 2, canvas.Width, "─", BoardStyles.BorderSoft);
        ScreenChromeRenderer.DrawTopRow(canvas, data.Board.Name, status);

        var footerItems = movingCard
            ? new[]
            {
                (Keys: "h/j/k/l", Label: "move"),
                (Keys: "space", Label: "drop"),
                (Keys: "esc", Label: "cancel"),
                (Keys: "q", Label: "quit")
            }
            : new[]
            {
                (Keys: "j/k", Label: "card"),
                (Keys: "h/l", Label: "column"),
                (Keys: "space", Label: "move"),
                (Keys: "enter", Label: "open"),
                (Keys: "n", Label: "new"),
                (Keys: "b", Label: "board"),
                (Keys: "r", Label: "reload"),
                (Keys: "q", Label: "quit")
            };
        DrawFooter(canvas, footerItems, BoardStyles.Selection);
    }

    private static void DrawFooter(
        TerminalCanvas canvas,
        IReadOnlyList<(string Keys, string Label)> items,
        Rgb keyColour)
    {
        var x = 2;
        var y = canvas.Height - 1;
        foreach (var item in items)
        {
            canvas.Put(x, y, item.Keys, keyColour, bold: true);
            x += UnicodeDisplay.TextWidth(item.Keys) + 1;
            canvas.Put(x, y, item.Label, BoardStyles.TextMuted);
            x += UnicodeDisplay.TextWidth(item.Label) + 2;
        }
    }

    public static void DrawColumnHeaders(
        TerminalCanvas canvas,
        IReadOnlyList<BoardLayoutColumn> columns,
        int selectedColumnIndex)
    {
        foreach (var column in columns)
        {
            var selected = column.BoardIndex == selectedColumnIndex;
            if (selected)
            {
                canvas.Fill(
                    column.X + 1,
                    ColumnHeaderRow,
                    column.Width - 2,
                    1,
                    BoardStyles.InputActiveBackground);
            }

            for (var y = ColumnHeaderRow; y < canvas.Height - 2; y++)
            {
                canvas.Put(column.X, y, "▏", BoardStyles.BorderSoft);
                canvas.Put(column.X + column.Width - 1, y, "▕", BoardStyles.BorderSoft);
            }

            var count = column.Column.Cards.Count.ToString();
            var titleWidth = Math.Max(1, column.Width - UnicodeDisplay.TextWidth(count) - 8);
            if (selected)
            {
                canvas.Put(
                    column.X + 1,
                    ColumnHeaderRow,
                    "▌",
                    BoardStyles.Selection,
                    BoardStyles.InputActiveBackground,
                    bold: true);
            }

            canvas.Put(
                column.X + 3,
                ColumnHeaderRow,
                UnicodeDisplay.Truncate(column.Column.Title.ToUpperInvariant(), titleWidth),
                BoardStyles.TextStrong,
                bold: true,
                maxWidth: titleWidth);
            canvas.Put(
                column.X + column.Width - UnicodeDisplay.TextWidth(count) - 3,
                ColumnHeaderRow,
                count,
                selected ? BoardStyles.Selection : BoardStyles.TextMuted,
                bold: selected);
        }
    }

    public static void DrawScrollIndicators(
        TerminalCanvas canvas,
        IReadOnlyList<BoardLayoutColumn> columns,
        IReadOnlyList<BoardLayoutCard> visibleCards)
    {
        var contentStartRow = BoardLayoutEngine.ContentStartRow;
        var trackHeight = (canvas.Height - 2) - contentStartRow;
        if (trackHeight <= 0)
        {
            return;
        }

        foreach (var column in columns)
        {
            var totalCards = column.Column.Cards.Count;
            var cards = visibleCards
                .Where(card => card.Column == column)
                .OrderBy(card => card.Y)
                .ToArray();
            if (cards.Length == 0)
            {
                continue;
            }

            var firstVisible = BoardLayoutEngine.FindCardIndex(column.Column, cards[0].Card.Id);
            var lastVisible = BoardLayoutEngine.FindCardIndex(column.Column, cards[^1].Card.Id);
            if (firstVisible < 0 || lastVisible < firstVisible)
            {
                continue;
            }

            var contentBottom = contentStartRow + trackHeight;
            var visibleEnd = lastVisible + VisibleCardFraction(cards[^1], contentBottom);
            if (firstVisible == 0 && visibleEnd >= totalCards)
            {
                continue;
            }

            var thumbStart = contentStartRow
                + (int)Math.Floor(trackHeight * (firstVisible / (double)totalCards));
            var thumbEnd = contentStartRow
                + (int)Math.Floor(trackHeight * (visibleEnd / totalCards));
            thumbEnd = Math.Clamp(thumbEnd, thumbStart + 1, contentStartRow + trackHeight);

            var x = column.X + column.Width - 1;
            for (var y = thumbStart; y < thumbEnd; y++)
            {
                canvas.Put(
                    x,
                    y,
                    "▐",
                    BoardStyles.ScrollIndicator,
                    canvas.BackgroundAt(x, y));
            }
        }
    }

    private static double VisibleCardFraction(BoardLayoutCard card, int contentBottom)
    {
        var extent = card.Height + (card.Card.SlickId is null ? 0 : 1);
        var visibleExtent = Math.Clamp(contentBottom - card.Y, 0, extent);
        return visibleExtent / (double)extent;
    }
}
