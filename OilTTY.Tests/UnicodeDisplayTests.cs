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
}
