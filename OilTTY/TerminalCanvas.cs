using System.Globalization;
using System.Text;

internal readonly record struct Rgb(byte Red, byte Green, byte Blue);

internal readonly record struct TerminalCell(
    string Grapheme,
    Rgb Foreground,
    Rgb Background,
    bool Bold,
    bool Continuation)
{
    public static TerminalCell Empty(Rgb foreground, Rgb background) =>
        new(" ", foreground, background, false, false);
}

internal sealed class TerminalCanvas
{
    private readonly TerminalCell[,] _cells;
    private readonly Rgb _defaultForeground;
    private readonly Rgb _defaultBackground;

    public TerminalCanvas(int width, int height, Rgb defaultForeground, Rgb defaultBackground)
    {
        Width = width;
        Height = height;
        _defaultForeground = defaultForeground;
        _defaultBackground = defaultBackground;
        _cells = new TerminalCell[height, width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                _cells[y, x] = TerminalCell.Empty(defaultForeground, defaultBackground);
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public TerminalCell CellAt(int x, int y) => _cells[y, x];

    public Rgb BackgroundAt(int x, int y) => _cells[y, x].Background;

    public void SetCell(int x, int y, string grapheme, Rgb foreground, Rgb background, bool bold = false)
    {
        ClearWideGraphemeAt(x, y);
        _cells[y, x] = new TerminalCell(grapheme, foreground, background, bold, false);
    }

    public void Fill(int x, int y, int width, int height, Rgb background)
    {
        for (var py = Math.Max(0, y); py < Math.Min(Height, y + height); py++)
        {
            for (var px = Math.Max(0, x); px < Math.Min(Width, x + width); px++)
            {
                ClearWideGraphemeAt(px, py);
                _cells[py, px] = TerminalCell.Empty(_defaultForeground, background);
            }
        }
    }

    public void FillGradient(int x, int y, int width, int height, SurfaceStyle style)
    {
        for (var py = Math.Max(0, y); py < Math.Min(Height, y + height); py++)
        {
            for (var px = Math.Max(0, x); px < Math.Min(Width, x + width); px++)
            {
                var background = style.BackgroundAt(px - x, width);
                ClearWideGraphemeAt(px, py);
                _cells[py, px] = TerminalCell.Empty(style.Foreground, background);
            }
        }
    }

    public int Put(
        int x,
        int y,
        string text,
        Rgb? foreground = null,
        Rgb? background = null,
        bool bold = false,
        int? maxWidth = null)
    {
        if (y < 0 || y >= Height)
        {
            return x;
        }

        var start = x;
        var limit = maxWidth is null ? Width : Math.Min(Width, x + maxWidth.Value);
        foreach (var grapheme in UnicodeDisplay.Graphemes(text))
        {
            var graphemeWidth = UnicodeDisplay.Width(grapheme);
            if (x < 0)
            {
                x += graphemeWidth;
                continue;
            }

            if (x + graphemeWidth > limit)
            {
                break;
            }

            for (var occupiedX = x; occupiedX < x + graphemeWidth; occupiedX++)
            {
                ClearWideGraphemeAt(occupiedX, y);
            }

            var cellBackground = background ?? _cells[y, x].Background;
            var cellForeground = foreground ?? _defaultForeground;
            _cells[y, x] = new TerminalCell(grapheme, cellForeground, cellBackground, bold, false);
            if (graphemeWidth == 2)
            {
                _cells[y, x + 1] = new TerminalCell(string.Empty, cellForeground, cellBackground, bold, true);
            }

            x += graphemeWidth;
        }

        return x - start;
    }

    public void HorizontalLine(int x, int y, int width, string grapheme, Rgb foreground)
    {
        for (var px = x; px < x + width; px++)
        {
            Put(px, y, grapheme, foreground);
        }
    }

    public string Render()
    {
        var output = new StringBuilder();
        for (var y = 0; y < Height; y++)
        {
            CellStyle? activeStyle = null;
            for (var x = 0; x < Width; x++)
            {
                var cell = _cells[y, x];
                if (cell.Continuation)
                {
                    continue;
                }

                var style = new CellStyle(cell.Foreground, cell.Background, cell.Bold);
                if (style != activeStyle)
                {
                    var weight = cell.Bold ? "1" : "22";
                    output.Append("\e[")
                        .Append(weight)
                        .Append(";38;2;")
                        .Append(cell.Foreground.Red)
                        .Append(';')
                        .Append(cell.Foreground.Green)
                        .Append(';')
                        .Append(cell.Foreground.Blue)
                        .Append(";48;2;")
                        .Append(cell.Background.Red)
                        .Append(';')
                        .Append(cell.Background.Green)
                        .Append(';')
                        .Append(cell.Background.Blue)
                        .Append('m');
                    activeStyle = style;
                }

                output.Append(cell.Grapheme);
            }

            output.Append("\e[0m");
            if (y < Height - 1)
            {
                output.Append('\n');
            }
        }

        return output.ToString();
    }

    private void ClearWideGraphemeAt(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        var start = x;
        while (start > 0 && _cells[y, start].Continuation)
        {
            start--;
        }

        if (start == x
            && (x + 1 >= Width || !_cells[y, x + 1].Continuation))
        {
            return;
        }

        var end = start + 1;
        while (end < Width && _cells[y, end].Continuation)
        {
            end++;
        }

        for (var px = start; px < end; px++)
        {
            _cells[y, px] = TerminalCell.Empty(_defaultForeground, _cells[y, px].Background);
        }
    }

    private readonly record struct CellStyle(Rgb Foreground, Rgb Background, bool Bold);
}

internal static class UnicodeDisplay
{
    public static IEnumerable<string> Graphemes(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }

    public static int TextWidth(string text) => Graphemes(text).Sum(Width);

    public static string EmojiLabelPrefix(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji))
        {
            return string.Empty;
        }

        // A narrow colour glyph can paint into the next cell even though VTE
        // advances by one. Preserve one visibly empty cell before label text.
        var gap = Width(emoji) == 1 ? "  " : " ";
        return emoji + gap;
    }

    public static int Width(string grapheme)
    {
        if (string.IsNullOrEmpty(grapheme))
        {
            return 0;
        }

        Rune? firstRune = null;
        var regionalIndicatorCount = 0;
        foreach (var rune in grapheme.EnumerateRunes())
        {
            firstRune ??= rune;
            // VTE advances the cursor from the base character's East Asian width;
            // a variation selector changes its glyph, not its occupied cell count.
            if (rune.Value is 0x200D or 0x20E3)
            {
                return 2;
            }

            if (rune.Value is >= 0x1F1E6 and <= 0x1F1FF)
            {
                regionalIndicatorCount++;
            }
        }

        if (regionalIndicatorCount >= 2)
        {
            return 2;
        }

        return firstRune is not null && IsWide(firstRune.Value.Value) ? 2 : 1;
    }

    public static string Truncate(string text, int width)
    {
        if (TextWidth(text) <= width)
        {
            return text;
        }

        if (width <= 1)
        {
            return width == 1 ? "…" : string.Empty;
        }

        var output = new StringBuilder();
        var used = 0;
        foreach (var grapheme in Graphemes(text))
        {
            var graphemeWidth = Width(grapheme);
            if (used + graphemeWidth > width - 1)
            {
                break;
            }

            output.Append(grapheme);
            used += graphemeWidth;
        }

        return output.Append('…').ToString();
    }

    public static IReadOnlyList<string> WrapText(
        string text,
        int firstLineWidth,
        int continuationWidth)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [string.Empty];
        }

        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var lineWidth = lines.Count == 0 ? firstLineWidth : continuationWidth;
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (TextWidth(candidate) <= lineWidth)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
                current = string.Empty;
                lineWidth = continuationWidth;
            }

            var remaining = word;
            while (TextWidth(remaining) > lineWidth)
            {
                var (line, rest) = SplitAtWidth(remaining, lineWidth);
                lines.Add(line);
                remaining = rest;
                lineWidth = continuationWidth;
            }

            current = remaining;
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static (string Line, string Remainder) SplitAtWidth(string text, int width)
    {
        var graphemes = Graphemes(text).ToArray();
        var usedWidth = 0;
        var count = 0;
        while (count < graphemes.Length)
        {
            var graphemeWidth = Width(graphemes[count]);
            if (count > 0 && usedWidth + graphemeWidth > width)
            {
                break;
            }

            usedWidth += graphemeWidth;
            count++;
        }

        return (
            string.Concat(graphemes.Take(count)),
            string.Concat(graphemes.Skip(count)));
    }

    private static bool IsWide(int codePoint) =>
        codePoint is >= 0x1100 and <= 0x115F
        or >= 0x231A and <= 0x231B
        or 0x2329 or 0x232A
        or >= 0x23E9 and <= 0x23EC
        or 0x23F0 or 0x23F3
        or >= 0x25FD and <= 0x25FE
        or >= 0x2614 and <= 0x2615
        or >= 0x2648 and <= 0x2653
        or 0x267F or 0x2693 or 0x26A1
        or >= 0x26AA and <= 0x26AB
        or >= 0x26BD and <= 0x26BE
        or >= 0x26C4 and <= 0x26C5
        or 0x26CE or 0x26D4 or 0x26EA
        or >= 0x26F2 and <= 0x26F3
        or 0x26F5 or 0x26FA or 0x26FD or 0x2705
        or >= 0x270A and <= 0x270B
        or 0x2728 or 0x274C or 0x274E
        or >= 0x2753 and <= 0x2755
        or 0x2757
        or >= 0x2795 and <= 0x2797
        or 0x27B0 or 0x27BF
        or >= 0x2B1B and <= 0x2B1C
        or 0x2B50 or 0x2B55
        or >= 0x2E80 and <= 0xA4CF and not 0x303F
        or >= 0xAC00 and <= 0xD7A3
        or >= 0xF900 and <= 0xFAFF
        or >= 0xFE10 and <= 0xFE19
        or >= 0xFE30 and <= 0xFE6F
        or >= 0xFF00 and <= 0xFF60
        or >= 0xFFE0 and <= 0xFFE6
        or >= 0x1B000 and <= 0x1B2FF
        or 0x1F004 or 0x1F0CF or 0x1F18E
        or >= 0x1F191 and <= 0x1F19A
        or >= 0x1F200 and <= 0x1F202
        or >= 0x1F210 and <= 0x1F23B
        or >= 0x1F240 and <= 0x1F248
        or >= 0x1F250 and <= 0x1F251
        or >= 0x1F260 and <= 0x1F265
        or >= 0x1F300 and <= 0x1F320
        or >= 0x1F32D and <= 0x1F335
        or >= 0x1F337 and <= 0x1F37C
        or >= 0x1F37E and <= 0x1F393
        or >= 0x1F3A0 and <= 0x1F3CA
        or >= 0x1F3CF and <= 0x1F3D3
        or >= 0x1F3E0 and <= 0x1F3F0
        or 0x1F3F4
        or >= 0x1F3F8 and <= 0x1F43E
        or 0x1F440
        or >= 0x1F442 and <= 0x1F4FC
        or >= 0x1F4FF and <= 0x1F53D
        or >= 0x1F54B and <= 0x1F54E
        or >= 0x1F550 and <= 0x1F567
        or 0x1F57A
        or >= 0x1F595 and <= 0x1F596
        or 0x1F5A4
        or >= 0x1F5FB and <= 0x1F64F
        or >= 0x1F680 and <= 0x1F6C5
        or 0x1F6CC
        or >= 0x1F6D0 and <= 0x1F6D2
        or >= 0x1F6D5 and <= 0x1F6D7
        or >= 0x1F6DC and <= 0x1F6DF
        or >= 0x1F6EB and <= 0x1F6EC
        or >= 0x1F6F4 and <= 0x1F6FC
        or >= 0x1F7E0 and <= 0x1F7EB
        or 0x1F7F0
        or >= 0x1F90C and <= 0x1F93A
        or >= 0x1F93C and <= 0x1F945
        or >= 0x1F947 and <= 0x1F9FF
        or >= 0x1FA70 and <= 0x1FA7C
        or >= 0x1FA80 and <= 0x1FA88
        or >= 0x1FA90 and <= 0x1FABD
        or >= 0x1FABF and <= 0x1FAC5
        or >= 0x1FACE and <= 0x1FADB
        or >= 0x1FAE0 and <= 0x1FAE8
        or >= 0x1FAF0 and <= 0x1FAF8
        or >= 0x20000 and <= 0x3FFFD;
}
