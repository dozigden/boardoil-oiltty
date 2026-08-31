using System.Text;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

try
{
    var options = AppOptions.Parse(args);
    if (options.ShowHelp)
    {
        Console.WriteLine(AppOptions.HelpText);
        return 0;
    }

    BoardStyles.UseTheme(options.Theme);
    var serverStore = new ServerStore();
    options = options with { Server = options.Server ?? await serverStore.LoadAsync() };
    if (options.Logout)
    {
        Console.WriteLine(await BoardOilClient.LogoutStoredSessionAsync(options));
        return 0;
    }

    var interactive = !options.Once && !Console.IsInputRedirected && !Console.IsOutputRedirected;
    using var terminal = interactive ? new TerminalSession(options.NoAlternateScreen) : null;
    var terminalRuntime = interactive ? new TerminalRuntime() : null;
    var loginApplication = terminalRuntime is null
        ? null
        : new LoginApplication(options.Server, terminalRuntime);
    await using var client = await BoardOilClient.ConnectAsync(
        options,
        loginApplication is null ? null : loginApplication.PromptAsync,
        serverStore);
    var app = new TerminalApplication(client, options, terminalRuntime);
    return await app.RunAsync();
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"OilTTY: {TerminalText.NeutraliseControls(exception.Message)}");
    return 1;
}
