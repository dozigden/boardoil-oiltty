internal sealed record AppOptions(
    Uri? Server,
    int BoardId,
    bool Once,
    bool NoAlternateScreen,
    bool NoSession,
    bool Logout,
    OilTTYTheme Theme,
    bool ShowHelp)
{
    public const string HelpText = """
        Usage: OilTTY [options]

        Options:
          --server <url>         BoardOil server URL (BOARDOIL_URL)
          --board <id>           Initial board ID (BOARDOIL_BOARD_ID)
          --theme <dark|light>   Colour theme (OILTTY_THEME; default: dark)
          --logout               Delete the saved session
          --no-session           Do not load or save a session
          --no-alt-screen        Do not use the alternate terminal screen
          --once                 Render once and exit
          -h, --help             Show this help

        Authentication environment variables:
          BOARDOIL_API_TOKEN     API token for non-interactive authentication
          BOARDOIL_USERNAME      Username for non-interactive authentication
          BOARDOIL_PASSWORD      Password for non-interactive authentication
        """;

    public static AppOptions Parse(string[] arguments)
    {
        var serverValue = Environment.GetEnvironmentVariable("BOARDOIL_URL");
        var boardValue = Environment.GetEnvironmentVariable("BOARDOIL_BOARD_ID");
        var boardSource = "BOARDOIL_BOARD_ID";
        var themeValue = Environment.GetEnvironmentVariable("OILTTY_THEME");
        var once = false;
        var noAlternateScreen = false;
        var noSession = false;
        var logout = false;
        var showHelp = false;

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--server":
                    serverValue = ReadValue(arguments, ref index, argument);
                    break;
                case "--board":
                    boardValue = ReadValue(arguments, ref index, argument);
                    boardSource = argument;
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
                case "--theme":
                    themeValue = ReadValue(arguments, ref index, argument);
                    break;
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        if (showHelp)
        {
            return new AppOptions(
                null,
                1,
                false,
                false,
                false,
                false,
                OilTTYTheme.Dark,
                true);
        }

        var server = string.IsNullOrWhiteSpace(serverValue) ? null : ParseServer(serverValue);
        var boardId = ParseBoardId(boardValue, boardSource);
        if (logout && noSession)
        {
            throw new ArgumentException("--logout cannot be combined with --no-session.");
        }

        return new AppOptions(
            server,
            boardId,
            once,
            noAlternateScreen,
            noSession,
            logout,
            ParseTheme(themeValue),
            false);
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

    private static int ParseBoardId(string? value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }

        if (!int.TryParse(value, out var boardId) || boardId <= 0)
        {
            throw new ArgumentException($"{source} must be a positive integer.");
        }

        return boardId;
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

    internal static OilTTYTheme ParseTheme(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "dark" => OilTTYTheme.Dark,
            "light" => OilTTYTheme.Light,
            _ => throw new ArgumentException("Theme must be 'light' or 'dark'.")
        };
}
