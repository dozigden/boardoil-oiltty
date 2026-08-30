using Xunit;

public sealed class LoginApplicationTests
{
    private static readonly TerminalViewport Viewport = new(80, 24);

    [Fact]
    public void Login_CollectsAndNormalisesServerBeforeCredentials()
    {
        var screen = new LoginApplication(initialServer: null, new TerminalRuntime());

        Type(screen, "https://boardoil.test/team");
        screen.HandleKey(Key('\r', ConsoleKey.Enter), Viewport);
        Type(screen, "alice");
        screen.HandleKey(Key('\r', ConsoleKey.Enter), Viewport);
        Type(screen, "secret");
        var update = screen.HandleKey(Key('\r', ConsoleKey.Enter), Viewport);

        Assert.True(update.IsComplete);
        Assert.Equal(new Uri("https://boardoil.test/team/"), update.Result!.Server);
        Assert.Equal("alice", update.Result.UserName);
        Assert.Equal("secret", update.Result.Password);
    }

    [Fact]
    public void Login_DefaultsToRememberedServerAndStartsAtUsername()
    {
        var screen = new LoginApplication(
            new Uri("https://remembered.test/boardoil/"),
            new TerminalRuntime());

        var frame = screen.Render(Viewport);

        Assert.Contains("https://remembered.test/boardoil/", PlainText(frame.Canvas));
        Assert.Equal(13, frame.Cursor!.Value.Y);
    }

    [Fact]
    public void Login_KeepsFocusOnAnInvalidServerAndShowsGuidance()
    {
        var screen = new LoginApplication(initialServer: null, new TerminalRuntime());
        Type(screen, "not a url");

        var update = screen.HandleKey(Key('\r', ConsoleKey.Enter), Viewport);
        var frame = screen.Render(Viewport);

        Assert.False(update.IsComplete);
        Assert.Equal(10, frame.Cursor!.Value.Y);
        Assert.Contains("absolute HTTP or HTTPS", PlainText(frame.Canvas));
    }

    private static void Type(LoginApplication screen, string value)
    {
        foreach (var character in value)
        {
            screen.HandleKey(Key(character, ConsoleKey.A), Viewport);
        }
    }

    private static ConsoleKeyInfo Key(char keyChar, ConsoleKey key) =>
        new(keyChar, key, false, false, false);

    private static string PlainText(TerminalCanvas canvas) =>
        string.Join('\n', Enumerable.Range(0, canvas.Height)
            .Select(y => string.Concat(Enumerable.Range(0, canvas.Width)
                .Select(x => canvas.CellAt(x, y))
                .Where(cell => !cell.Continuation)
                .Select(cell => cell.Grapheme))));
}
