using System.Globalization;

internal sealed record TextEditorVisualLine(
    int LogicalLineIndex,
    int StartCharacterIndex,
    int EndCharacterIndex,
    string Text);

internal sealed record TextEditorVisualLayout(
    IReadOnlyList<TextEditorVisualLine> Lines,
    int CursorRow,
    int CursorColumn);

internal sealed class MultilineTextEditor
{
    private readonly List<string> _lines;
    private readonly bool _allowNewLines;
    private readonly int? _maximumLength;
    private int _lineIndex;
    private int _characterIndex;
    private int? _preferredDisplayColumn;

    public MultilineTextEditor(
        string text,
        bool allowNewLines = true,
        int? maximumLength = null,
        bool cursorAtEnd = false)
    {
        _allowNewLines = allowNewLines;
        _maximumLength = maximumLength;
        _lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
        if (_lines.Count == 0)
        {
            _lines.Add(string.Empty);
        }

        if (cursorAtEnd)
        {
            _lineIndex = _lines.Count - 1;
            _characterIndex = _lines[^1].Length;
        }
    }

    public string Text => string.Join('\n', _lines);

    public int LineIndex => _lineIndex;

    public int CharacterIndex => _characterIndex;

    public bool HandleKey(ConsoleKeyInfo key, int displayWidth)
    {
        displayWidth = Math.Max(1, displayWidth);
        if (key.Key == ConsoleKey.LeftArrow)
        {
            MoveLeft();
            return true;
        }

        if (key.Key == ConsoleKey.RightArrow)
        {
            MoveRight();
            return true;
        }

        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
        {
            MoveVertical(key.Key == ConsoleKey.UpArrow ? -1 : 1, displayWidth);
            return true;
        }

        if (key.Key == ConsoleKey.Home)
        {
            MoveToVisualEdge(displayWidth, end: false);
            return true;
        }

        if (key.Key == ConsoleKey.End)
        {
            MoveToVisualEdge(displayWidth, end: true);
            return true;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            Backspace();
            return true;
        }

        if (key.Key == ConsoleKey.Delete)
        {
            Delete();
            return true;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            if (!_allowNewLines)
            {
                return false;
            }

            SplitLine();
            return true;
        }

        if (!char.IsControl(key.KeyChar) && !key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            Insert(key.KeyChar.ToString());
            return true;
        }

        return false;
    }

    public TextEditorVisualLayout CreateVisualLayout(int displayWidth)
    {
        displayWidth = Math.Max(1, displayWidth);
        var visualLines = new List<TextEditorVisualLine>();
        for (var logicalLineIndex = 0; logicalLineIndex < _lines.Count; logicalLineIndex++)
        {
            AddVisualLines(visualLines, logicalLineIndex, _lines[logicalLineIndex], displayWidth);
        }

        var cursorRow = 0;
        var cursorColumn = 0;
        for (var index = 0; index < visualLines.Count; index++)
        {
            var visualLine = visualLines[index];
            if (visualLine.LogicalLineIndex != _lineIndex)
            {
                continue;
            }

            var isLastForLogicalLine = index + 1 == visualLines.Count
                || visualLines[index + 1].LogicalLineIndex != _lineIndex;
            if (_characterIndex < visualLine.EndCharacterIndex
                || (isLastForLogicalLine && _characterIndex <= visualLine.EndCharacterIndex))
            {
                cursorRow = index;
                cursorColumn = UnicodeDisplay.TextWidth(
                    _lines[_lineIndex][visualLine.StartCharacterIndex.._characterIndex]);
                break;
            }
        }

        return new TextEditorVisualLayout(visualLines, cursorRow, cursorColumn);
    }

    public void MoveToVisualRow(int row, int displayWidth)
    {
        var layout = CreateVisualLayout(displayWidth);
        var target = layout.Lines[Math.Clamp(row, 0, layout.Lines.Count - 1)];
        _lineIndex = target.LogicalLineIndex;
        _characterIndex = target.StartCharacterIndex;
        _preferredDisplayColumn = null;
    }

    private void MoveLeft()
    {
        if (_characterIndex > 0)
        {
            _characterIndex = PreviousBoundary(_lines[_lineIndex], _characterIndex);
        }
        else if (_lineIndex > 0)
        {
            _lineIndex--;
            _characterIndex = _lines[_lineIndex].Length;
        }

        _preferredDisplayColumn = null;
    }

    private void MoveRight()
    {
        var line = _lines[_lineIndex];
        if (_characterIndex < line.Length)
        {
            _characterIndex = NextBoundary(line, _characterIndex);
        }
        else if (_lineIndex + 1 < _lines.Count)
        {
            _lineIndex++;
            _characterIndex = 0;
        }

        _preferredDisplayColumn = null;
    }

    private void MoveVertical(int delta, int displayWidth)
    {
        var layout = CreateVisualLayout(displayWidth);
        var targetRow = Math.Clamp(layout.CursorRow + delta, 0, layout.Lines.Count - 1);
        if (targetRow == layout.CursorRow)
        {
            return;
        }

        _preferredDisplayColumn ??= layout.CursorColumn;
        var target = layout.Lines[targetRow];
        _lineIndex = target.LogicalLineIndex;
        _characterIndex = CharacterIndexAtDisplayColumn(
            _lines[_lineIndex],
            target.StartCharacterIndex,
            target.EndCharacterIndex,
            _preferredDisplayColumn.Value);
    }

    private void MoveToVisualEdge(int displayWidth, bool end)
    {
        var layout = CreateVisualLayout(displayWidth);
        var line = layout.Lines[layout.CursorRow];
        _lineIndex = line.LogicalLineIndex;
        _characterIndex = end ? line.EndCharacterIndex : line.StartCharacterIndex;
        _preferredDisplayColumn = null;
    }

    private void Backspace()
    {
        if (_characterIndex > 0)
        {
            var line = _lines[_lineIndex];
            var start = PreviousBoundary(line, _characterIndex);
            _lines[_lineIndex] = line.Remove(start, _characterIndex - start);
            _characterIndex = start;
        }
        else if (_lineIndex > 0)
        {
            var previousLength = _lines[_lineIndex - 1].Length;
            _lines[_lineIndex - 1] += _lines[_lineIndex];
            _lines.RemoveAt(_lineIndex);
            _lineIndex--;
            _characterIndex = previousLength;
        }

        _preferredDisplayColumn = null;
    }

    private void Delete()
    {
        var line = _lines[_lineIndex];
        if (_characterIndex < line.Length)
        {
            var end = NextBoundary(line, _characterIndex);
            _lines[_lineIndex] = line.Remove(_characterIndex, end - _characterIndex);
        }
        else if (_lineIndex + 1 < _lines.Count)
        {
            _lines[_lineIndex] += _lines[_lineIndex + 1];
            _lines.RemoveAt(_lineIndex + 1);
        }

        _preferredDisplayColumn = null;
    }

    private void SplitLine()
    {
        var line = _lines[_lineIndex];
        var remainder = line[_characterIndex..];
        _lines[_lineIndex] = line[.._characterIndex];
        _lines.Insert(_lineIndex + 1, remainder);
        _lineIndex++;
        _characterIndex = 0;
        _preferredDisplayColumn = null;
    }

    private void Insert(string value)
    {
        if (_maximumLength is int maximumLength && Text.Length + value.Length > maximumLength)
        {
            return;
        }

        _lines[_lineIndex] = _lines[_lineIndex].Insert(_characterIndex, value);
        _characterIndex += value.Length;
        _preferredDisplayColumn = null;
    }

    private static void AddVisualLines(
        List<TextEditorVisualLine> result,
        int logicalLineIndex,
        string line,
        int displayWidth)
    {
        if (line.Length == 0)
        {
            result.Add(new TextEditorVisualLine(logicalLineIndex, 0, 0, string.Empty));
            return;
        }

        var starts = StringInfo.ParseCombiningCharacters(line);
        var segmentStart = starts[0];
        var segmentWidth = 0;
        for (var index = 0; index < starts.Length; index++)
        {
            var start = starts[index];
            var end = index + 1 < starts.Length ? starts[index + 1] : line.Length;
            var graphemeWidth = UnicodeDisplay.Width(line[start..end]);
            if (segmentWidth > 0 && segmentWidth + graphemeWidth > displayWidth)
            {
                result.Add(new TextEditorVisualLine(
                    logicalLineIndex,
                    segmentStart,
                    start,
                    line[segmentStart..start]));
                segmentStart = start;
                segmentWidth = 0;
            }

            segmentWidth += graphemeWidth;
        }

        result.Add(new TextEditorVisualLine(
            logicalLineIndex,
            segmentStart,
            line.Length,
            line[segmentStart..]));
    }

    private static int CharacterIndexAtDisplayColumn(
        string line,
        int start,
        int end,
        int displayColumn)
    {
        var used = 0;
        var index = start;
        while (index < end)
        {
            var next = NextBoundary(line, index);
            var width = UnicodeDisplay.Width(line[index..next]);
            if (used + width > displayColumn)
            {
                break;
            }

            used += width;
            index = next;
        }

        return index;
    }

    private static int PreviousBoundary(string text, int characterIndex)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var previous = 0;
        foreach (var boundary in boundaries)
        {
            if (boundary >= characterIndex)
            {
                break;
            }

            previous = boundary;
        }

        return previous;
    }

    private static int NextBoundary(string text, int characterIndex)
    {
        foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
        {
            if (boundary > characterIndex)
            {
                return boundary;
            }
        }

        return text.Length;
    }
}
