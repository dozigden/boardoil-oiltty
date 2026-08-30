internal sealed record BoardLayout(
    int Width,
    int Height,
    IReadOnlyList<BoardLayoutColumn> Columns,
    IReadOnlyList<BoardLayoutCard> Cards,
    int SelectedColumnFirstCard,
    int SelectedColumnIndex);

internal sealed record BoardLayoutColumn(
    BoardColumn Column,
    int BoardIndex,
    int Slot,
    int X,
    int Width);

internal sealed record BoardLayoutCard(
    BoardCard Card,
    BoardLayoutColumn Column,
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyList<string> TitleLines,
    string? AssignedUserLabel)
{
    public double Centre => Y + (Height / 2.0);
}

internal sealed class BoardLayoutEngine
{
    public const int ContentStartRow = 3;

    private const int MinimumColumnWidth = 32;

    public BoardLayout Create(
        BoardSnapshot board,
        BoardSelection selection,
        int requestedWidth,
        int requestedHeight)
    {
        var width = Math.Max(40, requestedWidth);
        var height = Math.Max(12, requestedHeight);
        if (board.Columns.Count == 0)
        {
            return new BoardLayout(width, height, [], [], 0, 0);
        }

        var columns = LayoutColumns(board, selection.ColumnIndex, width);
        var contentBottom = height - 2;
        var cards = new List<BoardLayoutCard>();
        var selectedColumnFirstCard = selection.FirstCardFor(board.Columns[selection.ColumnIndex]);
        foreach (var column in columns)
        {
            var selectedColumn = column.BoardIndex == selection.ColumnIndex;
            var firstCard = selection.FirstCardFor(column.Column);
            if (selectedColumn && selection.CardIndex < firstCard)
            {
                firstCard = selection.CardIndex;
            }

            var laidOut = LayoutColumnCards(column, firstCard, contentBottom);
            while (selectedColumn
                   && firstCard < selection.CardIndex
                   && laidOut.All(card => card.Card.Id != selection.CardId
                                                || CardExtendsPastViewport(card, contentBottom)))
            {
                firstCard++;
                laidOut = LayoutColumnCards(column, firstCard, contentBottom);
            }

            if (selectedColumn)
            {
                selectedColumnFirstCard = firstCard;
            }

            cards.AddRange(laidOut);
        }

        return new BoardLayout(
            width,
            height,
            columns,
            cards,
            selectedColumnFirstCard,
            selection.ColumnIndex);
    }

    public int FindNearestCardIndex(
        BoardSnapshot board,
        BoardSelection selection,
        int columnDelta,
        int requestedWidth,
        int requestedHeight)
    {
        if (board.Columns.Count == 0)
        {
            return 0;
        }

        var targetColumnIndex = Math.Clamp(
            selection.ColumnIndex + columnDelta,
            0,
            board.Columns.Count - 1);
        if (targetColumnIndex == selection.ColumnIndex)
        {
            return selection.CardIndex;
        }

        var sourceLayout = Create(board, selection, requestedWidth, requestedHeight);
        var selectedCard = sourceLayout.Cards.FirstOrDefault(card => card.Card.Id == selection.CardId);
        var sourceCentre = selectedCard is null
            ? ContentStartRow + (((sourceLayout.Height - 2) - ContentStartRow) / 2.0)
            : selectedCard.Centre;

        var targetColumn = LayoutColumns(board, targetColumnIndex, sourceLayout.Width)
            .Single(column => column.BoardIndex == targetColumnIndex);
        var targetCards = LayoutColumnCards(
            targetColumn,
            selection.FirstCardFor(targetColumn.Column),
            sourceLayout.Height - 2);
        if (targetCards.Count == 0)
        {
            return 0;
        }

        var fullyVisibleCards = targetCards
            .Where(card => card.Y + card.Height <= sourceLayout.Height - 2)
            .ToArray();
        var candidates = fullyVisibleCards.Length > 0 ? fullyVisibleCards : targetCards.ToArray();
        var nearest = candidates.MinBy(card => Math.Abs(card.Centre - sourceCentre))!;
        return FindCardIndex(targetColumn.Column, nearest.Card.Id);
    }

    public static int FindCardIndex(BoardColumn column, int cardId)
    {
        for (var index = 0; index < column.Cards.Count; index++)
        {
            if (column.Cards[index].Id == cardId)
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<BoardLayoutColumn> LayoutColumns(
        BoardSnapshot board,
        int selectedColumnIndex,
        int width)
    {
        var visibleCount = Math.Min(
            board.Columns.Count,
            Math.Max(1, (width + 1) / MinimumColumnWidth));
        var firstIndex = Math.Clamp(
            selectedColumnIndex - (visibleCount / 2),
            0,
            board.Columns.Count - visibleCount);
        var standardWidth = width / visibleCount;
        var remainder = width % visibleCount;

        var result = new List<BoardLayoutColumn>(visibleCount);
        var x = 0;
        for (var slot = 0; slot < visibleCount; slot++)
        {
            var columnWidth = standardWidth + (slot < remainder ? 1 : 0);
            var boardIndex = firstIndex + slot;
            result.Add(new BoardLayoutColumn(board.Columns[boardIndex], boardIndex, slot, x, columnWidth));
            x += columnWidth;
        }

        return result;
    }

    private static IReadOnlyList<BoardLayoutCard> LayoutColumnCards(
        BoardLayoutColumn column,
        int firstCard,
        int contentBottom)
    {
        var result = new List<BoardLayoutCard>();
        var y = ContentStartRow;
        BoardCard? previous = null;
        for (var index = firstCard; index < column.Column.Cards.Count; index++)
        {
            var card = column.Column.Cards[index];
            var cardWidth = Math.Max(12, column.Width - 4);
            var needsSpacerAbove = previous is null
                ? card.SlickId is not null
                : previous.SlickId is not null || card.SlickId is not null;
            var cardY = y + (needsSpacerAbove ? 1 : 0);
            var titleLines = ResolveTitleLines(card, cardWidth);
            var assignedUserLabel = ResolveAssignedUserLabel(card);
            var cardHeight = 2
                + titleLines.Count
                + (assignedUserLabel is null ? 0 : 1)
                + ResolveTagRowCount(card, cardWidth);
            var requiredBottom = cardY + cardHeight + (card.SlickId is not null ? 1 : 0);
            if (cardY >= contentBottom)
            {
                break;
            }

            result.Add(new BoardLayoutCard(
                card,
                column,
                column.X + 2,
                cardY,
                cardWidth,
                cardHeight,
                titleLines,
                assignedUserLabel));
            y = cardY + cardHeight;
            previous = card;
            if (requiredBottom > contentBottom)
            {
                break;
            }
        }

        return result;
    }

    private static bool CardExtendsPastViewport(BoardLayoutCard card, int contentBottom) =>
        card.Y + card.Height + (card.Card.SlickId is null ? 0 : 1) > contentBottom;

    private static IReadOnlyList<string> ResolveTitleLines(BoardCard card, int cardWidth)
    {
        var numberWidth = UnicodeDisplay.TextWidth($"#{card.Id}");
        var emojiWidth = string.IsNullOrWhiteSpace(card.CardTypeEmoji)
            ? 0
            : UnicodeDisplay.TextWidth(UnicodeDisplay.EmojiLabelPrefix(card.CardTypeEmoji));
        var firstLineWidth = Math.Max(2, cardWidth - 5 - emojiWidth - numberWidth);
        var continuationWidth = Math.Max(2, cardWidth - 4 - emojiWidth);
        return UnicodeDisplay.WrapText(card.Title, firstLineWidth, continuationWidth);
    }

    private static int ResolveTagRowCount(BoardCard card, int cardWidth)
    {
        if (card.Tags.Count == 0)
        {
            return 0;
        }

        var availableWidth = Math.Max(3, cardWidth - 3);
        var rows = 1;
        var usedWidth = 0;
        foreach (var tag in card.Tags)
        {
            var tagWidth = UnicodeDisplay.TextWidth(ResolveTagLabel(tag, availableWidth)) + 2;
            if (usedWidth > 0 && usedWidth + tagWidth > availableWidth)
            {
                rows++;
                usedWidth = 0;
            }

            usedWidth += tagWidth;
        }

        return rows;
    }

    public static string ResolveTagLabel(CardTag tag, int availableWidth)
    {
        var emojiPart = string.IsNullOrWhiteSpace(tag.Emoji)
            ? string.Empty
            : UnicodeDisplay.EmojiLabelPrefix(tag.Emoji);
        return UnicodeDisplay.Truncate($"{emojiPart}{tag.Name}", Math.Max(1, availableWidth - 2));
    }

    private static string? ResolveAssignedUserLabel(BoardCard card)
    {
        if (!string.IsNullOrWhiteSpace(card.AssignedUserDisplayName))
        {
            return card.AssignedUserDisplayName.Trim();
        }

        return card.AssignedUserId is int userId ? $"User #{userId}" : null;
    }
}
