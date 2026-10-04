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
                var above = visibleCards.Any(other => other.Column.Slot == card.Column.Slot
                    && other.Y + other.Height == card.Y);
                var below = visibleCards.Any(other => other.Column.Slot == card.Column.Slot
                    && other.Y == bottom);
                if (!above) Cap(card, true);
                if (!below) Cap(card, false);
            }

            var matching = runs.Where(run => run.Id == group.Key).ToArray();
            foreach (var left in matching)
            foreach (var right in matching.Where(run => run.Slot == left.Slot + 1))
            {
                var top = Math.Max(left.Top, right.Top);
                var bottom = Math.Min(left.Bottom, right.Bottom) - 1;
                var startX = left.X + left.Width + 1;
                var endX = right.X - 2;
                // The layout reserves four gutter cells between the two outer
                // banks. Short overlaps cannot accommodate both saddle edges.
                if (bottom - top < 2 || endX - startX != 3) continue;
                for (var y = Math.Max(top, BoardLayoutEngine.ContentStartRow);
                     y <= Math.Min(bottom, canvas.Height - 3); y++)
                for (var x = startX - 1; x <= endX + 1; x++)
                    Put(x, y, " ", colour, colour);
                string[] upper = ["🭏", "🬽", "🭈", "🭄"];
                string[] lower = ["🭊", "🭂", "🭍", "🬿"];
                for (var i = 0; i < 4; i++)
                {
                    Put(startX + i, top, upper[i], colour, root);
                    Put(startX + i, bottom, lower[i], root, colour);
                }
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
    }

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
