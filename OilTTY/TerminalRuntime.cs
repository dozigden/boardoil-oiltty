internal readonly record struct TerminalViewport(int Width, int Height);

internal readonly record struct TerminalCursor(int X, int Y);

internal sealed record TerminalFrame(TerminalCanvas Canvas, TerminalCursor? Cursor = null);

internal interface ITerminalScreen<TResult>
{
    TerminalFrame Render(TerminalViewport viewport);

    ScreenUpdate<TResult> HandleKey(ConsoleKeyInfo key, TerminalViewport viewport);
}

internal readonly record struct ScreenUpdate<TResult>(bool IsComplete, bool Redraw, TResult? Result)
{
    public static ScreenUpdate<TResult> Continue(bool redraw = true) =>
        new(false, redraw, default);

    public static ScreenUpdate<TResult> Complete(TResult result) =>
        new(true, false, result);
}

internal sealed class TerminalRuntime
{
    public TerminalViewport CurrentViewport => MeasureViewport(80, 24);

    public static TerminalViewport MeasureViewport(int fallbackWidth, int fallbackHeight)
    {
        try
        {
            var width = Console.IsOutputRedirected ? fallbackWidth : Console.WindowWidth;
            var height = Console.IsOutputRedirected ? fallbackHeight : Console.WindowHeight;
            return new TerminalViewport(Math.Max(40, width), Math.Max(12, height));
        }
        catch (IOException)
        {
            return new TerminalViewport(fallbackWidth, fallbackHeight);
        }
    }

    public async Task<TResult> RunAsync<TResult>(
        ITerminalScreen<TResult> screen,
        CancellationToken cancellationToken = default)
    {
        var lastViewport = default(TerminalViewport);
        var redraw = true;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var viewport = CurrentViewport;
            if (viewport != lastViewport)
            {
                redraw = true;
                lastViewport = viewport;
            }

            if (redraw)
            {
                Draw(screen.Render(viewport));
                redraw = false;
            }

            if (!Console.KeyAvailable)
            {
                await Task.Delay(40, cancellationToken);
                continue;
            }

            var update = screen.HandleKey(Console.ReadKey(intercept: true), viewport);
            if (update.IsComplete)
            {
                Console.Write("\e[?25l");
                return update.Result!;
            }

            redraw = update.Redraw;
        }
    }

    public void Draw(TerminalFrame frame)
    {
        Console.Write("\e[H" + frame.Canvas.Render());
        if (frame.Cursor is TerminalCursor cursor)
        {
            var x = Math.Clamp(cursor.X, 0, frame.Canvas.Width - 1);
            var y = Math.Clamp(cursor.Y, 0, frame.Canvas.Height - 1);
            Console.Write($"\e[{y + 1};{x + 1}H\e[?25h");
        }
        else
        {
            Console.Write("\e[?25l");
        }
    }
}
