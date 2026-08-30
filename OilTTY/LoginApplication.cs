using System.Globalization;

internal sealed record LoginCredentials(Uri Server, string UserName, string Password);

internal sealed class LoginApplication(Uri? initialServer, TerminalRuntime terminal) : ITerminalScreen<LoginCredentials>
{
    private const int StandardServerLabelRow = 9;
    private const int StandardServerFieldRow = 10;
    private const int StandardUserNameLabelRow = 12;
    private const int StandardUserNameFieldRow = 13;
    private const int StandardPasswordLabelRow = 15;
    private const int StandardPasswordFieldRow = 16;
    private const int StandardMessageRow = 18;
    private const int CompactServerFieldRow = 7;
    private const int CompactUserNameFieldRow = 8;
    private const int CompactPasswordFieldRow = 9;
    private const int CompactMessageRow = 10;

    private readonly TerminalRuntime _terminal = terminal;
    private string _server = initialServer?.AbsoluteUri ?? string.Empty;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string? _message;
    private LoginField _activeField = initialServer is null ? LoginField.Server : LoginField.UserName;

    public async Task<LoginCredentials> PromptAsync(string? initialUserName, string? message)
    {
        if (!string.IsNullOrWhiteSpace(initialUserName))
        {
            _userName = initialUserName;
        }

        _password = string.Empty;
        _message = message;
        _activeField = string.IsNullOrWhiteSpace(_server)
            ? LoginField.Server
            : string.IsNullOrEmpty(_userName)
                ? LoginField.UserName
                : LoginField.Password;
        return await _terminal.RunAsync(this);
    }

    public TerminalFrame Render(TerminalViewport viewport)
    {
        var width = viewport.Width;
        var height = viewport.Height;
        var canvas = new TerminalCanvas(width, height, BoardStyles.TextStrong, BoardStyles.RootBackground);
        DrawFrame(canvas);
        BoardOilBranding.Draw(canvas);

        var compact = height < 21;
        var fieldWidth = Math.Min(width - 8, 52);
        var fieldX = (width - fieldWidth) / 2;
        int cursorY;
        if (compact)
        {
            DrawField(
                canvas,
                fieldX,
                CompactServerFieldRow,
                fieldWidth,
                "Server",
                _server,
                _activeField == LoginField.Server,
                compact: true);
            DrawField(
                canvas,
                fieldX,
                CompactUserNameFieldRow,
                fieldWidth,
                "Username",
                _userName,
                _activeField == LoginField.UserName,
                compact: true);
            DrawField(
                canvas,
                fieldX,
                CompactPasswordFieldRow,
                fieldWidth,
                "Password",
                PasswordMask(),
                _activeField == LoginField.Password,
                compact: true);
            if (height > CompactMessageRow + 2)
            {
                DrawMessage(canvas, fieldX, CompactMessageRow, fieldWidth, _message);
            }

            cursorY = _activeField switch
            {
                LoginField.Server => CompactServerFieldRow,
                LoginField.UserName => CompactUserNameFieldRow,
                _ => CompactPasswordFieldRow
            };
        }
        else
        {
            canvas.Put(fieldX, 8, "SIGN IN", BoardStyles.TextStrong, bold: true);
            canvas.Put(fieldX, StandardServerLabelRow, "Server", BoardStyles.TextMuted);
            DrawField(
                canvas,
                fieldX,
                StandardServerFieldRow,
                fieldWidth,
                "Server",
                _server,
                _activeField == LoginField.Server,
                compact: false);
            canvas.Put(fieldX, StandardUserNameLabelRow, "Username", BoardStyles.TextMuted);
            DrawField(
                canvas,
                fieldX,
                StandardUserNameFieldRow,
                fieldWidth,
                "Username",
                _userName,
                _activeField == LoginField.UserName,
                compact: false);
            canvas.Put(fieldX, StandardPasswordLabelRow, "Password", BoardStyles.TextMuted);
            DrawField(
                canvas,
                fieldX,
                StandardPasswordFieldRow,
                fieldWidth,
                "Password",
                PasswordMask(),
                _activeField == LoginField.Password,
                compact: false);
            DrawMessage(canvas, fieldX, StandardMessageRow, fieldWidth, _message);
            cursorY = _activeField switch
            {
                LoginField.Server => StandardServerFieldRow,
                LoginField.UserName => StandardUserNameFieldRow,
                _ => StandardPasswordFieldRow
            };
        }

        var activeLabel = _activeField switch
        {
            LoginField.Server => "Server",
            LoginField.UserName => "Username",
            _ => "Password"
        };
        var activeValue = _activeField switch
        {
            LoginField.Server => _server,
            LoginField.UserName => _userName,
            _ => PasswordMask()
        };
        var cursorX = FieldCursorX(fieldX, fieldWidth, activeLabel, activeValue, compact);
        return new TerminalFrame(canvas, new TerminalCursor(cursorX, cursorY));
    }

    public ScreenUpdate<LoginCredentials> HandleKey(ConsoleKeyInfo key, TerminalViewport viewport)
    {
        if (key.Key == ConsoleKey.Escape
            || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
        {
            throw new OperationCanceledException();
        }

        if (key.Key is ConsoleKey.Tab or ConsoleKey.UpArrow or ConsoleKey.DownArrow)
        {
            var backwards = key.Key == ConsoleKey.UpArrow
                || key.Key == ConsoleKey.Tab && key.Modifiers.HasFlag(ConsoleModifiers.Shift);
            _activeField = MoveField(_activeField, backwards ? -1 : 1);
            return ScreenUpdate<LoginCredentials>.Continue();
        }

        if (key.Key == ConsoleKey.Enter)
        {
            if (_activeField == LoginField.Server)
            {
                if (TryGetServer(out var server))
                {
                    _server = server.AbsoluteUri;
                    _message = null;
                    _activeField = LoginField.UserName;
                    return ScreenUpdate<LoginCredentials>.Continue();
                }

                _message = "Enter an absolute HTTP or HTTPS server URL.";
                return ScreenUpdate<LoginCredentials>.Continue();
            }

            if (_activeField == LoginField.UserName)
            {
                if (!string.IsNullOrWhiteSpace(_userName))
                {
                    _activeField = LoginField.Password;
                    return ScreenUpdate<LoginCredentials>.Continue();
                }

                return ScreenUpdate<LoginCredentials>.Continue(redraw: false);
            }

            if (TryGetServer(out var loginServer)
                && !string.IsNullOrWhiteSpace(_userName)
                && !string.IsNullOrEmpty(_password))
            {
                return ScreenUpdate<LoginCredentials>.Complete(
                    new LoginCredentials(loginServer, _userName.Trim(), _password));
            }

            return ScreenUpdate<LoginCredentials>.Continue(redraw: false);
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (_activeField == LoginField.Server)
            {
                _server = RemoveLastTextElement(_server);
            }
            else if (_activeField == LoginField.UserName)
            {
                _userName = RemoveLastTextElement(_userName);
            }
            else
            {
                _password = RemoveLastTextElement(_password);
            }

            return ScreenUpdate<LoginCredentials>.Continue();
        }

        if (!char.IsControl(key.KeyChar))
        {
            _message = null;
            if (_activeField == LoginField.Server)
            {
                _server += key.KeyChar;
            }
            else if (_activeField == LoginField.UserName)
            {
                _userName += key.KeyChar;
            }
            else
            {
                _password += key.KeyChar;
            }

            return ScreenUpdate<LoginCredentials>.Continue();
        }

        return ScreenUpdate<LoginCredentials>.Continue(redraw: false);
    }

    private void DrawFrame(TerminalCanvas canvas)
    {
        canvas.Fill(0, 0, canvas.Width, canvas.Height, BoardStyles.RootBackground);
        canvas.HorizontalLine(0, 1, canvas.Width, "─", BoardStyles.BorderSoft);
        canvas.HorizontalLine(0, canvas.Height - 2, canvas.Width, "─", BoardStyles.BorderSoft);
        canvas.Put(2, 0, "◆", BoardStyles.Selection, bold: true);
        canvas.Put(5, 0, "Sign in", BoardStyles.TextStrong, bold: true);

        canvas.Put(2, canvas.Height - 1, "tab", BoardStyles.Selection, bold: true);
        canvas.Put(6, canvas.Height - 1, "field", BoardStyles.TextMuted);
        canvas.Put(13, canvas.Height - 1, "enter", BoardStyles.Selection, bold: true);
        canvas.Put(19, canvas.Height - 1, "sign in", BoardStyles.TextMuted);
        canvas.Put(28, canvas.Height - 1, "esc", BoardStyles.Selection, bold: true);
        canvas.Put(32, canvas.Height - 1, "quit", BoardStyles.TextMuted);
    }

    private static void DrawField(
        TerminalCanvas canvas,
        int x,
        int y,
        int width,
        string label,
        string value,
        bool active,
        bool compact)
    {
        var background = active ? BoardStyles.InputActiveBackground : BoardStyles.PanelBackground;
        canvas.Fill(x, y, width, 1, background);
        canvas.Put(x, y, active ? "▌" : " ", BoardStyles.Selection, background, bold: active);
        var prefix = compact ? $"{label}: " : string.Empty;
        var content = string.IsNullOrEmpty(value) ? prefix : prefix + value;
        var foreground = string.IsNullOrEmpty(value) ? BoardStyles.TextMuted : BoardStyles.TextStrong;
        canvas.Put(
            x + 2,
            y,
            UnicodeDisplay.Truncate(content, width - 4),
            foreground,
            background,
            bold: active,
            maxWidth: width - 4);
    }

    private static void DrawMessage(
        TerminalCanvas canvas,
        int x,
        int y,
        int width,
        string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            canvas.Put(x, y, UnicodeDisplay.Truncate(message, width), BoardStyles.Danger, maxWidth: width);
        }
    }

    private string PasswordMask() =>
        new('•', UnicodeDisplay.Graphemes(_password).Count());

    private static int FieldCursorX(
        int fieldX,
        int fieldWidth,
        string label,
        string value,
        bool compact)
    {
        var content = compact ? $"{label}: {value}" : value;
        var visibleWidth = Math.Min(UnicodeDisplay.TextWidth(content), fieldWidth - 4);
        return fieldX + 2 + visibleWidth;
    }

    private static string RemoveLastTextElement(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var elementStarts = StringInfo.ParseCombiningCharacters(value);
        return value[..elementStarts[^1]];
    }

    private bool TryGetServer(out Uri server)
    {
        try
        {
            server = AppOptions.ParseServer(_server.Trim());
            return true;
        }
        catch (ArgumentException)
        {
            server = null!;
            return false;
        }
    }

    private static LoginField MoveField(LoginField field, int delta)
    {
        const int fieldCount = 3;
        var index = ((int)field + delta + fieldCount) % fieldCount;
        return (LoginField)index;
    }

    private enum LoginField
    {
        Server,
        UserName,
        Password
    }
}
