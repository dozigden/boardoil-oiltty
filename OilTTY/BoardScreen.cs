internal enum BoardCommand
{
    Quit,
    Reload,
    PickBoard,
    OpenCard,
    CreateCard,
    MoveCard
}

internal sealed class BoardScreen : ITerminalScreen<BoardCommand>
{
    private readonly BoardLayoutEngine _layoutEngine = new();
    private readonly BoardRenderer _renderer = new();
    private BoardSelection _selection = new();
    private BoardData _data;
    private string _status;
    private int? _movingCardId;
    private BoardData? _moveOriginData;
    private CardMove? _moveOriginPlacement;
    private string? _moveOriginStatus;

    public BoardScreen(BoardData data, string status)
    {
        _data = data;
        _status = status;
        _selection.Normalise(data.Board);
    }

    public int? SelectedCardId => _selection.CardId;

    public int? MovingCardId => _movingCardId;

    public CardMove? PendingMove { get; private set; }

    public BoardData Data => _data;

    public BoardColumn? SelectedColumn =>
        _data.Board.Columns.ElementAtOrDefault(_selection.ColumnIndex);

    public BoardCard? SelectedCard =>
        _data.Board.Columns
            .SelectMany(column => column.Cards)
            .FirstOrDefault(card => card.Id == _selection.CardId);

    public TerminalFrame Render(TerminalViewport viewport)
    {
        var layout = _layoutEngine.Create(
            _data.Board,
            _selection,
            viewport.Width,
            viewport.Height);
        return _renderer.Render(_data, layout, _selection.CardId, _movingCardId, _status);
    }

    public ScreenUpdate<BoardCommand> HandleKey(ConsoleKeyInfo key, TerminalViewport viewport)
    {
        if (BoardStyles.TryToggleTheme(key))
        {
            return ScreenUpdate<BoardCommand>.Continue();
        }

        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)
            || key.Key == ConsoleKey.Q)
        {
            return ScreenUpdate<BoardCommand>.Complete(BoardCommand.Quit);
        }

        if (key.Key == ConsoleKey.Escape)
        {
            if (_movingCardId is not null)
            {
                CancelMove();
                return ScreenUpdate<BoardCommand>.Continue();
            }

            return ScreenUpdate<BoardCommand>.Complete(BoardCommand.Quit);
        }

        if (key.Key is ConsoleKey.J or ConsoleKey.DownArrow)
        {
            if (_movingCardId is int movingCardId)
            {
                MoveCarriedVertical(movingCardId, 1);
            }
            else
            {
                _selection.MoveVertical(_data.Board, 1);
            }

            RememberViewport(viewport);
            return ScreenUpdate<BoardCommand>.Continue();
        }

        if (key.Key is ConsoleKey.K or ConsoleKey.UpArrow)
        {
            if (_movingCardId is int movingCardId)
            {
                MoveCarriedVertical(movingCardId, -1);
            }
            else
            {
                _selection.MoveVertical(_data.Board, -1);
            }

            RememberViewport(viewport);
            return ScreenUpdate<BoardCommand>.Continue();
        }

        if (key.Key is ConsoleKey.H or ConsoleKey.LeftArrow)
        {
            if (_movingCardId is int movingCardId)
            {
                MoveCarriedHorizontal(movingCardId, -1, viewport);
            }
            else
            {
                MoveHorizontal(-1, viewport);
            }

            return ScreenUpdate<BoardCommand>.Continue();
        }

        if (key.Key is ConsoleKey.L or ConsoleKey.RightArrow)
        {
            if (_movingCardId is int movingCardId)
            {
                MoveCarriedHorizontal(movingCardId, 1, viewport);
            }
            else
            {
                MoveHorizontal(1, viewport);
            }

            return ScreenUpdate<BoardCommand>.Continue();
        }

        if (key.Key == ConsoleKey.Spacebar)
        {
            return _movingCardId is null
                ? PickUpSelectedCard()
                : PutDownMovingCard();
        }

        if (_movingCardId is not null)
        {
            return ScreenUpdate<BoardCommand>.Continue(redraw: false);
        }

        if (key.Key == ConsoleKey.R)
        {
            return ScreenUpdate<BoardCommand>.Complete(BoardCommand.Reload);
        }

        if (key.Key == ConsoleKey.B)
        {
            return ScreenUpdate<BoardCommand>.Complete(BoardCommand.PickBoard);
        }

        if (key.Key == ConsoleKey.N && SelectedColumn is not null)
        {
            RememberViewport(viewport);
            return ScreenUpdate<BoardCommand>.Complete(BoardCommand.CreateCard);
        }

        if (key.Key == ConsoleKey.Enter && SelectedCard is not null)
        {
            RememberViewport(viewport);
            return ScreenUpdate<BoardCommand>.Complete(BoardCommand.OpenCard);
        }

        return ScreenUpdate<BoardCommand>.Continue(redraw: false);
    }

    public void SetStatus(string status) =>
        _status = status;

    public void ReplaceData(BoardData data, int? preferredCardId)
    {
        _data = data;
        _selection.Normalise(data.Board, preferredCardId);
    }

    public void SwitchBoard(BoardData data)
    {
        _data = data;
        _selection = new BoardSelection();
        ClearMoveState();
        _selection.Normalise(data.Board);
    }

    public void ReplaceCard(BoardCard updatedCard)
    {
        var columns = _data.Board.Columns
            .Select(column => column with
            {
                Cards = column.Id == updatedCard.BoardColumnId
                    ? column.Cards
                        .Where(card => card.Id != updatedCard.Id)
                        .Append(updatedCard)
                        .OrderBy(card => card.SortKey, StringComparer.Ordinal)
                        .ToArray()
                    : column.Cards
                        .Where(card => card.Id != updatedCard.Id)
                        .ToArray()
            })
            .ToArray();
        _data = _data with { Board = _data.Board with { Columns = columns } };
        _selection.Normalise(_data.Board, updatedCard.Id);
    }

    public void ApplyMovedCard(BoardCard updatedCard)
    {
        var restoredStatus = _moveOriginStatus ?? _status;
        ClearMoveState();
        ReplaceCard(updatedCard);
        _status = restoredStatus;
    }

    public void CancelMove()
    {
        if (_moveOriginData is null)
        {
            return;
        }

        var movingCardId = _movingCardId;
        var originData = _moveOriginData;
        var restoredStatus = _moveOriginStatus ?? _status;
        ClearMoveState();
        _data = originData;
        _selection.Normalise(_data.Board, movingCardId);
        _status = restoredStatus;
    }

    private void MoveHorizontal(int delta, TerminalViewport viewport)
    {
        var targetCardIndex = _layoutEngine.FindNearestCardIndex(
            _data.Board,
            _selection,
            delta,
            viewport.Width,
            viewport.Height);
        RememberViewport(viewport);
        _selection.MoveHorizontal(_data.Board, delta, targetCardIndex);
        RememberViewport(viewport);
    }

    private ScreenUpdate<BoardCommand> PickUpSelectedCard()
    {
        var card = SelectedCard;
        if (card is null)
        {
            return ScreenUpdate<BoardCommand>.Continue(redraw: false);
        }

        _movingCardId = card.Id;
        _moveOriginData = _data;
        _moveOriginPlacement = ResolveMove(card.Id);
        _moveOriginStatus = _status;
        _status = $"◆ moving #{card.Id}";
        return ScreenUpdate<BoardCommand>.Continue();
    }

    private ScreenUpdate<BoardCommand> PutDownMovingCard()
    {
        var move = ResolveMove(_movingCardId!.Value);
        if (move == _moveOriginPlacement)
        {
            CancelMove();
            return ScreenUpdate<BoardCommand>.Continue();
        }

        PendingMove = move;
        _status = $"● saving move for #{move.CardId}";
        return ScreenUpdate<BoardCommand>.Complete(BoardCommand.MoveCard);
    }

    private void MoveCarriedVertical(int cardId, int delta)
    {
        var columnIndex = _selection.ColumnIndex;
        var column = _data.Board.Columns[columnIndex];
        var cards = column.Cards.ToList();
        var cardIndex = cards.FindIndex(card => card.Id == cardId);
        if (cardIndex < 0)
        {
            return;
        }

        var targetIndex = Math.Clamp(cardIndex + delta, 0, cards.Count - 1);
        if (targetIndex == cardIndex)
        {
            return;
        }

        var card = cards[cardIndex];
        cards.RemoveAt(cardIndex);
        cards.Insert(targetIndex, card);
        ReplaceColumn(columnIndex, column with { Cards = cards });
        _selection.Normalise(_data.Board, cardId);
    }

    private void MoveCarriedHorizontal(int cardId, int delta, TerminalViewport viewport)
    {
        var sourceColumnIndex = _selection.ColumnIndex;
        var targetColumnIndex = Math.Clamp(
            sourceColumnIndex + delta,
            0,
            _data.Board.Columns.Count - 1);
        if (targetColumnIndex == sourceColumnIndex)
        {
            return;
        }

        var targetCardIndex = _layoutEngine.FindNearestCardIndex(
            _data.Board,
            _selection,
            delta,
            viewport.Width,
            viewport.Height);
        RememberViewport(viewport);

        var sourceColumn = _data.Board.Columns[sourceColumnIndex];
        var sourceCards = sourceColumn.Cards.ToList();
        var movingIndex = sourceCards.FindIndex(card => card.Id == cardId);
        if (movingIndex < 0)
        {
            return;
        }

        var movingCard = sourceCards[movingIndex] with
        {
            BoardColumnId = _data.Board.Columns[targetColumnIndex].Id
        };
        sourceCards.RemoveAt(movingIndex);

        var targetColumn = _data.Board.Columns[targetColumnIndex];
        var targetCards = targetColumn.Cards.ToList();
        targetCards.Insert(Math.Clamp(targetCardIndex, 0, targetCards.Count), movingCard);

        var columns = _data.Board.Columns.ToArray();
        columns[sourceColumnIndex] = sourceColumn with { Cards = sourceCards };
        columns[targetColumnIndex] = targetColumn with { Cards = targetCards };
        _data = _data with { Board = _data.Board with { Columns = columns } };
        _selection.Normalise(_data.Board, cardId);
        RememberViewport(viewport);
    }

    private CardMove ResolveMove(int cardId)
    {
        foreach (var column in _data.Board.Columns)
        {
            var cardIndex = BoardLayoutEngine.FindCardIndex(column, cardId);
            if (cardIndex >= 0)
            {
                return new CardMove(
                    cardId,
                    column.Id,
                    cardIndex == 0 ? null : column.Cards[cardIndex - 1].Id);
            }
        }

        throw new InvalidOperationException($"Moving card #{cardId} is no longer on the board.");
    }

    private void ReplaceColumn(int columnIndex, BoardColumn column)
    {
        var columns = _data.Board.Columns.ToArray();
        columns[columnIndex] = column;
        _data = _data with { Board = _data.Board with { Columns = columns } };
    }

    private void ClearMoveState()
    {
        _movingCardId = null;
        _moveOriginData = null;
        _moveOriginPlacement = null;
        _moveOriginStatus = null;
        PendingMove = null;
    }

    private void RememberViewport(TerminalViewport viewport)
    {
        if (_data.Board.Columns.Count == 0)
        {
            return;
        }

        var layout = _layoutEngine.Create(
            _data.Board,
            _selection,
            viewport.Width,
            viewport.Height);
        _selection.RememberFirstCard(
            _data.Board.Columns[_selection.ColumnIndex],
            layout.SelectedColumnFirstCard);
    }
}
