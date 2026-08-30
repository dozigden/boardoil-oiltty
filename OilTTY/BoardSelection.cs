internal sealed class BoardSelection
{
    private readonly Dictionary<int, int> _columnFirstCards = [];

    public int ColumnIndex { get; private set; }

    public int CardIndex { get; private set; }

    public int? CardId { get; private set; }

    public int FirstCardFor(BoardColumn column) =>
        Math.Clamp(
            _columnFirstCards.GetValueOrDefault(column.Id),
            0,
            Math.Max(0, column.Cards.Count - 1));

    public void RememberFirstCard(BoardColumn column, int cardIndex) =>
        _columnFirstCards[column.Id] = Math.Clamp(
            cardIndex,
            0,
            Math.Max(0, column.Cards.Count - 1));

    public void Normalise(BoardSnapshot board, int? preferredCardId = null)
    {
        if (board.Columns.Count == 0)
        {
            ColumnIndex = 0;
            CardIndex = 0;
            CardId = null;
            return;
        }

        if (preferredCardId is not null)
        {
            for (var columnIndex = 0; columnIndex < board.Columns.Count; columnIndex++)
            {
                var cardIndex = FindCardIndex(board.Columns[columnIndex], preferredCardId.Value);
                if (cardIndex >= 0)
                {
                    ColumnIndex = columnIndex;
                    CardIndex = cardIndex;
                    CardId = preferredCardId;
                    return;
                }
            }
        }

        ColumnIndex = Math.Clamp(ColumnIndex, 0, board.Columns.Count - 1);
        Select(board.Columns[ColumnIndex], CardIndex);
    }

    public void MoveVertical(BoardSnapshot board, int delta)
    {
        var cards = board.Columns[ColumnIndex].Cards;
        if (cards.Count == 0)
        {
            return;
        }

        CardIndex = Math.Clamp(CardIndex + delta, 0, cards.Count - 1);
        CardId = cards[CardIndex].Id;
    }

    public void MoveHorizontal(BoardSnapshot board, int delta, int targetCardIndex)
    {
        if (board.Columns.Count == 0)
        {
            return;
        }

        var targetColumnIndex = Math.Clamp(ColumnIndex + delta, 0, board.Columns.Count - 1);
        if (targetColumnIndex == ColumnIndex)
        {
            return;
        }

        ColumnIndex = targetColumnIndex;
        Select(board.Columns[ColumnIndex], targetCardIndex);
    }

    private void Select(BoardColumn column, int cardIndex)
    {
        if (column.Cards.Count == 0)
        {
            CardIndex = 0;
            CardId = null;
            return;
        }

        CardIndex = Math.Clamp(cardIndex, 0, column.Cards.Count - 1);
        CardId = column.Cards[CardIndex].Id;
    }

    private static int FindCardIndex(BoardColumn column, int cardId)
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
}
