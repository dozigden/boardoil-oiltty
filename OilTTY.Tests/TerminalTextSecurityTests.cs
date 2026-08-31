using System.Net;
using System.Text;
using Xunit;

public sealed class TerminalTextSecurityTests
{
    private static readonly TerminalViewport Viewport = new(120, 40);

    [Fact]
    public void ServerProvidedBoardAndCardFields_RenderAsInertText()
    {
        var tag = new CardTag(4, "Tag\u001bName", "auto", "{}", "✨\u0007");
        var card = TestBoardFactory.Card(42, 2, "Card\u001bName", [tag], slickId: 12) with
        {
            CardTypeName = "Type\u009bName",
            CardTypeEmoji = "⌨️\u009c",
            Description = "Description\u001b]0;owned\u0007Text\nSecond line",
            AssignedUserId = 7,
            AssignedUserDisplayName = "Member\rName",
            SlickName = "Slick\nName"
        };
        var column = new BoardColumn(2, "Column\u001bName", "2", [card]);
        var board = new BoardSnapshot(
            1,
            "Board\u001bName",
            string.Empty,
            true,
            "Owner",
            [column]);
        var data = new BoardData(
            board,
            new Dictionary<int, CardTypeDefinition>
            {
                [1] = new(1, card.CardTypeName, card.CardTypeEmoji, "auto", "{}", IsSystem: true)
            },
            new Dictionary<int, SlickDefinition>
            {
                [12] = new(12, card.SlickName!, "auto", "{}")
            },
            [tag],
            [
                new BoardMember(
                    7,
                    "member",
                    card.AssignedUserDisplayName!,
                    null,
                    "Contributor",
                    DateTime.UnixEpoch,
                    DateTime.UnixEpoch)
            ]);

        var boardText = PlainText(new BoardScreen(data, "Error\u009dText")
            .Render(Viewport)
            .Canvas);
        var detailText = PlainText(new CardDetailScreen(data, card, "Error\u009dText")
            .Render(Viewport)
            .Canvas);

        Assert.Contains("Board�Name", boardText);
        Assert.Contains("COLUMN�NAME", boardText);
        Assert.Contains("Card�Name", boardText);
        Assert.Contains("Tag�Name", boardText);
        Assert.Contains("Description�]0;owned�Text", detailText);
        Assert.Contains("Second line", detailText);
        Assert.Contains("Type�Name", detailText);
        Assert.Contains("Member�Name", detailText);
        Assert.Contains("Slick�Name", detailText);
        Assert.Contains("Error�Text", boardText);
        AssertNoControls(boardText);
        AssertNoControls(detailText);
    }

    [Fact]
    public void ServerValidationErrors_RenderAsInertText()
    {
        var tag = TestBoardFactory.Tag(4, "UI");
        var card = TestBoardFactory.Card(42, 2, tags: [tag]);
        var column = new BoardColumn(2, "In progress", "2", [card]);
        var board = new BoardSnapshot(1, "Board", string.Empty, true, "Owner", [column]);
        var data = new BoardData(
            board,
            new Dictionary<int, CardTypeDefinition>
            {
                [1] = new(1, "Story", "📙", "auto", "{}", IsSystem: true)
            },
            new Dictionary<int, SlickDefinition>(),
            [tag],
            []);
        var screen = new CardDetailScreen(data, card, "connected");
        screen.SetSaveError(new BoardOilRequestException(
            HttpStatusCode.BadRequest,
            "Server\u001b]52;c;payload\u0007error"));

        var text = PlainText(screen.Render(Viewport).Canvas);

        Assert.Contains("Server�]52;c;payload�error", text);
        AssertNoControls(text);
    }

    private static string PlainText(TerminalCanvas canvas)
    {
        var result = new StringBuilder();
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var cell = canvas.CellAt(x, y);
                if (!cell.Continuation)
                {
                    result.Append(cell.Grapheme);
                }
            }

            result.Append('\n');
        }

        return result.ToString();
    }

    private static void AssertNoControls(string text)
    {
        Assert.DoesNotContain(
            text,
            character => char.IsControl(character) && character != '\n');
    }
}
