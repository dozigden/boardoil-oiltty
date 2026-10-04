using Xunit;

[Collection("Board palette")]
public sealed class BoardChecklistTests
{
    [Theory]
    [InlineData("Dark", false, 0)]
    [InlineData("Dark", true, 3)]
    [InlineData("Light", false, 7)]
    [InlineData("Light", true, 3)]
    public void Checklist_RendersAtBottomRightBesideTagsWithoutAddingARow(
        string theme, bool selected, int completed)
    {
        var previousTheme = BoardStyles.Theme;
        try
        {
            BoardStyles.UseTheme(Enum.Parse<OilTTYTheme>(theme));
            var card = TestBoardFactory.Card(42, 1, "Title", tags:
                [TestBoardFactory.Tag(1, "Design")]) with
            {
                CompletedChecklistItemCount = completed,
                TotalChecklistItemCount = 7
            };
            var (layout, canvas) = Render(card, selected: selected);
            var header = RowText(canvas, layout.Y + 1);

            Assert.Contains("📙 Title", header);
            Assert.Contains("#42", header);
            Assert.DoesNotContain("☑", header);
            var labelY = layout.Y + layout.Height - 2;
            Assert.Contains("▐Design▌", RowText(canvas, labelY));
            Assert.Contains($"☑\uFE0E {completed}/7", RowText(canvas, labelY));
            Assert.Equal(4, layout.Height);
            Assert.Equal("🬦", canvas.CellAt(layout.X, layout.Y).Grapheme);
            Assert.Equal(" ", canvas.CellAt(layout.X + 1, layout.Y + 1).Grapheme);
            Assert.Equal(selected ? BoardStyles.Selection : BoardStyles.BoardBackground,
                canvas.CellAt(layout.X, layout.Y).Background);
            var labelX = layout.X + layout.Width - 2 - UnicodeDisplay.TextWidth(layout.ChecklistLabel!);
            var style = BoardStyles.ResolveCard(CardType);
            Assert.Equal(style.Foreground, canvas.CellAt(labelX, labelY).Foreground);
            Assert.Equal(style.BackgroundAt(labelX - layout.X, layout.Width),
                canvas.CellAt(labelX, labelY).Background);
        }
        finally
        {
            BoardStyles.UseTheme(previousTheme);
        }
    }

    [Fact]
    public void NoChecklist_LeavesCardLayoutUnchangedAndDoesNotParseDescription()
    {
        var card = TestBoardFactory.Card(42, 1, "Title") with { Description = "- [x] Task" };

        var (layout, canvas) = Render(card);

        Assert.Equal(3, layout.Height);
        Assert.Contains("#42", RowText(canvas, layout.Y + 1));
        Assert.DoesNotContain("☑", RowText(canvas, layout.Y + 1));
    }

    [Fact]
    public void Checklist_WrappedEmojiTitleAssigneeAndTagsRemainReadableInNarrowColumns()
    {
        var card = TestBoardFactory.Card(42, 1, "Ship 👩‍💻 checklist support", tags:
        [
            TestBoardFactory.Tag(1, "Design"),
            TestBoardFactory.Tag(2, "Development")
        ]) with
        {
            AssignedUserId = 1,
            AssignedUserDisplayName = "Luke",
            CompletedChecklistItemCount = 3,
            TotalChecklistItemCount = 7
        };

        var (layout, canvas) = Render(card);
        var titleX = layout.X + 2 + UnicodeDisplay.TextWidth(UnicodeDisplay.EmojiLabelPrefix("📙"));
        var renderedTitle = new List<string>();
        for (var index = 0; index < layout.TitleLines.Count; index++)
        {
            var end = layout.X + layout.Width - 2;
            if (index == 0)
            {
                end -= UnicodeDisplay.TextWidth($"#{card.Id}") + 1;
            }

            renderedTitle.Add(RowText(canvas, layout.Y + 1 + index, titleX, end - titleX).Trim());
        }

        Assert.Equal(card.Title, string.Join(" ", renderedTitle));
        var content = string.Join("\n", Enumerable.Range(layout.Y + 1, layout.Height - 2)
            .Select(y => RowText(canvas, y, layout.X + 1, layout.Width - 2)));
        Assert.Contains("👤 Luke", content);
        Assert.Contains("▐Design▌", content);
        Assert.Contains("▐Development▌", content);
        Assert.Contains("☑\uFE0E 3/7", content);
    }

    [Theory]
    [InlineData("abcdefghijklmno", true)]
    [InlineData("abcdefghijklmnop", false)]
    [InlineData("A tag too long for a narrow column", false)]
    public void Checklist_OnlySharesTagRowWhenThereIsAGap(string tagName, bool sharesRow)
    {
        var card = TestBoardFactory.Card(42, 1, "Title", tags:
            [TestBoardFactory.Tag(1, tagName)]) with
        {
            CompletedChecklistItemCount = 3,
            TotalChecklistItemCount = 7
        };

        var (layout, canvas) = Render(card);
        var tagRow = RowText(canvas, layout.Y + 2);
        var labelRow = RowText(canvas, layout.Y + layout.Height - 2);

        Assert.Equal(sharesRow ? 4 : 5, layout.Height);
        Assert.Contains("▌", tagRow);
        Assert.Equal(sharesRow, tagRow.Contains("☑", StringComparison.Ordinal));
        Assert.Contains("☑\uFE0E 3/7", labelRow);
        if (sharesRow)
        {
            Assert.Contains("▌ ☑", tagRow);
        }
    }

    [Fact]
    public void Checklist_SharesLastWrappedTagRow()
    {
        var card = TestBoardFactory.Card(42, 1, "Title", tags:
        [
            TestBoardFactory.Tag(1, "abcdefghijklmnopq"),
            TestBoardFactory.Tag(2, "Design")
        ]) with { CompletedChecklistItemCount = 3, TotalChecklistItemCount = 7 };

        var (layout, canvas) = Render(card);

        Assert.Equal(5, layout.Height);
        Assert.Contains("▐abcdefghijklmnopq▌", RowText(canvas, layout.Y + 2));
        Assert.Contains("▐Design▌", RowText(canvas, layout.Y + 3));
        Assert.Contains("☑\uFE0E 3/7", RowText(canvas, layout.Y + 3));
    }

    [Fact]
    public void Checklist_OnTitleOnlyCardUsesARowBelowTheCardNumber()
    {
        var card = TestBoardFactory.Card(42, 1, "Title") with
            { CompletedChecklistItemCount = 3, TotalChecklistItemCount = 7 };

        var (layout, canvas) = Render(card);

        Assert.Equal(4, layout.Height);
        Assert.Contains("#42", RowText(canvas, layout.Y + 1));
        Assert.Contains("☑\uFE0E 3/7", RowText(canvas, layout.Y + 2));
    }

    [Fact]
    public void LargeCounts_UseASeparateRowWithoutOverwritingTitleOrTags()
    {
        var card = TestBoardFactory.Card(1234567890, 1, "Title", tags:
            [TestBoardFactory.Tag(1, "Design")]) with
        {
            CompletedChecklistItemCount = 1234567890,
            TotalChecklistItemCount = 1234567890
        };

        var (layout, canvas) = Render(card);

        Assert.Contains("📙 Title", RowText(canvas, layout.Y + 1));
        Assert.Contains("#1234567890", RowText(canvas, layout.Y + 1));
        Assert.Contains("▐Design▌", RowText(canvas, layout.Y + 2));
        Assert.Contains("☑\uFE0E 1234567890/1234567890", RowText(canvas, layout.Y + 3));
        Assert.Equal(5, layout.Height);
    }

    [Fact]
    public void PartialCard_ChecklistIsClippedAboveFooter()
    {
        var card = TestBoardFactory.Card(42, 1, "Title") with
        {
            CompletedChecklistItemCount = 3,
            TotalChecklistItemCount = 7
        };
        var (layout, canvas) = Render(card);
        canvas.Put(0, canvas.Height - 2, "footer", BoardStyles.TextStrong);
        var footer = RowText(canvas, canvas.Height - 2);

        BoardCardRenderer.Draw(canvas, layout with { Y = canvas.Height - 3 },
            BoardStyles.ResolveCard(CardType), selected: false, moving: false);

        Assert.Equal(footer, RowText(canvas, canvas.Height - 2));
    }

    private static readonly CardTypeDefinition CardType = new(
        1, "Story", "📙", "solid", "{\"backgroundColor\":\"#385688\",\"borderMode\":\"none\"}");

    private static (BoardLayoutCard Layout, TerminalCanvas Canvas) Render(BoardCard card, bool selected = false)
    {
        var board = TestBoardFactory.Board(TestBoardFactory.Column(1, card), TestBoardFactory.Column(2));
        var selection = new BoardSelection();
        selection.Normalise(board);
        var layout = new BoardLayoutEngine().Create(board, selection, 64, 24);
        var data = new BoardData(board, new Dictionary<int, CardTypeDefinition> { [1] = CardType },
            new Dictionary<int, SlickDefinition>(), [], []);
        var canvas = new BoardRenderer().Render(data, layout, selected ? card.Id : null, null, "connected").Canvas;
        return (Assert.Single(layout.Cards), canvas);
    }

    private static string RowText(TerminalCanvas canvas, int y, int x = 0, int? width = null) =>
        string.Concat(Enumerable.Range(x, width ?? canvas.Width)
            .Select(column => canvas.CellAt(column, y))
            .Where(cell => !cell.Continuation)
            .Select(cell => cell.Grapheme));
}
