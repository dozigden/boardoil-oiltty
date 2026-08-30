using Xunit;

public sealed class BoardLayoutTests
{
    [Fact]
    public void Create_ReturnsRequiredViewportWithoutMutatingSelection()
    {
        var column = TestBoardFactory.Column(
            1,
            TestBoardFactory.Card(1, 1),
            TestBoardFactory.Card(2, 1),
            TestBoardFactory.Card(3, 1),
            TestBoardFactory.Card(4, 1),
            TestBoardFactory.Card(5, 1));
        var board = TestBoardFactory.Board(column);
        var selection = new BoardSelection();
        selection.Normalise(board);
        selection.MoveVertical(board, 3);

        var layout = new BoardLayoutEngine().Create(board, selection, 80, 12);

        Assert.Equal(0, selection.FirstCardFor(column));
        Assert.Equal(2, layout.SelectedColumnFirstCard);
        Assert.Contains(layout.Cards, card => card.Card.Id == selection.CardId);
    }

    [Fact]
    public void Create_IncludesCardWhoseTopIsStillInsideViewport()
    {
        var board = TestBoardFactory.Board(
            TestBoardFactory.Column(
                1,
                TestBoardFactory.Card(1, 1),
                TestBoardFactory.Card(2, 1),
                TestBoardFactory.Card(3, 1)));
        var selection = new BoardSelection();
        selection.Normalise(board);

        var layout = new BoardLayoutEngine().Create(board, selection, 80, 12);
        var partialCard = Assert.Single(layout.Cards, card => card.Card.Id == 3);

        Assert.True(partialCard.Y < layout.Height - 2);
        Assert.True(partialCard.Y + partialCard.Height > layout.Height - 2);
    }

    [Fact]
    public void Create_AllocatesWholeRowsWithoutSplittingTags()
    {
        var tags = new[]
        {
            TestBoardFactory.Tag(1, "abcdefghij"),
            TestBoardFactory.Tag(2, "klmnopqrst"),
            TestBoardFactory.Tag(3, "uvwxyzabcd")
        };
        var board = TestBoardFactory.Board(
            TestBoardFactory.Column(1, TestBoardFactory.Card(1, 1, tags: tags)));
        var selection = new BoardSelection();
        selection.Normalise(board);

        var card = Assert.Single(new BoardLayoutEngine().Create(board, selection, 40, 20).Cards);

        Assert.Equal(5, card.Height);
    }

    [Fact]
    public void Create_WrapsCardTitlesAtGraphemeAwareWidths()
    {
        var board = TestBoardFactory.Board(
            TestBoardFactory.Column(
                1,
                TestBoardFactory.Card(
                    1,
                    1,
                    title: "A title that wraps onto another line")));
        var selection = new BoardSelection();
        selection.Normalise(board);

        var card = Assert.Single(new BoardLayoutEngine().Create(board, selection, 40, 20).Cards);

        Assert.Equal(["A title that wraps onto", "another line"], card.TitleLines);
        Assert.Equal(4, card.Height);
    }

    [Fact]
    public void FindNearestCardIndex_UsesRememberedTargetViewportAndVisualCentre()
    {
        var source = TestBoardFactory.Column(
            1,
            TestBoardFactory.Card(1, 1),
            TestBoardFactory.Card(2, 1));
        var target = TestBoardFactory.Column(
            2,
            TestBoardFactory.Card(10, 2),
            TestBoardFactory.Card(11, 2),
            TestBoardFactory.Card(12, 2),
            TestBoardFactory.Card(13, 2));
        var board = TestBoardFactory.Board(source, target);
        var selection = new BoardSelection();
        selection.Normalise(board);
        selection.MoveVertical(board, 1);
        selection.RememberFirstCard(target, 2);

        var targetIndex = new BoardLayoutEngine().FindNearestCardIndex(board, selection, 1, 80, 20);

        Assert.Equal(3, targetIndex);
    }
}
