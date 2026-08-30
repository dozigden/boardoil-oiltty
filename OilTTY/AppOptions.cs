internal sealed record AppOptions(
    Uri? Server,
    int BoardId,
    bool Once,
    bool NoAlternateScreen,
    bool NoSession,
    bool Logout)
{
    public static AppOptions Parse(string[] arguments)
    {
        var serverValue = Environment.GetEnvironmentVariable("BOARDOIL_URL");
        var boardValue = Environment.GetEnvironmentVariable("BOARDOIL_BOARD_ID");
        var boardId = int.TryParse(boardValue, out var configuredBoardId) ? configuredBoardId : 1;
        var once = false;
        var noAlternateScreen = false;
        var noSession = false;
        var logout = false;

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--server":
                    serverValue = ReadValue(arguments, ref index, argument);
                    break;
                case "--board":
                    var rawBoardId = ReadValue(arguments, ref index, argument);
                    if (!int.TryParse(rawBoardId, out boardId) || boardId <= 0)
                    {
                        throw new ArgumentException("--board must be a positive integer.");
                    }

                    break;
                case "--once":
                    once = true;
                    break;
                case "--no-alt-screen":
                    noAlternateScreen = true;
                    break;
                case "--no-session":
                    noSession = true;
                    break;
                case "--logout":
                    logout = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        var server = string.IsNullOrWhiteSpace(serverValue) ? null : ParseServer(serverValue);
        if (logout && noSession)
        {
            throw new ArgumentException("--logout cannot be combined with --no-session.");
        }

        return new AppOptions(server, boardId, once, noAlternateScreen, noSession, logout);
    }

    private static string ReadValue(string[] arguments, ref int index, string option)
    {
        index++;
        if (index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return arguments[index];
    }

    internal static Uri ParseServer(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("BoardOil server must be an absolute HTTP or HTTPS URL.");
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/"
        };
        return builder.Uri;
    }
}
