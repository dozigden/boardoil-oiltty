internal enum ImageSettingsCommand { Cancel, Save, Quit }

internal sealed class ImageSettingsScreen : ITerminalScreen<ImageSettingsCommand>
{
    private static readonly (ImageGlyphSets Set, string Label, string Samples, string Description)[] Choices =
    [
        (ImageGlyphSets.Quadrants, "Quadrants", "▞▘▝▖▌▛", "Sharper vertical and diagonal edges."),
        (ImageGlyphSets.Eighths, "Eighths", "▁▂▃▅▆▇", "Finer horizontal edges."),
        (ImageGlyphSets.Sextants, "Sextants", "🬀🬂🬆🬉🬖🬝", "More shapes. Needs terminal support."),
        (ImageGlyphSets.VerticalEighths, "Vertical eighths", "▏▎▍▌▋▊▉", "Finer vertical edges."),
        (ImageGlyphSets.Diagonals, "Diagonals", "🭀🭅🭊🭐", "Diagonal blocks. Needs terminal support.")
    ];
    private readonly CardDescriptionThumbnail _preview;
    private readonly bool _cardPreview;
    private int _selected;
    private string? _error;

    public ImageSettingsScreen(ImageGlyphSets glyphSets, CardDescriptionThumbnail? preview = null)
    {
        GlyphSets = glyphSets;
        _preview = preview ?? ImageSettingsPreview.Create();
        _cardPreview = preview is not null;
    }

    public ImageGlyphSets GlyphSets { get; private set; }

    public TerminalFrame Render(TerminalViewport viewport)
    {
        var width = Math.Max(40, viewport.Width);
        var height = Math.Max(12, viewport.Height);
        var canvas = new TerminalCanvas(width, height, BoardStyles.TextStrong, BoardStyles.RootBackground);
        ScreenChromeRenderer.DrawTopRow(canvas, "Image settings", "Local preference");
        canvas.HorizontalLine(0, 1, width, "─", BoardStyles.BorderSoft);
        canvas.HorizontalLine(0, height - 2, width, "─", BoardStyles.BorderSoft);
        var compact = height < 22;
        var leftWidth = Math.Min(34, (width - 6) / 2);
        var previewX = leftWidth + 5;
        var previewWidth = width - previewX - 2;
        canvas.Put(2, 2, "CHARACTER SETS", BoardStyles.TextMuted, bold: true, maxWidth: leftWidth);
        canvas.Put(previewX, 2, _cardPreview ? "CARD PREVIEW" : "SAMPLE PREVIEW",
            BoardStyles.TextMuted, bold: true, maxWidth: previewWidth);
        for (var index = 0; index < Choices.Length; index++)
        {
            var row = compact ? 3 + index : 4 + index * 2;
            var enabled = GlyphSets.HasFlag(Choices[index].Set);
            var label = Choices[index].Set == ImageGlyphSets.VerticalEighths && leftWidth < 24
                ? "V-eighths" : Choices[index].Label;
            DrawChoice(canvas, row, $"[{(enabled ? 'x' : ' ')}] {label}", leftWidth, index,
                Choices[index].Samples);
        }
        if (!compact)
            canvas.Put(2, 14, leftWidth < 30 ? "Half blocks ▀ on" : "Half blocks ▀ (always on)",
                BoardStyles.TextMuted, maxWidth: leftWidth);
        if (compact)
        {
            DrawChoice(canvas, 8, "Save", 7, Choices.Length);
            DrawChoice(canvas, 8, "Cancel", leftWidth - 8, Choices.Length + 1, x: 10);
        }
        else
        {
            DrawChoice(canvas, 16, "[ Save ]", leftWidth, Choices.Length);
            DrawChoice(canvas, 18, "[ Cancel ]", leftWidth, Choices.Length + 1);
        }
        if (!compact && height >= 24)
        {
            var help = _selected < Choices.Length ? Choices[_selected].Description
                : _selected == Choices.Length ? "Save locally and apply now." : "Keep your previous settings.";
            var lines = UnicodeDisplay.WrapText(help, leftWidth, leftWidth);
            for (var row = 0; row < Math.Min(height - 23, lines.Count); row++)
                canvas.Put(2, 20 + row, height == 24 ? UnicodeDisplay.Truncate(help, leftWidth) : lines[row],
                    BoardStyles.TextMuted, maxWidth: leftWidth);
        }

        var previewTop = 3;
        var previewRows = height - 7;
        canvas.Fill(previewX, previewTop, previewWidth, previewRows, BoardStyles.PanelBackground);
        var image = _preview.RenderLines(previewWidth, previewRows, BoardStyles.PanelBackground, GlyphSets);
        var imageTop = previewTop + Math.Max(0, (previewRows - image.Count) / 2);
        for (var row = 0; row < image.Count; row++)
        {
            var x = previewX;
            foreach (var span in image[row].Spans)
            {
                x += canvas.Put(x, imageTop + row, span.Text, span.Foreground, span.Background);
            }
        }
        var message = _error ?? (compact ? "Half blocks ▀ on; Save applies." : "Preview only until saved. Your choice is saved on this device.");
        canvas.Put(2, height - 3, UnicodeDisplay.Truncate(message, width - 4),
            _error is null ? BoardStyles.TextMuted : BoardStyles.Danger, maxWidth: width - 4);
        var footer = width < 72 ? "↑↓ move  space toggle  esc cancel"
            : "↑↓ choose  space toggle  enter select  ctrl+s save  esc cancel  ctrl+t theme";
        canvas.Put(2, height - 1, footer, BoardStyles.Selection, maxWidth: width - 4);
        return new TerminalFrame(canvas);
    }

    public ScreenUpdate<ImageSettingsCommand> HandleKey(ConsoleKeyInfo key, TerminalViewport viewport)
    {
        if (BoardStyles.TryToggleTheme(key)) return ScreenUpdate<ImageSettingsCommand>.Continue();
        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            return ScreenUpdate<ImageSettingsCommand>.Complete(ImageSettingsCommand.Quit);
        if (key.Key is ConsoleKey.Escape or ConsoleKey.Q or ConsoleKey.F2)
            return ScreenUpdate<ImageSettingsCommand>.Complete(ImageSettingsCommand.Cancel);
        if (key.Key == ConsoleKey.S && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            return ScreenUpdate<ImageSettingsCommand>.Complete(ImageSettingsCommand.Save);
        if (key.Key is ConsoleKey.J or ConsoleKey.DownArrow)
            _selected = Math.Min(Choices.Length + 1, _selected + 1);
        else if (key.Key is ConsoleKey.K or ConsoleKey.UpArrow)
            _selected = Math.Max(0, _selected - 1);
        else if (key.Key == ConsoleKey.Tab)
            _selected = (_selected + (key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? Choices.Length + 1 : 1)) % (Choices.Length + 2);
        else if (key.Key is ConsoleKey.Spacebar or ConsoleKey.Enter)
        {
            if (_selected >= Choices.Length)
                return ScreenUpdate<ImageSettingsCommand>.Complete(
                    _selected == Choices.Length ? ImageSettingsCommand.Save : ImageSettingsCommand.Cancel);
            GlyphSets ^= Choices[_selected].Set;
            _error = null;
        }
        else return ScreenUpdate<ImageSettingsCommand>.Continue(redraw: false);
        return ScreenUpdate<ImageSettingsCommand>.Continue();
    }

    public async Task<bool> SaveAsync(SettingsStore store, CancellationToken cancellationToken = default)
    {
        try
        {
            await store.SaveImageGlyphsAsync(GlyphSets, cancellationToken);
            _error = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _error = TerminalText.NeutraliseControls($"Save failed: {exception.Message}");
            return false;
        }
    }

    private void DrawChoice(TerminalCanvas canvas, int row, string label, int width, int index,
        string? samples = null, int x = 2)
    {
        var selected = _selected == index;
        var background = selected ? BoardStyles.InputActiveBackground : BoardStyles.RootBackground;
        canvas.Fill(x, row, width, 1, background);
        canvas.Put(x, row, selected ? "▌" : " ", BoardStyles.Selection, background);
        canvas.Put(x + 2, row, label, selected ? BoardStyles.TextStrong : BoardStyles.TextMuted,
            background, bold: selected, maxWidth: width - 2);
        if (samples is not null)
        {
            var sampleX = x + 2 + UnicodeDisplay.TextWidth(label) + 1;
            canvas.Put(sampleX, row, samples, BoardStyles.TextStrong, background,
                maxWidth: Math.Max(0, x + width - sampleX));
        }
    }
}

internal static class ImageSettingsPreview
{
    public static CardDescriptionThumbnail Create()
    {
        // A small built-in colour study exercises curves, diagonals, and fine lines.
        // No network or external image is needed to compare character support.
        const int width = 160;
        const int height = 96;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var colour = new Rgb((byte)(25 + y / 2), (byte)(38 + x / 4), (byte)(75 + y));
            var dx = x - 112;
            var dy = y - 29;
            if (dx * dx + dy * dy < 18 * 18) colour = new Rgb(255, 199, 93);
            if (y > 76 - x * 0.4) colour = new Rgb(87, 74, 144);
            if (y > 26 + x * 0.45) colour = new Rgb(60, 150, 162);
            if (x is > 12 and < 51 && y is > 12 and < 41)
                colour = y % 7 < 2 ? new Rgb(222, 237, 244) : new Rgb(42, 58, 87);
            if (x is > 96 and < 147 && y is > 73 and < 85)
                colour = x % 9 < 2 ? new Rgb(244, 181, 187) : new Rgb(151, 63, 103);
            var offset = (y * width + x) * 4;
            pixels[offset] = colour.Red;
            pixels[offset + 1] = colour.Green;
            pixels[offset + 2] = colour.Blue;
            pixels[offset + 3] = 255;
        }
        return new CardDescriptionThumbnail(width, height, pixels);
    }
}
