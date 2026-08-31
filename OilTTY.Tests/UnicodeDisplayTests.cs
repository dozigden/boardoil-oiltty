using Xunit;

public sealed class UnicodeDisplayTests
{
    [Theory]
    [InlineData("A", 1)]
    [InlineData("界", 2)]
    [InlineData("🛢️", 1)]
    [InlineData("👨‍💻", 2)]
    [InlineData("🇬🇧", 2)]
    public void TextWidth_UsesTerminalCellWidths(string text, int expectedWidth)
    {
        Assert.Equal(expectedWidth, UnicodeDisplay.TextWidth(text));
    }

    [Fact]
    public void EmojiLabelPrefix_LeavesAVisibleGapForNarrowEmojiGlyphs()
    {
        Assert.Equal("🛢️  ", UnicodeDisplay.EmojiLabelPrefix("🛢️"));
        Assert.Equal("😀 ", UnicodeDisplay.EmojiLabelPrefix("😀"));
    }

    [Fact]
    public void Truncate_DoesNotSplitAGrapheme()
    {
        Assert.Equal("A…", UnicodeDisplay.Truncate("A👨‍💻B", 3));
    }

    [Fact]
    public void TextOperations_NeutraliseControlsWithoutChangingDisplayWidth()
    {
        const string source = "A\u001b👨‍💻B";
        const string expected = "A�👨‍💻B";

        Assert.Equal(expected, string.Concat(UnicodeDisplay.Graphemes(source)));
        Assert.Equal(5, UnicodeDisplay.TextWidth(source));
        Assert.Equal(expected, UnicodeDisplay.Truncate(source, 5));
        Assert.Equal([expected], UnicodeDisplay.WrapText(source, 5, 5));
    }
}
