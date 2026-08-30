internal static class BoardOilBranding
{
    private const int LogoStartRow = 2;
    private const int WordmarkRow = 6;

    private static readonly string[] LogoRows =
    [
        "💧🟦🟨",
        "🟨🟥🟪",
        "🟧  🟩"
    ];

    public static void Draw(TerminalCanvas canvas)
    {
        foreach (var (row, index) in LogoRows.Select((row, index) => (row, index)))
        {
            var rowWidth = UnicodeDisplay.TextWidth(row);
            canvas.Put((canvas.Width - rowWidth) / 2, LogoStartRow + index, row);
        }

        const string wordmark = "BoardOil";
        var wordmarkWidth = UnicodeDisplay.TextWidth(wordmark);
        canvas.Put(
            (canvas.Width - wordmarkWidth) / 2,
            WordmarkRow,
            wordmark,
            BoardStyles.TextStrong,
            bold: true);
    }
}
