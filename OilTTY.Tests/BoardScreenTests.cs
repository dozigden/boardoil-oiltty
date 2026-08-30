using Xunit;

public sealed class BoardScreenTests
{
    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(81)]
    public void Header_CentresOilTtyAcrossTheTerminal(int width)
    {
        var screen = new BoardScreen(
            Data(TestBoardFactory.Column(1, TestBoardFactory.Card(10, 1))),
            "connected");

        var canvas = screen.Render(new TerminalViewport(width, 24)).Canvas;
        var appNameX = (width - UnicodeDisplay.TextWidth("OilTTY")) / 2;

        Assert.Equal("OilTTY", RowText(canvas, appNameX, 0, 6));
    }

    [Fact]
    public void N_CreatesInTheSelectedEmptyColumn()
    {
        var data = Data(TestBoardFactory.Column(1));
        var screen = new BoardScreen(data, "connected");

        var update = screen.HandleKey(
            new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false),
            new TerminalViewport(80, 24));

        Assert.True(update.IsComplete);
        Assert.Equal(BoardCommand.CreateCard, update.Result);
        Assert.Equal(1, screen.SelectedColumn!.Id);
        Assert.Null(screen.SelectedCard);
    }

    [Fact]
    public void ActiveColumnChrome_HighlightsAnEmptyColumnAndFollowsHorizontalNavigation()
    {
        var screen = new BoardScreen(
            Data(TestBoardFactory.Column(1), TestBoardFactory.Column(2)),
            "connected");
        var viewport = new TerminalViewport(80, 24);

        var first = screen.Render(viewport).Canvas;

        Assert.Equal("▌", first.CellAt(1, 2).Grapheme);
        Assert.Equal(BoardStyles.InputActiveBackground, first.CellAt(1, 2).Background);
        Assert.Equal(BoardStyles.BorderSoft, first.CellAt(0, 3).Foreground);
        Assert.Equal(BoardStyles.BorderSoft, first.CellAt(40, 3).Foreground);

        screen.HandleKey(Key('l', ConsoleKey.L), viewport);
        var second = screen.Render(viewport).Canvas;

        Assert.Equal(" ", second.CellAt(1, 2).Grapheme);
        Assert.Equal(BoardStyles.BorderSoft, second.CellAt(0, 3).Foreground);
        Assert.Equal("▌", second.CellAt(41, 2).Grapheme);
        Assert.Equal(BoardStyles.InputActiveBackground, second.CellAt(41, 2).Background);
        Assert.Equal(BoardStyles.BorderSoft, second.CellAt(40, 3).Foreground);
    }

    [Fact]
    public void N_IsIgnoredWhenTheBoardHasNoColumns()
    {
        var screen = new BoardScreen(Data(), "connected");

        var update = screen.HandleKey(
            new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false),
            new TerminalViewport(80, 24));

        Assert.False(update.IsComplete);
        Assert.False(update.Redraw);
        Assert.Null(screen.SelectedColumn);
    }

    [Fact]
    public void ReplaceCard_InsertsAndSelectsTheAuthoritativeCreatedCard()
    {
        var screen = new BoardScreen(Data(TestBoardFactory.Column(1)), "connected");
        var created = TestBoardFactory.Card(42, 1, "Created") with { SortKey = "0001" };

        screen.ReplaceCard(created);

        Assert.Equal(42, screen.SelectedCardId);
        Assert.Equal(created, screen.SelectedCard);
        Assert.Equal(created, Assert.Single(screen.SelectedColumn!.Cards));
    }

    [Fact]
    public void Footer_AdvertisesTheNewCardCommand()
    {
        var screen = new BoardScreen(Data(TestBoardFactory.Column(1)), "connected");

        var canvas = screen.Render(new TerminalViewport(120, 24)).Canvas;
        var footer = string.Concat(Enumerable.Range(0, canvas.Width)
            .Select(x => canvas.CellAt(x, canvas.Height - 1))
            .Where(cell => !cell.Continuation)
            .Select(cell => cell.Grapheme));

        Assert.Contains("n new", footer);
        Assert.Contains("space move", footer);
        Assert.Contains("ctrl+t theme", footer);
    }

    [Fact]
    public void Space_PicksUpTheSelectedCardAndShowsMoveChrome()
    {
        var screen = new BoardScreen(
            Data(TestBoardFactory.Column(1, TestBoardFactory.Card(10, 1))),
            "connected");

        var update = screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);

        Assert.False(update.IsComplete);
        Assert.Equal(10, screen.MovingCardId);
        Assert.Null(screen.PendingMove);

        var canvas = screen.Render(Viewport).Canvas;
        Assert.Equal("╔", canvas.CellAt(1, BoardLayoutEngine.ContentStartRow - 1).Grapheme);
        Assert.Equal(BoardStyles.Selection, canvas.CellAt(1, BoardLayoutEngine.ContentStartRow - 1).Foreground);
        Assert.Equal("█", canvas.CellAt(77, BoardLayoutEngine.ContentStartRow).Grapheme);
        Assert.Equal(BoardStyles.CardShadow, canvas.CellAt(77, BoardLayoutEngine.ContentStartRow).Foreground);
        Assert.Equal("█", canvas.CellAt(2, BoardLayoutEngine.ContentStartRow + 2).Grapheme);
        Assert.Contains("space drop", FooterText(canvas));
        Assert.Contains("esc cancel", FooterText(canvas));
    }

    [Fact]
    public void MovingCard_RaisesAboveItsLayoutSlotAndAdjacentCard()
    {
        var screen = new BoardScreen(
            Data(TestBoardFactory.Column(
                1,
                TestBoardFactory.Card(10, 1),
                TestBoardFactory.Card(20, 1))),
            "connected");
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);

        var canvas = screen.Render(Viewport).Canvas;

        Assert.Equal("╔", canvas.CellAt(1, BoardLayoutEngine.ContentStartRow - 1).Grapheme);
        Assert.Equal("█", canvas.CellAt(2, BoardLayoutEngine.ContentStartRow + 2).Grapheme);
        Assert.Equal(BoardStyles.CardShadow, canvas.CellAt(2, BoardLayoutEngine.ContentStartRow + 2).Foreground);
        Assert.Equal("╭", canvas.CellAt(2, BoardLayoutEngine.ContentStartRow + 3).Grapheme);
    }

    [Fact]
    public void Navigation_ReordersThePickedUpCardAndSpaceProducesAnAnchoredMove()
    {
        var screen = new BoardScreen(
            Data(TestBoardFactory.Column(
                1,
                TestBoardFactory.Card(10, 1),
                TestBoardFactory.Card(20, 1),
                TestBoardFactory.Card(30, 1))),
            "connected");
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);

        screen.HandleKey(Key('j', ConsoleKey.J), Viewport);
        var update = screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);

        Assert.Equal([20, 10, 30], screen.SelectedColumn!.Cards.Select(card => card.Id));
        Assert.Equal(10, screen.SelectedCardId);
        Assert.True(update.IsComplete);
        Assert.Equal(BoardCommand.MoveCard, update.Result);
        Assert.Equal(new CardMove(10, 1, 20), screen.PendingMove);
    }

    [Fact]
    public void HorizontalNavigation_MovesPickedUpCardIntoNearestPositionInAdjacentColumn()
    {
        var screen = new BoardScreen(
            Data(
                TestBoardFactory.Column(1, TestBoardFactory.Card(10, 1)),
                TestBoardFactory.Column(
                    2,
                    TestBoardFactory.Card(20, 2),
                    TestBoardFactory.Card(30, 2))),
            "connected");
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);

        screen.HandleKey(Key('l', ConsoleKey.L), Viewport);
        screen.HandleKey(Key('j', ConsoleKey.J), Viewport);
        var update = screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);

        Assert.Empty(screen.Data.Board.Columns[0].Cards);
        Assert.Equal([20, 10, 30], screen.Data.Board.Columns[1].Cards.Select(card => card.Id));
        Assert.Equal(2, screen.SelectedColumn!.Id);
        Assert.True(update.IsComplete);
        Assert.Equal(new CardMove(10, 2, 20), screen.PendingMove);
    }

    [Fact]
    public void Escape_CancelsMoveAndRestoresOriginalLayout()
    {
        var data = Data(
            TestBoardFactory.Column(
                1,
                TestBoardFactory.Card(10, 1),
                TestBoardFactory.Card(20, 1)),
            TestBoardFactory.Column(2));
        var screen = new BoardScreen(data, "connected");
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);
        screen.HandleKey(Key('l', ConsoleKey.L), Viewport);

        var update = screen.HandleKey(Key('\e', ConsoleKey.Escape), Viewport);

        Assert.False(update.IsComplete);
        Assert.Same(data, screen.Data);
        Assert.Equal(10, screen.SelectedCardId);
        Assert.Null(screen.MovingCardId);
        Assert.Null(screen.PendingMove);
    }

    [Fact]
    public void Space_WithoutAChangedPositionSimplyPutsTheCardDown()
    {
        var screen = new BoardScreen(
            Data(TestBoardFactory.Column(1, TestBoardFactory.Card(10, 1))),
            "connected");
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);

        var update = screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);

        Assert.False(update.IsComplete);
        Assert.Null(screen.MovingCardId);
        Assert.Null(screen.PendingMove);
    }

    [Fact]
    public void ApplyMovedCard_ReconcilesPreviewWithTheAuthoritativeSortOrder()
    {
        var original = TestBoardFactory.Card(10, 1) with { SortKey = "10" };
        var other = TestBoardFactory.Card(20, 2) with { SortKey = "20" };
        var screen = new BoardScreen(
            Data(
                TestBoardFactory.Column(1, original),
                TestBoardFactory.Column(2, other)),
            "connected");
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);
        screen.HandleKey(Key('l', ConsoleKey.L), Viewport);
        screen.HandleKey(Key('j', ConsoleKey.J), Viewport);
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar), Viewport);

        var moved = original with { BoardColumnId = 2, SortKey = "30" };
        screen.ApplyMovedCard(moved);

        Assert.Equal([20, 10], screen.Data.Board.Columns[1].Cards.Select(card => card.Id));
        Assert.Equal(10, screen.SelectedCardId);
        Assert.Null(screen.MovingCardId);
        Assert.Null(screen.PendingMove);
    }

    private static readonly TerminalViewport Viewport = new(80, 24);

    private static ConsoleKeyInfo Key(char keyChar, ConsoleKey key) =>
        new(keyChar, key, false, false, false);

    private static string FooterText(TerminalCanvas canvas) =>
        string.Concat(Enumerable.Range(0, canvas.Width)
            .Select(x => canvas.CellAt(x, canvas.Height - 1))
            .Where(cell => !cell.Continuation)
            .Select(cell => cell.Grapheme));

    private static string RowText(TerminalCanvas canvas, int x, int y, int width) =>
        string.Concat(Enumerable.Range(x, width)
            .Select(column => canvas.CellAt(column, y))
            .Where(cell => !cell.Continuation)
            .Select(cell => cell.Grapheme));

    private static BoardData Data(params BoardColumn[] columns) =>
        new(
            TestBoardFactory.Board(columns),
            new Dictionary<int, CardTypeDefinition>(),
            new Dictionary<int, SlickDefinition>(),
            [],
            []);
}
