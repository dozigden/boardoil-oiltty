using Xunit;

public sealed class BoardSlickRendererTests
{
    [Fact]
    public void Draw_BridgesMatchingSlickAcrossColumnDividerWithHalfCellPinch()
    {
        const int slickId = 7;
        var left = TestBoardFactory.Column(1, TestBoardFactory.Card(1, 1, slickId: slickId));
        var right = TestBoardFactory.Column(2, TestBoardFactory.Card(2, 2, slickId: slickId));
        var board = TestBoardFactory.Board(left, right);
        var selection = new BoardSelection();
        selection.Normalise(board);
        var layout = new BoardLayoutEngine().Create(board, selection, 80, 20);
        var slick = new SlickDefinition(
            slickId,
            "Joined",
            "solid",
            "{\"backgroundColor\":\"#385688\"}");
        var data = new BoardData(
            board,
            new Dictionary<int, CardTypeDefinition>(),
            new Dictionary<int, SlickDefinition> { [slickId] = slick },
            [],
            []);
        var canvas = new TerminalCanvas(80, 20, BoardStyles.TextStrong, BoardStyles.RootBackground);

        BoardSlickRenderer.Draw(canvas, data, layout.Cards);

        var colour = new Rgb(56, 86, 136);
        Assert.Equal("▀", canvas.CellAt(39, 4).Grapheme);
        Assert.Equal(colour, canvas.CellAt(39, 4).Background);
        Assert.Equal(" ", canvas.CellAt(39, 5).Grapheme);
        Assert.Equal(colour, canvas.CellAt(39, 5).Background);
    }
}
