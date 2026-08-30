internal static class BoardSlickRenderer
{
    public static void Draw(
        TerminalCanvas canvas,
        BoardData data,
        IReadOnlyList<BoardLayoutCard> visibleCards)
    {
        var contentBottom = canvas.Height - 2;
        var groups = visibleCards
            .Where(card => card.Card.SlickId is not null)
            .GroupBy(card => card.Card.SlickId!.Value)
            .OrderBy(group => group.Key);
        var masks = new List<Rgb?[,]>();
        foreach (var group in groups)
        {
            data.Slicks.TryGetValue(group.Key, out var slick);
            var colour = BoardStyles.ResolveSlick(slick, group.Key);
            var mask = new Rgb?[canvas.Height * 2, canvas.Width];
            var cards = group.ToArray();
            foreach (var card in cards)
            {
                AddSlickSlots(mask, colour, card, canvas.Width, contentBottom);
            }

            var occupiedSlots = cards
                .Select(card => card.Column.Slot)
                .Distinct()
                .Order()
                .ToArray();
            foreach (var leftSlot in occupiedSlots)
            {
                var rightSlot = leftSlot + 1;
                if (!occupiedSlots.Contains(rightSlot))
                {
                    continue;
                }

                var closestPair = (
                    from left in cards
                    where left.Column.Slot == leftSlot
                    from right in cards
                    where right.Column.Slot == rightSlot
                    let distance = Math.Abs(left.Centre - right.Centre)
                    orderby distance
                    select new { Left = left, Right = right, Distance = distance })
                    .FirstOrDefault();
                if (closestPair is not null
                    && closestPair.Distance <= Math.Max(closestPair.Left.Height, closestPair.Right.Height) + 2)
                {
                    Bridge(mask, colour, closestPair.Left, closestPair.Right, canvas.Width, contentBottom);
                }
            }

            masks.Add(mask);
        }

        CompositeMasks(canvas, masks);
        DrawOuterSlickCorners(canvas, data, visibleCards, contentBottom);
        DrawMatchingSlickJoinEdges(canvas, data, visibleCards, contentBottom);
    }

    private static void DrawOuterSlickCorners(
        TerminalCanvas canvas,
        BoardData data,
        IReadOnlyList<BoardLayoutCard> visibleCards,
        int contentBottom)
    {
        foreach (var columnCards in visibleCards.GroupBy(card => card.Column.Slot))
        {
            var cards = columnCards.OrderBy(card => card.Y).ToArray();
            for (var index = 0; index < cards.Length; index++)
            {
                var card = cards[index];
                if (card.Card.SlickId is not int slickId)
                {
                    continue;
                }

                data.Slicks.TryGetValue(slickId, out var slick);
                var colour = BoardStyles.ResolveSlick(slick, slickId);
                var leftX = card.X - 1;
                var rightX = card.X + card.Width;
                var hasSlickAbove = index > 0 && cards[index - 1].Card.SlickId is not null;
                if (!hasSlickAbove)
                {
                    var row = card.Y - 1;
                    if (row >= 0 && row < contentBottom)
                    {
                        canvas.Put(leftX, row, "▗", colour, canvas.BackgroundAt(leftX, row));
                        canvas.Put(rightX, row, "▖", colour, canvas.BackgroundAt(rightX, row));
                    }
                }

                var hasSlickBelow = index < cards.Length - 1 && cards[index + 1].Card.SlickId is not null;
                if (!hasSlickBelow)
                {
                    var row = card.Y + card.Height;
                    if (row >= 0 && row < contentBottom)
                    {
                        canvas.Put(leftX, row, "▝", colour, canvas.BackgroundAt(leftX, row));
                        canvas.Put(rightX, row, "▘", colour, canvas.BackgroundAt(rightX, row));
                    }
                }
            }
        }
    }

    private static void DrawMatchingSlickJoinEdges(
        TerminalCanvas canvas,
        BoardData data,
        IReadOnlyList<BoardLayoutCard> visibleCards,
        int contentBottom)
    {
        foreach (var columnCards in visibleCards.GroupBy(card => card.Column.Slot))
        {
            var cards = columnCards.OrderBy(card => card.Y).ToArray();
            for (var index = 0; index < cards.Length - 1; index++)
            {
                var upper = cards[index];
                var lower = cards[index + 1];
                if (upper.Card.SlickId is not int slickId
                    || lower.Card.SlickId != slickId
                    || lower.Y != upper.Y + upper.Height + 1)
                {
                    continue;
                }

                data.Slicks.TryGetValue(slickId, out var slick);
                var colour = BoardStyles.ResolveSlick(slick, slickId);
                var row = upper.Y + upper.Height;
                if (row < 0 || row >= contentBottom)
                {
                    continue;
                }

                var leftX = upper.X - 1;
                var rightX = upper.X + upper.Width;
                canvas.Put(leftX, row, "▐", colour, canvas.BackgroundAt(leftX, row));
                canvas.Put(rightX, row, "▌", colour, canvas.BackgroundAt(rightX, row));
            }
        }
    }

    private static void AddSlickSlots(
        Rgb?[,] mask,
        Rgb colour,
        BoardLayoutCard card,
        int width,
        int height)
    {
        var leftX = card.X - 1;
        var rightX = card.X + card.Width;
        var y0 = Math.Max(0, card.Y * 2);
        var y1 = Math.Min(height * 2, (card.Y + card.Height) * 2);
        for (var y = y0; y < y1; y++)
        {
            if (leftX >= 0 && leftX < width)
            {
                mask[y, leftX] = colour;
            }

            if (rightX >= 0 && rightX < width)
            {
                mask[y, rightX] = colour;
            }
        }

        AddSlickCap(
            mask,
            colour,
            card.X - 1,
            card.X + card.Width,
            card.Y - 1,
            topHalfOnly: false,
            bottomHalfOnly: true,
            width: width,
            height: height);
        AddSlickCap(
            mask,
            colour,
            card.X - 1,
            card.X + card.Width,
            card.Y + card.Height,
            topHalfOnly: true,
            bottomHalfOnly: false,
            width: width,
            height: height);
    }

    private static void AddSlickCap(
        Rgb?[,] mask,
        Rgb colour,
        int x0,
        int x1,
        int row,
        bool topHalfOnly,
        bool bottomHalfOnly,
        int width,
        int height)
    {
        if (row < 0 || row >= height)
        {
            return;
        }

        var topY = row * 2;
        var bottomY = topY + 1;
        for (var x = Math.Max(0, x0 + 1); x <= Math.Min(width - 1, x1 - 1); x++)
        {
            if (!bottomHalfOnly)
            {
                mask[topY, x] = colour;
            }

            if (!topHalfOnly)
            {
                mask[bottomY, x] = colour;
            }
        }
    }

    private static void Bridge(
        Rgb?[,] mask,
        Rgb colour,
        BoardLayoutCard left,
        BoardLayoutCard right,
        int width,
        int height)
    {
        var startX = left.X + left.Width;
        var endX = right.X - 1;
        if (endX < startX)
        {
            return;
        }

        for (var x = Math.Max(0, startX); x <= Math.Min(width - 1, endX); x++)
        {
            var progress = endX == startX ? 0.5 : (x - startX) / (double)(endX - startX);
            var topEdge = (left.Y * 2) + (((right.Y - left.Y) * 2) * progress);
            var leftBottom = (left.Y + left.Height) * 2;
            var rightBottom = (right.Y + right.Height) * 2;
            var bottomEdge = leftBottom + ((rightBottom - leftBottom) * progress);

            var firstY = (int)Math.Floor(topEdge) + 1;
            var lastY = (int)Math.Ceiling(bottomEdge) - 2;
            for (var y = Math.Max(0, firstY); y <= Math.Min((height * 2) - 1, lastY); y++)
            {
                mask[y, x] = colour;
            }
        }
    }

    private static void CompositeMasks(TerminalCanvas canvas, IReadOnlyList<Rgb?[,]> masks)
    {
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                Rgb? top = null;
                Rgb? bottom = null;
                foreach (var mask in masks)
                {
                    top = mask[y * 2, x] ?? top;
                    bottom = mask[(y * 2) + 1, x] ?? bottom;
                }

                if (top is null && bottom is null)
                {
                    continue;
                }

                var baseColour = canvas.BackgroundAt(x, y);
                if (top == bottom)
                {
                    canvas.SetCell(x, y, " ", BoardStyles.TextStrong, top ?? baseColour);
                }
                else
                {
                    canvas.SetCell(x, y, "▀", top ?? baseColour, bottom ?? baseColour);
                }
            }
        }
    }
}
