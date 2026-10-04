internal static class BoardSlickRenderer
{
    // Every contour is a prescribed piece with matching fractional endpoints.
    // Card-facing edges are drawn by BoardCardRenderer after these outer layers.
    public static void Draw(
        TerminalCanvas canvas,
        BoardData data,
        IReadOnlyList<BoardLayoutCard> visibleCards)
    {
        var root = BoardStyles.BoardBackground;
        var runs = new List<SlickRun>();
        var bridges = new List<Bridge>();
        foreach (var column in visibleCards.GroupBy(card => card.Column.Slot))
        {
            SlickRun? run = null;
            foreach (var card in column.OrderBy(card => card.Y))
            {
                if (card.Card.SlickId is not int id)
                {
                    run = null;
                    continue;
                }
                if (run is not null && run.Id == id && run.Bottom == card.Y)
                    run.Bottom = card.Y + card.Height;
                else
                {
                    run = new SlickRun(id, card.Column.Slot, card.X, card.Width, card.Y, card.Y + card.Height);
                    runs.Add(run);
                }
            }
        }

        foreach (var group in visibleCards.Where(card => card.Card.SlickId is not null)
                     .GroupBy(card => card.Card.SlickId!.Value).OrderBy(group => group.Key))
        {
            data.Slicks.TryGetValue(group.Key, out var slick);
            var colour = BoardStyles.ResolveSlick(slick, group.Key);
            // Each slick has an independent sparse layer. Its bridges occupy
            // only the gutter between overlapping runs, never another card.
            var layer = new Dictionary<(int X, int Y), (string Glyph, Rgb Foreground, Rgb Background)>();
            foreach (var card in group)
            {
                var left = card.X - 1;
                var right = card.X + card.Width;
                var bottom = card.Y + card.Height;
                for (var y = Math.Max(BoardLayoutEngine.ContentStartRow, card.Y);
                     y < Math.Min(bottom, canvas.Height - 2); y++)
                {
                    Put(left, y, " ", colour, colour);
                    Put(right, y, " ", colour, colour);
                }
                Put(left, card.Y, "🭅", colour, root);
                Put(right, card.Y, "🭐", colour, root);
                Put(left, bottom - 1, "🭀", root, colour);
                Put(right, bottom - 1, "🭋", root, colour);
                var above = visibleCards.FirstOrDefault(other => other.Column.Slot == card.Column.Slot
                    && other.Y + other.Height == card.Y);
                var below = visibleCards.FirstOrDefault(other => other.Column.Slot == card.Column.Slot
                    && other.Y == bottom);
                if (above is null) Cap(card, true);
                if (below is null) Cap(card, false);

                var plainAbove = above is not null && above.Card.SlickId is null;
                var plainBelow = below is not null && below.Card.SlickId is null;
                // Finish at the outside of the slick's edge row, not at the
                // card face a third-row inside it. The straight side continues
                // into the first third of the bottom corner (last third at the
                // top), so no inset-coloured ledge remains beyond the taper.
                if (plainAbove)
                {
                    Put(left, card.Y, "🭄", colour, root);
                    Put(right, card.Y, "🭏", colour, root);
                }
                if (plainBelow)
                {
                    Put(left, bottom - 1, "🬿", root, colour);
                    Put(right, bottom - 1, "🭊", root, colour);
                }
            }

            var matching = runs.Where(run => run.Id == group.Key).ToArray();
            foreach (var left in matching)
            foreach (var right in matching.Where(run => run.Slot == left.Slot + 1))
            {
                var top = Math.Max(left.Top, right.Top);
                var bottom = Math.Min(left.Bottom, right.Bottom) - 1;
                var startX = left.X + left.Width + 1;
                var endX = right.X - 2;
                // A shared card row permits the connection; it does not
                // confine the bridge to a horizontal strip at that height.
                if (bottom < top || endX - startX != 3) continue;
                int[] upperEdge;
                int[] lowerEdge;
                if (bottom - top <= 1)
                {
                    var down = right.Top > left.Top;
                    var leftTop = down ? left.Bottom - 3 : left.Top;
                    var rightTop = down ? right.Top : right.Bottom - 3;
                    var baseTop = Math.Min(leftTop, rightTop);
                    var offset = Math.Abs(rightTop - leftTop);
                    // Third-row boundary knots give a broad waist and exact
                    // glyph joins. The endpoints span three rows of each bank.
                    upperEdge = offset == 1 ? [0, 1, 2, 3, 3] : [0, 2, 4, 5, 6];
                    lowerEdge = offset == 1 ? [9, 9, 10, 11, 12] : [9, 10, 11, 13, 15];
                    if (!down)
                    {
                        Array.Reverse(upperEdge);
                        Array.Reverse(lowerEdge);
                    }
                    upperEdge = upperEdge.Select(y => y + baseTop * 3).ToArray();
                    lowerEdge = lowerEdge.Select(y => y + baseTop * 3).ToArray();
                }
                else
                {
                    upperEdge = [top * 3, top * 3 + 2, top * 3 + 3, top * 3 + 2, top * 3];
                    lowerEdge = [bottom * 3 + 3, bottom * 3 + 1, bottom * 3, bottom * 3 + 1, bottom * 3 + 3];
                }
                bridges.Add(new Bridge(left, right, colour, startX, upperEdge, lowerEdge));
            }
            foreach (var (position, cell) in layer)
                canvas.SetCell(position.X, position.Y, cell.Glyph, cell.Foreground, cell.Background);

            void Put(int x, int y, string glyph, Rgb foreground, Rgb background)
            {
                if (x >= 0 && x < canvas.Width && y >= BoardLayoutEngine.ContentStartRow && y < canvas.Height - 2)
                    layer[(x, y)] = (glyph, foreground, background);
            }
            void Cap(BoardLayoutCard card, bool top)
            {
                var y = top ? card.Y - 1 : card.Y + card.Height;
                Put(card.X - 1, y, top ? "🭇" : "🭌", top ? colour : root, top ? root : colour);
                for (var x = card.X; x < card.X + card.Width; x++)
                    Put(x, y, top ? "🬎" : "🬂", top ? root : colour, top ? colour : root);
                Put(card.X + card.Width, y, top ? "🬼" : "🭁", top ? colour : root, top ? root : colour);
            }
        }
        DrawBridges(canvas, bridges, root);
    }

    private static void DrawBridges(TerminalCanvas canvas, List<Bridge> bridges, Rgb root)
    {
        var joins = new List<(int X, int[] Edge, Rgb Above, Rgb Below)>();
        foreach (var above in bridges)
        foreach (var below in bridges)
        {
            if (above.X != below.X || above.Left.Id == below.Left.Id
                || above.Left.Bottom != below.Left.Top || above.Right.Bottom != below.Right.Top)
                continue;
            // Only close a gap when the two outlines need the same terminal
            // cell. Resolve the whole seam so no root-coloured slits remain
            // on the next row of a diagonal that crosses a cell boundary.
            var sharesCell = Enumerable.Range(0, 4).Any(i =>
                (Math.Max(above.Lower[i], above.Lower[i + 1]) + 2) / 3
                    > Math.Min(below.Upper[i], below.Upper[i + 1]) / 3);
            if (!sharesCell) continue;
            var leftY = above.Left.Bottom;
            var rightY = above.Right.Bottom;
            int[]? seam = Math.Abs(rightY - leftY) switch
            {
                1 => [0, 1, 2, 2, 3],
                2 => [0, 1, 3, 5, 6],
                _ => null
            };
            if (seam is null) continue;
            if (rightY < leftY) Array.Reverse(seam);
            seam = seam.Select(y => y + Math.Min(leftY, rightY) * 3).ToArray();
            above.Lower = seam;
            below.Upper = seam;
            joins.Add((above.X, seam, above.Colour, below.Colour));
        }

        foreach (var group in bridges.GroupBy(bridge => bridge.Left.Id))
        {
            var cells = new Dictionary<(int X, int Y), (string Glyph, Rgb Foreground, Rgb Background)>();
            foreach (var bridge in group)
            {
                // Closing a seam can extend an attachment further along its
                // own slick. Keep those facing banks solid right to the join.
                for (var y = Math.Max(BoardLayoutEngine.ContentStartRow, bridge.Upper[0] / 3);
                     y < Math.Min(canvas.Height - 2, bridge.Lower[0] / 3); y++)
                    Put(bridge.X - 1, y, (" ", bridge.Colour, bridge.Colour));
                for (var y = Math.Max(BoardLayoutEngine.ContentStartRow, bridge.Upper[4] / 3);
                     y < Math.Min(canvas.Height - 2, bridge.Lower[4] / 3); y++)
                    Put(bridge.X + 4, y, (" ", bridge.Colour, bridge.Colour));
                for (var i = 0; i < 4; i++)
                {
                    var first = Math.Max(BoardLayoutEngine.ContentStartRow,
                        (int)Math.Floor(Math.Min(bridge.Upper[i], bridge.Upper[i + 1]) / 3d));
                    var last = Math.Min(canvas.Height - 2,
                        (int)Math.Ceiling(Math.Max(bridge.Lower[i], bridge.Lower[i + 1]) / 3d));
                    for (var row = first; row < last; row++)
                    {
                        var topEdge = Math.Max(bridge.Upper[i], bridge.Upper[i + 1]) > row * 3;
                        var bottomEdge = Math.Min(bridge.Lower[i], bridge.Lower[i + 1]) < (row + 1) * 3;
                        var cell = (Glyph: " ", Foreground: bridge.Colour, Background: bridge.Colour);
                        if (topEdge || bottomEdge)
                        {
                            var edge = topEdge ? bridge.Upper : bridge.Lower;
                            cell = EdgeCell(edge[i] - row * 3, edge[i + 1] - row * 3,
                                topEdge ? root : bridge.Colour, topEdge ? bridge.Colour : root);
                        }
                        var position = (bridge.X + i, row);
                        // Preserve the union where branches of the same slick meet.
                        if (cells.TryGetValue(position, out var existing) && existing != cell)
                            cell = (" ", bridge.Colour, bridge.Colour);
                        cells[position] = cell;
                    }
                }
            }
            foreach (var (position, cell) in cells)
                Put(position.X, position.Y, cell);
        }

        // Both sides of a shared boundary belong to slicks. Paint these cells
        // last, with two slick colours and no board background, independent of ID.
        foreach (var join in joins)
        for (var i = 0; i < 4; i++)
        {
            var first = (int)Math.Floor(Math.Min(join.Edge[i], join.Edge[i + 1]) / 3d);
            var last = (int)Math.Ceiling(Math.Max(join.Edge[i], join.Edge[i + 1]) / 3d);
            for (var row = first; row < last; row++)
                Put(join.X + i, row, EdgeCell(join.Edge[i] - row * 3,
                    join.Edge[i + 1] - row * 3, join.Above, join.Below));
        }

        void Put(int x, int y, (string Glyph, Rgb Foreground, Rgb Background) cell)
        {
            if (x >= 0 && x < canvas.Width && y >= BoardLayoutEngine.ContentStartRow && y < canvas.Height - 2)
                canvas.SetCell(x, y, cell.Glyph, cell.Foreground, cell.Background);
        }
    }

    private static (string Glyph, Rgb Foreground, Rgb Background) EdgeCell(int left, int right, Rgb above, Rgb below)
        // Flat thirds use an upper block; diagonals fill below their line.
        => (DiagonalBelow(left, right), left == right ? above : below, left == right ? below : above);

    private sealed class Bridge(SlickRun left, SlickRun right, Rgb colour, int x, int[] upper, int[] lower)
    {
        public SlickRun Left { get; } = left;
        public SlickRun Right { get; } = right;
        public Rgb Colour { get; } = colour;
        public int X { get; } = x;
        public int[] Upper { get; set; } = upper;
        public int[] Lower { get; set; } = lower;
    }

    // Boundary heights at the left and right edge of one cell, in thirds.
    // Crossing pieces use a half-cell endpoint on the adjoining terminal row.
    private static string DiagonalBelow(int left, int right) => (left, right) switch
    {
        (1, 1) => "🬂", (2, 2) => "🬎",
        (0, 1) => "🭍", (1, 2) => "🭑", (2, 3) => "🬽",
        (1, 0) => "🭂", (2, 1) => "🭆", (3, 2) => "🭈",
        (0, 2) => "🭏", (1, 3) => "🬿", (2, 4) => "🬼", (-1, 1) => "🭌",
        (2, 0) => "🭄", (3, 1) => "🭊", (4, 2) => "🭇", (1, -1) => "🭁",
        _ => throw new ArgumentOutOfRangeException(nameof(left), $"Unsupported boundary {left}/{right}")
    };

    private sealed class SlickRun(int id, int slot, int x, int width, int top, int bottom)
    {
        public int Id { get; } = id;
        public int Slot { get; } = slot;
        public int X { get; } = x;
        public int Width { get; } = width;
        public int Top { get; } = top;
        public int Bottom { get; set; } = bottom;
    }
}
