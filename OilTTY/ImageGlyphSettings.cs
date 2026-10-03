[Flags]
internal enum ImageGlyphSets
{
    HalfBlocks = 0,
    Quadrants = 1,
    Eighths = 2,
    Sextants = 4,
    VerticalEighths = 8,
    Diagonals = 16,
    All = Quadrants | Eighths | Sextants | VerticalEighths | Diagonals,
    Default = All
}

internal static class ImageGlyphSettings
{
    public static ImageGlyphSets Parse(string value)
    {
        var sets = ImageGlyphSets.HalfBlocks;
        foreach (var name in value.Split(',', StringSplitOptions.TrimEntries))
        {
            sets |= name.ToLowerInvariant() switch
            {
                "halfblocks" => ImageGlyphSets.HalfBlocks,
                "quadrants" => ImageGlyphSets.Quadrants,
                "eighths" => ImageGlyphSets.Eighths,
                "sextants" => ImageGlyphSets.Sextants,
                "vertical-eighths" => ImageGlyphSets.VerticalEighths,
                "diagonals" => ImageGlyphSets.Diagonals,
                _ => throw new ArgumentException(
                    "Image glyphs must be a comma-separated list of halfblocks, quadrants, eighths, sextants, vertical-eighths, or diagonals.")
            };
        }
        return sets;
    }

    public static string Format(ImageGlyphSets sets)
    {
        if ((sets & ~ImageGlyphSets.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sets));
        }
        if (sets == ImageGlyphSets.HalfBlocks)
        {
            return "halfblocks";
        }
        var names = new List<string>();
        if (sets.HasFlag(ImageGlyphSets.Quadrants)) names.Add("quadrants");
        if (sets.HasFlag(ImageGlyphSets.Eighths)) names.Add("eighths");
        if (sets.HasFlag(ImageGlyphSets.Sextants)) names.Add("sextants");
        if (sets.HasFlag(ImageGlyphSets.VerticalEighths)) names.Add("vertical-eighths");
        if (sets.HasFlag(ImageGlyphSets.Diagonals)) names.Add("diagonals");
        return string.Join(',', names);
    }
}
