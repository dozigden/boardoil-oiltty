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

    [Theory]
    [InlineData("\u001b[31mred\u001b[0m", "�[31mred�[0m")]
    [InlineData("\u001b]0;owned\u0007", "�]0;owned�")]
    [InlineData("\u001b]52;c;payload\u001b\\", "�]52;c;payload�\\")]
    [InlineData("\u009b31mred\u009c", "�31mred�")]
    [InlineData("left\rright", "left�right")]
    [InlineData("left\nright", "left�right")]
    public void Put_NeutralisesTerminalControls(string source, string expected)
    {
        var canvas = new TerminalCanvas(32, 1, Foreground, Background);

        canvas.Put(0, 0, source);

        Assert.Equal(expected, PlainText(canvas));
        Assert.DoesNotContain(PlainText(canvas), character => char.IsControl(character));
    }

    [Fact]
    public void SetCell_NeutralisesTerminalControls()
    {
        var canvas = CreateCanvas();

        canvas.SetCell(0, 0, "\u001b", Foreground, Background);

        Assert.Equal("�", canvas.CellAt(0, 0).Grapheme);
    }

    private static TerminalCanvas CreateCanvas() => new(4, 1, Foreground, Background);

    private static string PlainText(TerminalCanvas canvas) =>
        string.Concat(Enumerable.Range(0, canvas.Width)
            .Select(x => canvas.CellAt(x, 0))
            .Where(cell => !cell.Continuation)
            .Select(cell => cell.Grapheme))
        .TrimEnd();
}
