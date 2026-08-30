using System.Globalization;

internal sealed record CardDetailSpan(
    string Text,
    Rgb Foreground,
    Rgb? Background = null,
    bool Bold = false,
    SurfaceStyle? Surface = null);

internal sealed record CardDetailLine(
    IReadOnlyList<CardDetailSpan> Spans,
    CardDetailField? Field = null)
{
    public static readonly CardDetailLine Empty = new([]);
}

internal sealed record CardDetailLayout(
    int Width,
    int Height,
    IReadOnlyList<string> TitleLines,
    int ContentTop,
    int ContentBottom,
    bool IsWide,
    int MainX,
    int MainWidth,
    int AsideX,
    int AsideWidth,
    int TitleTextWidth,
    int TitleWindowStart,
    int? TitleCursorRow,
    int? TitleCursorColumn,
    int DescriptionTextWidth,
    IReadOnlyList<CardDetailLine> DescriptionLines,
    IReadOnlyList<CardDetailLine> OptionsLines,
    int? DescriptionCursorRow,
    int? DescriptionCursorColumn,
    int TagsFirstRow,
    int TagsLastRow,
    int ColumnFirstRow,
    int ColumnLastRow,
    int CardTypeFirstRow,
    int CardTypeLastRow,
    int AssignedUserFirstRow,
    int AssignedUserLastRow,
    int SlickFirstRow,
    int SlickLastRow,
    int ExternalUrlFirstRow,
    int ExternalUrlLastRow,
    int? ExternalUrlCursorRow,
    int? ExternalUrlCursorColumn)
{
    public int PaneContentTop => ContentTop + 2;

    public int PaneViewportRows => Math.Max(0, ContentBottom - PaneContentTop);

    public int DescriptionMaxScroll => Math.Max(0, DescriptionLines.Count - PaneViewportRows);

    public int OptionsMaxScroll => Math.Max(0, OptionsLines.Count - PaneViewportRows);
}

internal sealed class CardDetailLayoutEngine
{
    public CardDetailLayout Create(
        BoardData data,
        BoardCard card,
        int requestedWidth,
        int requestedHeight,
        CardDetailField? editingField = null,
        MultilineTextEditor? editor = null,
        bool showTimestamps = true)
    {
        var width = Math.Max(40, requestedWidth);
        var height = Math.Max(12, requestedHeight);
        var titlePrefixWidth = string.IsNullOrWhiteSpace(card.CardTypeEmoji)
            ? 0
            : UnicodeDisplay.TextWidth(UnicodeDisplay.EmojiLabelPrefix(card.CardTypeEmoji));
        var maximumTitleRows = Math.Max(1, Math.Min(3, height - 8));
        var titleTextWidth = Math.Max(4, width - 6 - titlePrefixWidth);
        IReadOnlyList<string> titleLines;
        var titleWindowStart = 0;
        int? titleCursorRow = null;
        int? titleCursorColumn = null;
        if (editingField == CardDetailField.Title && editor is not null)
        {
            titleTextWidth = Math.Max(1, titleTextWidth - 1);
            var titleEditorLayout = editor.CreateVisualLayout(titleTextWidth);
            titleWindowStart = Math.Clamp(
                titleEditorLayout.CursorRow - maximumTitleRows + 1,
                0,
                Math.Max(0, titleEditorLayout.Lines.Count - maximumTitleRows));
            titleLines = titleEditorLayout.Lines
                .Skip(titleWindowStart)
                .Take(maximumTitleRows)
                .Select(line => line.Text)
                .ToArray();
            titleCursorRow = titleEditorLayout.CursorRow - titleWindowStart;
            titleCursorColumn = titleEditorLayout.CursorColumn;
        }
        else
        {
            titleLines = UnicodeDisplay.WrapText(
                card.Title,
                Math.Max(4, width - 6 - titlePrefixWidth),
                Math.Max(4, width - 6));
            titleLines = LimitLines(titleLines, maximumTitleRows, Math.Max(4, width - 6));
        }

        var contentTop = 3 + titleLines.Count;
        var contentBottom = height - 2;
        var isWide = true;
        var mainX = 2;
        var asideWidth = Math.Clamp(width / 3, 18, 42);
        var asideX = width - asideWidth - 2;
        var mainWidth = asideX - mainX - 3;

        var descriptionWidth = isWide ? mainWidth - 2 : mainWidth;
        var descriptionEditor = editingField == CardDetailField.Description ? editor : null;
        var descriptionTextWidth = descriptionEditor is null
            ? descriptionWidth
            : Math.Max(1, descriptionWidth - 1);
        IReadOnlyList<CardDetailLine> descriptionLines;
        int? descriptionCursorRow = null;
        int? descriptionCursorColumn = null;
        if (descriptionEditor is null)
        {
            descriptionLines = BuildDescriptionLines(card.Description, descriptionTextWidth);
        }
        else
        {
            var editorLayout = descriptionEditor.CreateVisualLayout(descriptionTextWidth);
            descriptionLines = editorLayout.Lines
                .Select(line => Line(line.Text, BoardStyles.TextStrong))
                .ToArray();
            descriptionCursorRow = editorLayout.CursorRow;
            descriptionCursorColumn = editorLayout.CursorColumn;
        }

        var externalUrlEditor = editingField == CardDetailField.ExternalUrl ? editor : null;
        var options = BuildOptionsLines(
            data,
            card,
            asideWidth,
            externalUrlEditor,
            showTimestamps);

        return new CardDetailLayout(
            width,
            height,
            titleLines,
            contentTop,
            contentBottom,
            isWide,
            mainX,
            mainWidth,
            asideX,
            asideWidth,
            titleTextWidth,
            titleWindowStart,
            titleCursorRow,
            titleCursorColumn,
            descriptionTextWidth,
            descriptionLines,
            options.Lines,
            descriptionCursorRow,
            descriptionCursorColumn,
            options.TagsFirstRow,
            options.TagsLastRow,
            options.ColumnFirstRow,
            options.ColumnLastRow,
            options.CardTypeFirstRow,
            options.CardTypeLastRow,
            options.AssignedUserFirstRow,
            options.AssignedUserLastRow,
            options.SlickFirstRow,
            options.SlickLastRow,
            options.ExternalUrlFirstRow,
            options.ExternalUrlLastRow,
            options.ExternalUrlCursorRow,
            options.ExternalUrlCursorColumn);
    }

    private static IReadOnlyList<CardDetailLine> BuildDescriptionLines(string description, int width)
    {
        var lines = new List<CardDetailLine>();

        if (string.IsNullOrWhiteSpace(description))
        {
            lines.Add(Line("No description.", BoardStyles.TextMuted));
            return lines;
        }

        foreach (var sourceLine in description.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (sourceLine.Length == 0)
            {
                lines.Add(CardDetailLine.Empty);
                continue;
            }

            lines.AddRange(UnicodeDisplay.WrapText(sourceLine, width, width)
                .Select(line => Line(line, BoardStyles.TextStrong)));
        }

        return lines;
    }

    private static OptionsContent BuildOptionsLines(
        BoardData data,
        BoardCard card,
        int width,
        MultilineTextEditor? externalUrlEditor,
        bool showTimestamps)
    {
        var lines = new List<CardDetailLine>
        {
            Line("TAGS", BoardStyles.TextMuted, bold: true, CardDetailField.Tags)
        };
        const int tagsFirstRow = 0;
        if (card.Tags.Count == 0)
        {
            lines.Add(Line("None", BoardStyles.TextStrong, field: CardDetailField.Tags));
        }
        else
        {
            lines.AddRange(BuildTagLines(
                card.Tags,
                Math.Max(1, width - 2),
                CardDetailField.Tags));
        }
        var tagsLastRow = lines.Count - 1;

        var column = data.Board.Columns
            .FirstOrDefault(candidate => candidate.Id == card.BoardColumnId)?.Title
            ?? $"Column #{card.BoardColumnId}";
        var columnRows = AddField(
            lines,
            "COLUMN",
            column,
            width,
            CardDetailField.BoardColumn);
        var cardTypeRows = AddField(
            lines,
            "TYPE",
            $"{UnicodeDisplay.EmojiLabelPrefix(card.CardTypeEmoji ?? string.Empty)}{card.CardTypeName}",
            width,
            CardDetailField.CardType);
        var assignedUserRows = AddField(
            lines,
            "ASSIGNED TO",
            card.AssignedUserDisplayName ?? "Unassigned",
            width,
            CardDetailField.AssignedUser);

        lines.Add(CardDetailLine.Empty);
        var slickFirstRow = lines.Count;
        lines.Add(Line("SLICK", BoardStyles.TextMuted, bold: true, CardDetailField.Slick));
        if (card.SlickId is int slickId && !string.IsNullOrWhiteSpace(card.SlickName))
        {
            data.Slicks.TryGetValue(slickId, out var slick);
            lines.Add(new CardDetailLine(
            [
                new CardDetailSpan("● ", BoardStyles.ResolveSlick(slick, slickId), Bold: true),
                new CardDetailSpan(card.SlickName, BoardStyles.TextStrong)
            ], CardDetailField.Slick));
        }
        else
        {
            lines.Add(Line("None", BoardStyles.TextStrong, field: CardDetailField.Slick));
        }
        var slickLastRow = lines.Count - 1;

        lines.Add(CardDetailLine.Empty);
        var externalUrlFirstRow = lines.Count;
        lines.Add(Line(
            "EXTERNAL URL",
            BoardStyles.TextMuted,
            bold: true,
            field: CardDetailField.ExternalUrl));
        int? externalUrlCursorRow = null;
        int? externalUrlCursorColumn = null;
        if (externalUrlEditor is null)
        {
            var externalUrlWidth = Math.Max(1, width - 2);
            lines.AddRange(UnicodeDisplay.WrapText(
                    card.ExternalUrl ?? "None",
                    externalUrlWidth,
                    externalUrlWidth)
                .Select(line => Line(line, BoardStyles.TextStrong, field: CardDetailField.ExternalUrl)));
        }
        else
        {
            var externalUrlLayout = externalUrlEditor.CreateVisualLayout(Math.Max(1, width - 3));
            externalUrlCursorRow = lines.Count + externalUrlLayout.CursorRow;
            externalUrlCursorColumn = externalUrlLayout.CursorColumn;
            lines.AddRange(externalUrlLayout.Lines
                .Select(line => Line(line.Text, BoardStyles.TextStrong, field: CardDetailField.ExternalUrl)));
        }

        var externalUrlLastRow = lines.Count - 1;
        if (showTimestamps)
        {
            AddField(lines, "CREATED", FormatDate(card.CardCreatedUtc), width);
            AddField(lines, "UPDATED", FormatDate(card.CardUpdatedUtc), width);
        }
        return new OptionsContent(
            lines,
            tagsFirstRow,
            tagsLastRow,
            columnRows.First,
            columnRows.Last,
            cardTypeRows.First,
            cardTypeRows.Last,
            assignedUserRows.First,
            assignedUserRows.Last,
            slickFirstRow,
            slickLastRow,
            externalUrlFirstRow,
            externalUrlLastRow,
            externalUrlCursorRow,
            externalUrlCursorColumn);
    }

    private static IReadOnlyList<CardDetailLine> BuildTagLines(
        IReadOnlyList<CardTag> tags,
        int width,
        CardDetailField? field = null)
    {
        var lines = new List<CardDetailLine>();
        var spans = new List<CardDetailSpan>();
        var used = 0;
        foreach (var tag in tags)
        {
            var style = BoardStyles.ResolveTag(tag);
            var label = $"{UnicodeDisplay.EmojiLabelPrefix(tag.Emoji ?? string.Empty)}{tag.Name}";
            label = UnicodeDisplay.Truncate(label, Math.Max(1, width - 2));
            var tagWidth = UnicodeDisplay.TextWidth(label) + 2;
            if (spans.Count > 0 && used + tagWidth > width)
            {
                lines.Add(new CardDetailLine(spans.ToArray(), field));
                spans = [];
                used = 0;
            }

            spans.Add(new CardDetailSpan("▐", style.LeftBackground));
            spans.Add(new CardDetailSpan(label, style.Foreground, Surface: style));
            spans.Add(new CardDetailSpan("▌", style.RightBackground));
            used += tagWidth;
        }

        if (spans.Count > 0)
        {
            lines.Add(new CardDetailLine(spans, field));
        }

        return lines;
    }

    private static FieldRows AddField(
        List<CardDetailLine> lines,
        string label,
        string value,
        int width,
        CardDetailField? field = null)
    {
        lines.Add(CardDetailLine.Empty);
        var first = lines.Count;
        lines.Add(Line(label, BoardStyles.TextMuted, bold: true, field));
        var contentWidth = field is null ? width : Math.Max(1, width - 2);
        lines.AddRange(UnicodeDisplay.WrapText(value, contentWidth, contentWidth)
            .Select(line => Line(line, BoardStyles.TextStrong, field: field)));
        return new FieldRows(first, lines.Count - 1);
    }

    private static string FormatDate(DateTime value) =>
        value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static CardDetailLine Line(
        string text,
        Rgb foreground,
        bool bold = false,
        CardDetailField? field = null) =>
        new([new CardDetailSpan(text, foreground, Bold: bold)], field);

    private static IReadOnlyList<string> LimitLines(
        IReadOnlyList<string> lines,
        int maximumLines,
        int width)
    {
        if (lines.Count <= maximumLines)
        {
            return lines;
        }

        var result = lines.Take(maximumLines).ToArray();
        result[^1] = UnicodeDisplay.Truncate(result[^1] + "…", width);
        return result;
    }

    private sealed record OptionsContent(
        IReadOnlyList<CardDetailLine> Lines,
        int TagsFirstRow,
        int TagsLastRow,
        int ColumnFirstRow,
        int ColumnLastRow,
        int CardTypeFirstRow,
        int CardTypeLastRow,
        int AssignedUserFirstRow,
        int AssignedUserLastRow,
        int SlickFirstRow,
        int SlickLastRow,
        int ExternalUrlFirstRow,
        int ExternalUrlLastRow,
        int? ExternalUrlCursorRow,
        int? ExternalUrlCursorColumn);

    private readonly record struct FieldRows(int First, int Last);

}
