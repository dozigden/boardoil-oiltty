internal static class ScreenChromeRenderer
{
    private const string AppName = "OilTTY";

    public static void DrawTopRow(TerminalCanvas canvas, string screenTitle, string status)
    {
        var appNameWidth = UnicodeDisplay.TextWidth(AppName);
        var appNameX = (canvas.Width - appNameWidth) / 2;

        canvas.Put(2, 0, "◆", BoardStyles.Selection, bold: true);

        const int titleX = 5;
        var titleWidth = Math.Max(0, appNameX - titleX - 2);
        if (titleWidth > 0)
        {
            canvas.Put(
                titleX,
                0,
                UnicodeDisplay.Truncate(screenTitle, titleWidth),
                BoardStyles.TextStrong,
                bold: true,
                maxWidth: titleWidth);
        }

        canvas.Put(appNameX, 0, AppName, BoardStyles.TextStrong, bold: true);

        var statusStart = appNameX + appNameWidth + 2;
        var statusWidth = Math.Max(0, canvas.Width - statusStart - 2);
        if (statusWidth <= 0)
        {
            return;
        }

        var statusText = UnicodeDisplay.Truncate(status, statusWidth);
        var statusX = canvas.Width - UnicodeDisplay.TextWidth(statusText) - 2;
        canvas.Put(statusX, 0, statusText, BoardStyles.Connected, bold: true);
    }
}
