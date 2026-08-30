using Xunit;

public sealed class BoardOilBrandingTests
{
    [Fact]
    public void Draw_CentresOilTTYForBoardOilWordmark()
    {
        var canvas = new TerminalCanvas(80, 12, BoardStyles.TextStrong, BoardStyles.RootBackground);

        BoardOilBranding.Draw(canvas);

        const string wordmark = "OilTTY for BoardOil";
        var wordmarkX = (canvas.Width - UnicodeDisplay.TextWidth(wordmark)) / 2;
        var rendered = string.Concat(Enumerable.Range(wordmarkX, wordmark.Length)
            .Select(x => canvas.CellAt(x, 6).Grapheme));
        Assert.Equal(wordmark, rendered);
    }
}
