using Xunit;

public sealed class TerminalCanvasTests
{
    private static readonly Rgb Foreground = new(240, 240, 240);
    private static readonly Rgb Background = new(10, 20, 30);

    [Fact]
    public void Put_ReleasesContinuationWhenWideGraphemeIsOverwritten()
    {
        var canvas = CreateCanvas();
        canvas.Put(0, 0, "😀");

        canvas.Put(0, 0, "A");

        Assert.Equal("A", canvas.CellAt(0, 0).Grapheme);
        Assert.Equal(" ", canvas.CellAt(1, 0).Grapheme);
        Assert.False(canvas.CellAt(1, 0).Continuation);
    }

    [Fact]
    public void Put_ClearsWideGraphemeWhenContinuationCellIsOverwritten()
    {
        var canvas = CreateCanvas();
        canvas.Put(0, 0, "😀");

        canvas.Put(1, 0, "B");

        Assert.Equal(" ", canvas.CellAt(0, 0).Grapheme);
        Assert.Equal("B", canvas.CellAt(1, 0).Grapheme);
        Assert.False(canvas.CellAt(1, 0).Continuation);
    }

    [Fact]
    public void Fill_ClearsWideGraphemeCrossingFillBoundary()
    {
        var canvas = CreateCanvas();
        canvas.Put(0, 0, "😀");
        var fill = new Rgb(50, 60, 70);

        canvas.Fill(1, 0, 1, 1, fill);

        Assert.Equal(" ", canvas.CellAt(0, 0).Grapheme);
        Assert.Equal(" ", canvas.CellAt(1, 0).Grapheme);
        Assert.Equal(fill, canvas.CellAt(1, 0).Background);
    }

    private static TerminalCanvas CreateCanvas() => new(4, 1, Foreground, Background);
}
