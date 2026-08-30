internal sealed record BoardPickerResult(BoardSummary? Board);

internal sealed class BoardPickerScreen : ITerminalScreen<BoardPickerResult>
{
    private readonly IReadOnlyList<BoardSummary> _boards;
    private readonly int _currentBoardId;
    private readonly string _status;
    private readonly BoardPickerRenderer _renderer = new();
    private int _selectedIndex;

    public BoardPickerScreen(
        IReadOnlyList<BoardSummary> boards,
        int currentBoardId,
        string status)
    {
        _boards = boards;
        _currentBoardId = currentBoardId;
        _status = status;
        _selectedIndex = FindBoardIndex(boards, currentBoardId);
    }

    public TerminalFrame Render(TerminalViewport viewport) =>
        _renderer.Render(
            _boards,
            _selectedIndex,
            _currentBoardId,
            viewport.Width,
            viewport.Height,
            _status);

    public ScreenUpdate<BoardPickerResult> HandleKey(ConsoleKeyInfo key, TerminalViewport viewport)
    {
        if (BoardStyles.TryToggleTheme(key))
        {
            return ScreenUpdate<BoardPickerResult>.Continue();
        }

        if (key.Key is ConsoleKey.Escape or ConsoleKey.B or ConsoleKey.Q)
        {
            return ScreenUpdate<BoardPickerResult>.Complete(new BoardPickerResult(null));
        }

        if (key.Key is ConsoleKey.J or ConsoleKey.DownArrow)
        {
            if (_boards.Count == 0)
            {
                return ScreenUpdate<BoardPickerResult>.Continue(redraw: false);
            }

            _selectedIndex = Math.Min(_boards.Count - 1, _selectedIndex + 1);
            return ScreenUpdate<BoardPickerResult>.Continue();
        }

        if (key.Key is ConsoleKey.K or ConsoleKey.UpArrow)
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
            return ScreenUpdate<BoardPickerResult>.Continue();
        }

        if (key.Key == ConsoleKey.Home)
        {
            _selectedIndex = 0;
            return ScreenUpdate<BoardPickerResult>.Continue();
        }

        if (key.Key == ConsoleKey.End)
        {
            _selectedIndex = Math.Max(0, _boards.Count - 1);
            return ScreenUpdate<BoardPickerResult>.Continue();
        }

        if (key.Key == ConsoleKey.Enter && _boards.Count > 0)
        {
            return ScreenUpdate<BoardPickerResult>.Complete(
                new BoardPickerResult(_boards[_selectedIndex]));
        }

        return ScreenUpdate<BoardPickerResult>.Continue(redraw: false);
    }

    private static int FindBoardIndex(IReadOnlyList<BoardSummary> boards, int boardId)
    {
        for (var index = 0; index < boards.Count; index++)
        {
            if (boards[index].Id == boardId)
            {
                return index;
            }
        }

        return 0;
    }
}
