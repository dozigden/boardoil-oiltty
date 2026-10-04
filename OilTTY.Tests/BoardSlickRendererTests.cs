using Xunit;

public sealed class BoardSlickRendererTests
{
    [Theory]
    [InlineData(-4, true)]
    [InlineData(-3, true)]
    [InlineData(-2, true)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(4, true, 5, 3)]
    [InlineData(-4, true, 3, 5)]
    [InlineData(3, true, 5, 6)]
    [InlineData(-3, true, 6, 5)]
    public void Draw_RequiresOverlapButConnectsAcrossTheSlickSides(
        int offset, bool matching, int leftHeight = 3, int rightHeight = 3)
    {
        var board = TestBoardFactory.Board(
            TestBoardFactory.Column(1, TestBoardFactory.Card(1, 1, slickId: 7)),
            TestBoardFactory.Column(2, TestBoardFactory.Card(2, 2, slickId: matching ? 7 : 8)));
        var selection = new BoardSelection();
        selection.Normalise(board);
        var layout = new BoardLayoutEngine().Create(board, selection, 80, 20);
        // Model different vertical positions caused by card heights or scrolling.
        var left = layout.Cards[0] with { Y = 4 + Math.Max(0, -offset), Height = leftHeight };
        var right = layout.Cards[1] with { Y = 4 + Math.Max(0, offset), Height = rightHeight };
        var data = new BoardData(board, new Dictionary<int, CardTypeDefinition>(),
            new Dictionary<int, SlickDefinition>(), [], []);
        var canvas = new TerminalCanvas(80, 20, BoardStyles.TextStrong, BoardStyles.BoardBackground);

        BoardSlickRenderer.Draw(canvas, data, [left, right]);

        var top = Math.Max(left.Y, right.Y);
        var bottom = Math.Min(left.Y + left.Height, right.Y + right.Height);
        var colour = BoardStyles.ResolveSlick(null, 7);
        var connects = matching && bottom > top;
        var extentTop = Math.Min(left.Y, right.Y);
        var extentBottom = Math.Max(left.Y + left.Height, right.Y + right.Height);
        for (var y = BoardLayoutEngine.ContentStartRow; y < canvas.Height - 2; y++)
        for (var x = left.X + left.Width + 1; x <= right.X - 2; x++)
        {
            var cell = canvas.CellAt(x, y);
            if (!connects || y < extentTop || y >= extentBottom)
                Assert.False(cell.Foreground == colour || cell.Background == colour);
        }
        if (connects && bottom - top is 1 or 2)
        {
            // Sample the Unicode shapes to check the actual coloured band,
            // rather than only the presence of a colour in each cell's palette.
            const int samples = 12;
            var height = (extentBottom - extentTop) * samples;
            var filled = new bool[height, 4 * samples];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < 4 * samples; x++)
            {
                var cell = canvas.CellAt(left.X + left.Width + 1 + x / samples, extentTop + y / samples);
                filled[y, x] = Sample(cell, (x % samples + 0.5) / samples, (y % samples + 0.5) / samples) == colour;
            }
            // Even a one-row overlap produces a broad band, not a one-row neck.
            for (var x = 0; x < 4 * samples; x++)
                Assert.True(Enumerable.Range(0, height).Count(y => filled[y, x]) >= 2 * samples);
            var firstYs = Enumerable.Range(0, height).Where(y => filled[y, 0]).ToArray();
            var lastYs = Enumerable.Range(0, height).Where(y => filled[y, 4 * samples - 1]).ToArray();
            Assert.NotEmpty(firstYs);
            Assert.NotEmpty(lastYs);
            Assert.Equal(offset > 0, lastYs.Average() > firstYs.Average());
            Assert.All(firstYs, y => Assert.InRange(y / samples + extentTop, left.Y, left.Y + left.Height - 1));
            Assert.All(lastYs, y => Assert.InRange(y / samples + extentTop, right.Y, right.Y + right.Height - 1));
            Assert.Contains(Enumerable.Range(0, height), y =>
                (y / samples + extentTop < top || y / samples + extentTop >= bottom)
                && Enumerable.Range(0, 4 * samples).Any(x => filled[y, x]));

            var visited = new HashSet<(int X, int Y)>();
            var pending = new Queue<(int X, int Y)>();
            foreach (var y in firstYs) { visited.Add((0, y)); pending.Enqueue((0, y)); }
            while (pending.TryDequeue(out var point))
            {
                foreach (var next in new[] { (point.X - 1, point.Y), (point.X + 1, point.Y),
                             (point.X, point.Y - 1), (point.X, point.Y + 1) })
                {
                    var (x, y) = next;
                    if (x >= 0 && x < 4 * samples && y >= 0 && y < height && filled[y, x] && visited.Add(next))
                        pending.Enqueue(next);
                }
            }
            Assert.Contains(visited, point => point.X == 4 * samples - 1);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    public void Draw_ClipsSlopingBridgeAtViewportEdges(int top)
    {
        var board = TestBoardFactory.Board(
            TestBoardFactory.Column(1, TestBoardFactory.Card(1, 1, slickId: 7)),
            TestBoardFactory.Column(2, TestBoardFactory.Card(2, 2, slickId: 7)));
        var selection = new BoardSelection();
        selection.Normalise(board);
        var layout = new BoardLayoutEngine().Create(board, selection, 80, 20);
        var left = layout.Cards[0] with { Y = top };
        var right = layout.Cards[1] with { Y = top + 2 };
        var data = new BoardData(board, new Dictionary<int, CardTypeDefinition>(),
            new Dictionary<int, SlickDefinition>(), [], []);
        var canvas = new TerminalCanvas(80, 20, BoardStyles.TextStrong, BoardStyles.BoardBackground);

        BoardSlickRenderer.Draw(canvas, data, [left, right]);

        var colour = BoardStyles.ResolveSlick(null, 7);
        Assert.Equal(colour, canvas.CellAt(left.X + left.Width + 2, top == 1 ? 3 : 17).Background);
        foreach (var y in new[] { 0, 1, 2, 18, 19 })
        for (var x = 0; x < canvas.Width; x++)
        {
            Assert.Equal(" ", canvas.CellAt(x, y).Grapheme);
            Assert.Equal(BoardStyles.BoardBackground, canvas.CellAt(x, y).Background);
        }
    }

    [Theory]
    [InlineData(1, 3, 3)]
    [InlineData(2, 3, 3)]
    [InlineData(-1, 3, 3)]
    [InlineData(-2, 3, 3)]
    [InlineData(1, 3, 4)]
    [InlineData(-1, 4, 3)]
    public void Draw_NeighbouringBridgesPreserveBothColoursAndIgnoreSlickIdOrder(
        int offset, int leftHeight, int rightHeight)
    {
        var red = new Rgb(238, 110, 102);
        var blue = new Rgb(82, 160, 229);
        TerminalCanvas? previous = null;
        foreach (var swapIds in new[] { false, true })
        {
            var redId = swapIds ? 8 : 7;
            var blueId = swapIds ? 7 : 8;
            var board = TestBoardFactory.Board(
                TestBoardFactory.Column(1, TestBoardFactory.Card(1, 1, slickId: redId),
                    TestBoardFactory.Card(2, 1, slickId: blueId)),
                TestBoardFactory.Column(2, TestBoardFactory.Card(3, 2, slickId: redId),
                    TestBoardFactory.Card(4, 2, slickId: blueId)));
            var selection = new BoardSelection();
            selection.Normalise(board);
            var layout = new BoardLayoutEngine().Create(board, selection, 80, 20);
            var leftTop = 4 + Math.Max(0, -offset);
            var rightTop = 4 + Math.Max(0, offset);
            BoardLayoutCard[] cards = [
                layout.Cards[0] with { Y = leftTop, Height = leftHeight },
                layout.Cards[1] with { Y = leftTop + leftHeight },
                layout.Cards[2] with { Y = rightTop, Height = rightHeight },
                layout.Cards[3] with { Y = rightTop + rightHeight }
            ];
            var data = new BoardData(board, new Dictionary<int, CardTypeDefinition>(),
                new Dictionary<int, SlickDefinition> {
                    [redId] = new(redId, "Red", "solid", "{\"backgroundColor\":\"#EE6E66\"}"),
                    [blueId] = new(blueId, "Blue", "solid", "{\"backgroundColor\":\"#52A0E5\"}")
                }, [], []);
            TerminalCanvas Draw(IEnumerable<BoardLayoutCard> visible)
            {
                var result = new TerminalCanvas(80, 20, BoardStyles.TextStrong, BoardStyles.BoardBackground);
                BoardSlickRenderer.Draw(result, data, visible.ToArray());
                return result;
            }
            var together = Draw(cards);
            var redOnly = Draw(cards.Where(card => card.Card.SlickId == redId));
            var blueOnly = Draw(cards.Where(card => card.Card.SlickId == blueId));
            var shared = 0;
            var startX = cards[0].X + cards[0].Width + 1;
            for (var y = 3; y < 18; y++)
            for (var x = startX; x < startX + 4; x++)
            {
                var cell = together.CellAt(x, y);
                if (cell.Foreground == red && cell.Background == blue
                    || cell.Foreground == blue && cell.Background == red) shared++;
                if (previous is not null) Assert.Equal(previous.CellAt(x, y), cell);
                for (var sy = 0; sy < 12; sy++)
                for (var sx = 0; sx < 12; sx++)
                {
                    var px = (sx + 0.5) / 12;
                    var py = (sy + 0.5) / 12;
                    var actual = Sample(cell, px, py);
                    if (Sample(redOnly.CellAt(x, y), px, py) == red) Assert.Equal(red, actual);
                    if (Sample(blueOnly.CellAt(x, y), px, py) == blue) Assert.Equal(blue, actual);
                    // Every adjacent glyph must continue the same boundary.
                    if (x < startX + 3)
                        Assert.Equal(Sample(cell, 1 - 1e-6, py), Sample(together.CellAt(x + 1, y), 1e-6, py));
                }
            }
            Assert.True(shared > 0);
            previous = together;
        }
    }

    private static Rgb Sample(TerminalCell cell, double x, double y)
    {
        if (cell.Grapheme == " ") return cell.Background;
        // Unicode diagonal endpoints, measured in thirds of a cell's height.
        var (left, right) = cell.Grapheme switch
        {
            "🭍" => (0, 1), "🭑" => (1, 2), "🬽" => (2, 3),
            "🭈" => (3, 2), "🭆" => (2, 1), "🭂" => (1, 0),
            "🭏" => (0, 2), "🬿" => (1, 3), "🬼" => (2, 4), "🭌" => (-1, 1),
            "🭄" => (2, 0), "🭊" => (3, 1), "🭇" => (4, 2), "🭁" => (1, -1),
            "🬂" or "🬎" => (0, 0),
            _ => throw new InvalidOperationException($"Unexpected bridge glyph {cell.Grapheme}")
        };
        var foreground = cell.Grapheme switch
        {
            "🬂" => y < 1d / 3, "🬎" => y < 2d / 3,
            _ => y >= (left + (right - left) * x) / 3
        };
        return foreground ? cell.Foreground : cell.Background;
    }

    [Fact]
    public void Draw_BridgesMatchingSlickFromOuterBanksAcrossFourGutterCells()
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
        Assert.Equal("🬽", canvas.CellAt(39, 4).Grapheme);
        Assert.Equal(colour, canvas.CellAt(39, 4).Foreground);
        Assert.Equal(" ", canvas.CellAt(39, 5).Grapheme);
        Assert.Equal(colour, canvas.CellAt(39, 5).Background);
    }
}
