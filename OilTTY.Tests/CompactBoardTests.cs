using Xunit;

[Collection("Board palette")]
public sealed class CompactBoardTests
{
    [Theory]
    [InlineData("Dark", 64)]
    [InlineData("Dark", 121)]
    [InlineData("Light", 64)]
    [InlineData("Light", 121)]
    public void MixedCards_TouchWithoutSpacerRowsAndKeepSquareOrdinaryCorners(string theme, int width)
    {
        var previous = BoardStyles.Theme;
        try
        {
            BoardStyles.UseTheme(Enum.Parse<OilTTYTheme>(theme));
            var data = Data(TestBoardFactory.Column(1,
                TestBoardFactory.Card(1, 1), TestBoardFactory.Card(2, 1),
                TestBoardFactory.Card(3, 1, slickId: 7), TestBoardFactory.Card(4, 1, slickId: 8),
                TestBoardFactory.Card(5, 1)), TestBoardFactory.Column(2));
            var (layout, canvas) = Render(data, width, 30);
            for (var i = 1; i < layout.Cards.Count; i++)
                Assert.Equal(layout.Cards[i - 1].Y + layout.Cards[i - 1].Height, layout.Cards[i].Y);
            foreach (var card in layout.Cards)
            {
                var slick = card.Card.SlickId;
                var surround = slick is int id ? BoardStyles.ResolveSlick(null, id) : BoardStyles.BoardBackground;
                Assert.Equal(slick is null ? "🬦" : "🭉", canvas.CellAt(card.X, card.Y).Grapheme);
                Assert.Equal(surround, canvas.CellAt(card.X + 2, card.Y).Foreground);
                Assert.Equal(surround, canvas.CellAt(card.X + 2, card.Y + card.Height - 1).Background);
                Assert.NotEqual(BoardStyles.CardBackground, surround);
            }
        }
        finally { BoardStyles.UseTheme(previous); }
    }

    [Fact]
    public void Insets_PreserveGradientEndpointsAndSelectionDoesNotReplaceContours()
    {
        var type = new CardTypeDefinition(1, "Story", "📙", "gradient",
            "{\"leftColor\":\"#301040\",\"rightColor\":\"#207060\",\"borderMode\":\"custom\",\"borderColor\":\"#ffffff\"}");
        var data = Data(TestBoardFactory.Column(1, TestBoardFactory.Card(123, 1, slickId: 7))) with
            { CardTypes = new Dictionary<int, CardTypeDefinition> { [1] = type } };
        var (layout, normal) = Render(data, 80, 20);
        var selected = new BoardRenderer().Render(data, layout, 123, null, "connected").Canvas;
        var card = Assert.Single(layout.Cards);
        var style = BoardStyles.ResolveCard(type);
        Assert.Equal(style.LeftBackground, selected.CellAt(card.X, card.Y + 1).Background);
        Assert.Equal(style.RightBackground, selected.CellAt(card.X + card.Width - 1, card.Y + 1).Background);
        for (var x = card.X; x < card.X + card.Width; x++)
        {
            Assert.Equal(normal.CellAt(x, card.Y), selected.CellAt(x, card.Y));
            Assert.Equal(style.BackgroundAt(x - card.X, card.Width),
                x == card.X || x == card.X + card.Width - 1
                    ? selected.CellAt(x, card.Y).Foreground : selected.CellAt(x, card.Y).Background);
        }
        Assert.Equal("▸", selected.CellAt(card.X + 1, card.Y + 1).Grapheme);
        Assert.Equal(BoardStyles.Selection, selected.CellAt(card.X + card.Width - 6, card.Y + 1).Background);
    }

    [Fact]
    public void ScrollThumb_OverlaysBridgesWithStableBackingAndPreservesFooter()
    {
        var data = Data(
            TestBoardFactory.Column(1, Enumerable.Range(1, 10).Select(id => TestBoardFactory.Card(id, 1, slickId: 7)).ToArray()),
            TestBoardFactory.Column(2, Enumerable.Range(20, 10).Select(id => TestBoardFactory.Card(id, 2, slickId: 7)).ToArray()));
        var (layout, canvas) = Render(data, 80, 19);
        var partial = layout.Cards.First(card => card.Y + card.Height > 17);
        Assert.True(partial.Y < 17);
        Assert.Equal("🬂", canvas.CellAt(partial.X + 2, partial.Y).Grapheme);
        var thumb = canvas.CellAt(39, 5);
        Assert.Equal("▌", thumb.Grapheme);
        Assert.Equal(BoardStyles.ScrollIndicator, thumb.Foreground);
        Assert.Equal(BoardStyles.BoardBackground, thumb.Background);
        Assert.Equal(BoardStyles.ResolveSlick(null, 7), canvas.CellAt(40, 5).Background);
        for (var x = 0; x < canvas.Width; x++)
        {
            Assert.Equal("─", canvas.CellAt(x, 17).Grapheme);
            Assert.Equal(BoardStyles.BoardBackground, canvas.CellAt(x, 18).Background);
        }
    }

    [Fact]
    public void Bridges_ConnectSeparateMatchingRunsWithoutCrossingOrdinaryCards()
    {
        var data = Data(
            TestBoardFactory.Column(1, TestBoardFactory.Card(1, 1, slickId: 7),
                TestBoardFactory.Card(2, 1), TestBoardFactory.Card(3, 1, slickId: 7)),
            TestBoardFactory.Column(2, TestBoardFactory.Card(4, 2, slickId: 7),
                TestBoardFactory.Card(5, 2), TestBoardFactory.Card(6, 2, slickId: 7)));
        var (layout, canvas) = Render(data, 80, 24);
        var ordinary = layout.Cards.Single(card => card.Card.Id == 2);
        Assert.Equal(BoardStyles.BoardBackground, canvas.CellAt(39, ordinary.Y + 1).Background);
        foreach (var card in layout.Cards.Where(card => card.Column.Slot == 0 && card.Card.SlickId == 7))
        {
            Assert.Equal(BoardStyles.ResolveSlick(null, 7), canvas.CellAt(39, card.Y + 1).Background);
            Assert.Equal("🭏", canvas.CellAt(card.X + card.Width + 1, card.Y).Grapheme);
        }
    }

    [Fact]
    public void Navigation_PreservesOtherColumnsViewportWithCompactSlickSpacing()
    {
        var data = Data(
            TestBoardFactory.Column(1, Enumerable.Range(1, 15).Select(id => TestBoardFactory.Card(id, 1, slickId: 7)).ToArray()),
            TestBoardFactory.Column(2, Enumerable.Range(30, 15).Select(id => TestBoardFactory.Card(id, 2, slickId: 8)).ToArray()));
        var screen = new BoardScreen(data, "connected");
        var viewport = new TerminalViewport(80, 18);
        for (var i = 0; i < 8; i++) screen.HandleKey(Key('j', ConsoleKey.J), viewport);
        var before = screen.Render(viewport).Canvas;
        screen.HandleKey(Key('l', ConsoleKey.L), viewport);
        for (var i = 0; i < 5; i++) screen.HandleKey(Key('j', ConsoleKey.J), viewport);
        var after = screen.Render(viewport).Canvas;
        // Exclude selection marker/number, headers and scrollbar; card content
        // in the left column must remain at its remembered vertical position.
        for (var y = 3; y < 16; y++)
        for (var x = 5; x < 30; x++)
            Assert.Equal(before.CellAt(x, y), after.CellAt(x, y));
    }

    private static ConsoleKeyInfo Key(char c, ConsoleKey key) => new(c, key, false, false, false);
    private static BoardData Data(params BoardColumn[] columns) =>
        new(TestBoardFactory.Board(columns), new Dictionary<int, CardTypeDefinition>(),
            new Dictionary<int, SlickDefinition>(), [], []);
    private static (BoardLayout Layout, TerminalCanvas Canvas) Render(BoardData data, int width, int height)
    {
        var selection = new BoardSelection();
        selection.Normalise(data.Board);
        var layout = new BoardLayoutEngine().Create(data.Board, selection, width, height);
        return (layout, new BoardRenderer().Render(data, layout, null, null, "connected").Canvas);
    }
}
