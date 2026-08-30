internal sealed class TerminalApplication(
    BoardOilClient client,
    AppOptions options,
    TerminalRuntime? terminal)
{
    private readonly BoardOilClient _client = client;
    private readonly AppOptions _options = options;
    private readonly TerminalRuntime? _terminal = terminal;

    public async Task<int> RunAsync()
    {
        var currentBoardId = _options.BoardId;
        var data = await _client.LoadBoardAsync(currentBoardId);
        var screen = new BoardScreen(data, ConnectedStatus());

        if (_terminal is null)
        {
            var viewport = TerminalRuntime.MeasureViewport(158, 44);
            Console.Write(screen.Render(viewport).Canvas.Render());
            return 0;
        }

        while (true)
        {
            var command = await _terminal.RunAsync(screen);
            if (command == BoardCommand.Quit)
            {
                return 0;
            }

            if (command == BoardCommand.Reload)
            {
                await ReloadAsync(screen, currentBoardId);
                continue;
            }

            if (command == BoardCommand.OpenCard)
            {
                if (await RunCardAsync(screen, currentBoardId))
                {
                    return 0;
                }

                continue;
            }

            if (command == BoardCommand.CreateCard)
            {
                if (await RunCreateCardAsync(screen, currentBoardId))
                {
                    return 0;
                }

                continue;
            }

            if (command == BoardCommand.MoveCard)
            {
                await MoveCardAsync(screen, currentBoardId);
                continue;
            }

            var chosenBoard = await PickBoardAsync(screen, currentBoardId);
            if (chosenBoard is null || chosenBoard.Id == currentBoardId)
            {
                screen.SetStatus(ConnectedStatus());
                continue;
            }

            try
            {
                screen.SetStatus(UnicodeDisplay.Truncate(
                    $"● loading {chosenBoard.Name}",
                    Math.Max(8, _terminal.CurrentViewport.Width / 2)));
                _terminal.Draw(screen.Render(_terminal.CurrentViewport));
                data = await _client.LoadBoardAsync(chosenBoard.Id);
                currentBoardId = chosenBoard.Id;
                screen.SwitchBoard(data);
                screen.SetStatus(ConnectedStatus());
            }
            catch (Exception exception)
            {
                screen.SetStatus(ErrorStatus(exception));
            }
        }
    }

    private async Task MoveCardAsync(BoardScreen screen, int boardId)
    {
        var move = screen.PendingMove;
        if (move is null)
        {
            return;
        }

        _terminal!.Draw(screen.Render(_terminal.CurrentViewport));
        try
        {
            var movedCard = await _client.MoveCardAsync(boardId, move);
            screen.ApplyMovedCard(movedCard);
        }
        catch (Exception exception)
        {
            screen.CancelMove();
            screen.SetStatus(ErrorStatus(exception));
        }
    }

    private async Task<bool> RunCreateCardAsync(BoardScreen boardScreen, int boardId)
    {
        var selectedColumn = boardScreen.SelectedColumn;
        if (selectedColumn is null)
        {
            return false;
        }

        var draft = CardDraft.CreateNew(boardScreen.Data, selectedColumn.Id);
        if (draft is null)
        {
            boardScreen.SetStatus("! board has no default card type");
            return false;
        }

        var detailScreen = new CardDetailScreen(boardScreen.Data, draft, ConnectedStatus());
        while (true)
        {
            var command = await _terminal!.RunAsync(detailScreen);
            if (command == CardDetailCommand.Close)
            {
                return false;
            }

            if (command == CardDetailCommand.Quit)
            {
                return true;
            }

            var pendingDraft = detailScreen.PendingDraft;
            if (pendingDraft is null)
            {
                continue;
            }

            detailScreen.BeginSaving();
            _terminal.Draw(detailScreen.Render(_terminal.CurrentViewport));
            try
            {
                var createdCard = await _client.CreateCardAsync(boardId, pendingDraft);
                boardScreen.ReplaceCard(createdCard);
                return false;
            }
            catch (Exception exception)
            {
                detailScreen.SetSaveError(exception);
            }
        }
    }

    private async Task<bool> RunCardAsync(BoardScreen boardScreen, int boardId)
    {
        var selectedCard = boardScreen.SelectedCard;
        if (selectedCard is null)
        {
            return false;
        }

        var detailScreen = new CardDetailScreen(boardScreen.Data, selectedCard, ConnectedStatus());
        while (true)
        {
            var command = await _terminal!.RunAsync(detailScreen);
            if (command == CardDetailCommand.Close)
            {
                return false;
            }

            if (command == CardDetailCommand.Quit)
            {
                return true;
            }

            var draft = detailScreen.PendingDraft;
            if (draft is null)
            {
                continue;
            }

            detailScreen.BeginSaving();
            _terminal.Draw(detailScreen.Render(_terminal.CurrentViewport));
            try
            {
                var updatedCard = await _client.UpdateCardAsync(boardId, selectedCard.Id, draft);
                boardScreen.ReplaceCard(updatedCard);
                detailScreen.ApplySaved(boardScreen.Data, updatedCard);
                selectedCard = updatedCard;
            }
            catch (Exception exception)
            {
                detailScreen.SetSaveError(exception);
            }
        }
    }

    private async Task ReloadAsync(BoardScreen screen, int currentBoardId)
    {
        screen.SetStatus("● loading");
        _terminal!.Draw(screen.Render(_terminal.CurrentViewport));
        try
        {
            var selectedCardId = screen.SelectedCardId;
            var data = await _client.LoadBoardAsync(currentBoardId);
            screen.ReplaceData(data, selectedCardId);
            screen.SetStatus(ConnectedStatus());
        }
        catch (Exception exception)
        {
            screen.SetStatus(ErrorStatus(exception));
        }
    }

    private async Task<BoardSummary?> PickBoardAsync(BoardScreen screen, int currentBoardId)
    {
        screen.SetStatus("● loading boards");
        _terminal!.Draw(screen.Render(_terminal.CurrentViewport));
        try
        {
            var boards = await _client.LoadBoardsAsync();
            if (boards.Count == 0)
            {
                return null;
            }

            var picker = new BoardPickerScreen(boards, currentBoardId, ConnectedStatus());
            var result = await _terminal.RunAsync(picker);
            return result.Board;
        }
        catch (Exception exception)
        {
            screen.SetStatus(ErrorStatus(exception));
            return null;
        }
    }

    private string ConnectedStatus() =>
        $"● {_client.IdentityLabel}";

    private string ErrorStatus(Exception exception) =>
        UnicodeDisplay.Truncate(
            $"! {exception.Message}",
            Math.Max(8, (_terminal?.CurrentViewport.Width ?? 80) / 2));
}
