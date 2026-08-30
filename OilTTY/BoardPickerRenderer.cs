internal sealed class BoardPickerRenderer
{
    private const int ListHeadingRow = 8;
    private const int ListStartRow = 9;

    private static readonly Rgb SelectionBackground =
        BoardStyles.Mix(BoardStyles.RootBackground, BoardStyles.Selection, 0.18);

    public TerminalFrame Render(
        IReadOnlyList<BoardSummary> boards,
        int selectedIndex,
        int currentBoardId,
        int requestedWidth,
        int requestedHeight,
        string status)
    {
        var width = Math.Max(40, requestedWidth);
        var height = Math.Max(12, requestedHeight);
        var canvas = new TerminalCanvas(width, height, BoardStyles.TextStrong, BoardStyles.RootBackground);

        DrawFrame(canvas, status);
        BoardOilBranding.Draw(canvas);

        var listWidth = Math.Min(width - 4, 72);
        var listX = (width - listWidth) / 2;
        canvas.Put(listX, ListHeadingRow, "BOARDS", BoardStyles.TextStrong, bold: true);

        if (boards.Count == 0)
        {
            canvas.Put(listX, ListStartRow, "No accessible boards.", BoardStyles.TextMuted);
            return new TerminalFrame(canvas);
        }

        var visibleRows = Math.Max(1, (height - 2) - ListStartRow);
        var firstIndex = Math.Clamp(
            selectedIndex - (visibleRows / 2),
            0,
            Math.Max(0, boards.Count - visibleRows));
        var lastIndex = Math.Min(boards.Count, firstIndex + visibleRows);
        for (var index = firstIndex; index < lastIndex; index++)
        {
            var board = boards[index];
            var row = ListStartRow + index - firstIndex;
            var selected = index == selectedIndex;
            if (selected)
            {
                canvas.Fill(listX, row, listWidth, 1, SelectionBackground);
                canvas.Put(listX, row, "▌", BoardStyles.Selection, SelectionBackground, bold: true);
            }

            var background = selected ? SelectionBackground : BoardStyles.RootBackground;
            var id = $"#{board.Id}";
            var idWidth = UnicodeDisplay.TextWidth(id);
            canvas.Put(listX + 2, row, id, BoardStyles.TextMuted, background);

            var marker = board.Id == currentBoardId ? "● current" : string.Empty;
            var markerWidth = UnicodeDisplay.TextWidth(marker);
            var markerX = listX + listWidth - markerWidth - 2;
            var nameX = listX + 2 + Math.Max(5, idWidth + 2);
            var nameWidth = Math.Max(1, markerX - nameX - (markerWidth > 0 ? 2 : 0));
            canvas.Put(
                nameX,
                row,
                UnicodeDisplay.Truncate(board.Name, nameWidth),
                selected ? BoardStyles.TextStrong : BoardStyles.TextMuted,
                background,
                bold: selected,
                maxWidth: nameWidth);
            if (markerWidth > 0)
            {
                canvas.Put(markerX, row, marker, BoardStyles.Connected, background);
            }
        }

        return new TerminalFrame(canvas);
    }

    private static void DrawFrame(TerminalCanvas canvas, string status)
    {
        canvas.Fill(0, 0, canvas.Width, canvas.Height, BoardStyles.RootBackground);
        canvas.HorizontalLine(0, 1, canvas.Width, "─", BoardStyles.BorderSoft);
        canvas.HorizontalLine(0, canvas.Height - 2, canvas.Width, "─", BoardStyles.BorderSoft);
        canvas.Put(2, 0, "◆", BoardStyles.Selection, bold: true);
        canvas.Put(5, 0, "Board picker", BoardStyles.TextStrong, bold: true);

        var statusText = UnicodeDisplay.Truncate(status, Math.Max(1, (canvas.Width / 2) - 4));
        var statusX = Math.Max(18, canvas.Width - UnicodeDisplay.TextWidth(statusText) - 2);
        canvas.Put(statusX, 0, statusText, BoardStyles.Connected, bold: true);

        canvas.Put(2, canvas.Height - 1, "j/k", BoardStyles.Selection, bold: true);
        canvas.Put(6, canvas.Height - 1, "board", BoardStyles.TextMuted);
        canvas.Put(13, canvas.Height - 1, "enter", BoardStyles.Selection, bold: true);
        canvas.Put(19, canvas.Height - 1, "open", BoardStyles.TextMuted);
        canvas.Put(26, canvas.Height - 1, "b/esc", BoardStyles.Selection, bold: true);
        canvas.Put(32, canvas.Height - 1, "cancel", BoardStyles.TextMuted);
    }
}
