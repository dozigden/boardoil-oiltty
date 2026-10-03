using Xunit;

public sealed class ImageGlyphSettingsTests
{
    [Theory]
    [InlineData("halfblocks", 0, "halfblocks")]
    [InlineData("quadrants", 1, "quadrants")]
    [InlineData("eighths", 2, "eighths")]
    [InlineData("sextants", 4, "sextants")]
    [InlineData("vertical-eighths", 8, "vertical-eighths")]
    [InlineData("diagonals", 16, "diagonals")]
    [InlineData("diagonals,vertical-eighths,sextants,eighths,quadrants", 31, "quadrants,eighths,sextants,vertical-eighths,diagonals")]
    [InlineData("vertical-eighths,sextants,eighths,quadrants", 15, "quadrants,eighths,sextants,vertical-eighths")]
    [InlineData("eighths,quadrants", 3, "quadrants,eighths")]
    [InlineData(" HALFblocks, QUADRANTS ,sextants,quadrants", 5, "quadrants,sextants")]
    [InlineData("quadrants,eighths,sextants", 7, "quadrants,eighths,sextants")]
    public void Parse_IndependentSetsRoundTrip(string value, int expected, string canonical)
    {
        var sets = ImageGlyphSettings.Parse(value);
        Assert.Equal((ImageGlyphSets)expected, sets);
        Assert.Equal(canonical, ImageGlyphSettings.Format(sets));
        Assert.Equal(sets, ImageGlyphSettings.Parse(ImageGlyphSettings.Format(sets)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("quadrants,")]
    [InlineData("quadrants,,eighths")]
    [InlineData("octants")]
    [InlineData("all")]
    public void Parse_RejectsInvalidSets(string value) =>
        Assert.Throws<ArgumentException>(() => ImageGlyphSettings.Parse(value));
}
