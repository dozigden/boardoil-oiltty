internal sealed class TerminalSession : IDisposable
{
    private readonly bool _alternateScreen;
    private readonly bool _originalTreatControlCAsInput;

    public TerminalSession(bool noAlternateScreen)
    {
        _alternateScreen = !noAlternateScreen;
        _originalTreatControlCAsInput = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        Console.Write(_alternateScreen
            ? "\e[?1049h\e[?25l\e[2J\e[H"
            : "\e[?25l\e[2J\e[H");
    }

    public void Dispose()
    {
        Console.Write(_alternateScreen
            ? "\e[0m\e[?25h\e[?1049l"
            : "\e[0m\e[?25h\n");
        Console.TreatControlCAsInput = _originalTreatControlCAsInput;
    }
}
