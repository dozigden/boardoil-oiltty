using System.Buffers.Binary;
using System.IO.Compression;
using Xunit;

public sealed class CardDescriptionImagesTests
{
    // Independent line equations in an 8×24 cell, following the named endpoints
    // in Unicode U+1FB3C–U+1FB51. Opposite fills use the same edge.
    private static readonly (int Slope, int Intercept)[] DiagonalLines =
    [
        (2, 16), (1, 16), (4, 8), (2, 8), (6, 0), (-2, 8), (-1, 8),
        (-4, 16), (-2, 16), (-6, 24), (-1, 16), (-2, 32), (-1, 24),
        (-4, 40), (-2, 24), (-6, 48), (2, -8), (1, 0), (4, -16),
        (2, 0), (6, -24), (1, 8)
    ];

    public static IEnumerable<object[]> DiagonalPatterns => Enumerable.Range(0, 22)
        .SelectMany(index => new[] { new object[] { index, false }, new object[] { index, true } });

    [Theory]
    [MemberData(nameof(DiagonalPatterns))]
    public void Thumbnail_PreservesEverySmoothMosaicAndItsComplement(int shape, bool inverted)
    {
        var line = DiagonalLines[shape];
        var pixels = Enumerable.Range(0, 8 * 24).SelectMany(index =>
        {
            var x = index % 8 + 0.5;
            var y = index / 8 + 0.5;
            var value = (byte)((y >= line.Slope * x + line.Intercept) != inverted ? 255 : 0);
            return new byte[] { value, value, value, 255 };
        }).ToArray();
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(8, 24, pixels));
        var background = new Rgb(22, 29, 39);
        var span = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, background, ImageGlyphSets.Diagonals)).Spans);
        Assert.Equal(char.ConvertFromUtf32(0x1FB3C + shape), span.Text);
        Assert.Equal(1, UnicodeDisplay.TextWidth(span.Text));
        Assert.Equal(1, UnicodeDisplay.TextWidth(char.ConvertFromUtf32(0x1FB52 + shape)));
        for (var row = 0; row < 24; row++)
        for (var column = 0; column < 8; column++)
        {
            var offset = (row * 8 + column) * 4;
            Assert.Equal(new Rgb(pixels[offset], pixels[offset + 1], pixels[offset + 2]),
                ImageCellColour(span, row, column, columns: 8));
        }
        var disabled = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, background,
            ImageGlyphSets.All & ~ImageGlyphSets.Diagonals)).Spans);
        Assert.DoesNotContain(disabled.Text.EnumerateRunes(), rune => rune.Value is >= 0x1FB3C and <= 0x1FB67);
    }

    [Theory]
    [InlineData(1, "▏")]
    [InlineData(2, "▎")]
    [InlineData(3, "▍")]
    [InlineData(4, "▌")]
    [InlineData(5, "▋")]
    [InlineData(6, "▊")]
    [InlineData(7, "▉")]
    public void Thumbnail_PreservesVerticalEdgesWithOnlyVerticalEighthsEnabled(int eighths, string glyph)
    {
        foreach (var inverted in new[] { false, true })
        {
            var pixels = Enumerable.Range(0, 8 * 16).SelectMany(index =>
            {
                var value = (byte)((index % 8 < eighths) != inverted ? 255 : 0);
                return new byte[] { value, value, value, 255 };
            }).ToArray();
            var thumbnail = CardDescriptionThumbnail.DecodePng(Png(8, 16, pixels));
            var span = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, new Rgb(22, 29, 39),
                ImageGlyphSets.VerticalEighths)).Spans);
            Assert.Equal(glyph, span.Text);
            Assert.Equal(1, UnicodeDisplay.TextWidth(span.Text));
            for (var row = 0; row < 24; row++)
            for (var column = 0; column < 8; column++)
            {
                var expected = (byte)((column < eighths) != inverted ? 255 : 0);
                Assert.Equal(new Rgb(expected, expected, expected), ImageCellColour(span, row, column, columns: 8));
            }
            var disabled = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, new Rgb(22, 29, 39),
                ImageGlyphSets.HalfBlocks)).Spans);
            Assert.Equal("▀", disabled.Text);
        }
    }

    [Fact]
    public void StoreChangingGlyphsReusesLoadedImagesAndRequestsRedraw()
    {
        var downloads = 0;
        using var store = new CardDescriptionImageStore(
            _ => Task.FromResult(Attachments(new CardAttachment(7, "diagram.png", "image/png", 10, DateTime.UnixEpoch, null, true))),
            (_, _) => { downloads++; return Task.FromResult(Png(1, 1, [10, 20, 30, 255])); },
            ImageGlyphSets.HalfBlocks);
        Assert.Null(store.LoadedPreview());
        Assert.Equal(0, downloads);
        var image = store.Get("diagram.png").Thumbnail;
        Assert.NotNull(image);
        Assert.Same(image, store.LoadedPreview());
        var revision = store.Revision;
        store.SetGlyphSets(ImageGlyphSets.All);
        Assert.Equal(revision + 1, store.Revision);
        Assert.Equal(ImageGlyphSets.All, store.GlyphSets);
        Assert.Same(image, store.Get("diagram.png").Thumbnail);
        Assert.Equal(1, downloads);
        store.SetGlyphSets(ImageGlyphSets.All);
        Assert.Equal(revision + 1, store.Revision);
    }

    [Fact]
    public void Markdown_ParsesCanonicalPercentEncodedAttachmentReference()
    {
        var parsed = CardDescriptionImageMarkdown.TryParseLine(
            "  ![Architecture](boardoil-attachment:Architecture%20%28final%29.png)  ",
            out var image);

        Assert.True(parsed);
        Assert.Equal("Architecture", image.AltText);
        Assert.Equal("Architecture (final).png", image.AttachmentFileName);
    }

    [Fact]
    public void Markdown_DecodesUtf8AttachmentFileName()
    {
        Assert.True(CardDescriptionImageMarkdown.TryParseLine(
            "![Artwork](boardoil-attachment:%F0%9F%8E%A8%20plan.png)",
            out var image));

        Assert.Equal("🎨 plan.png", image.AttachmentFileName);
    }

    [Theory]
    [InlineData("boardoil-attachment:bad%2.png")]
    [InlineData("boardoil-attachment:%FF.png")]
    [InlineData("boardoil-attachment:%2Fescape.png")]
    [InlineData("boardoil-attachment:..")]
    [InlineData("https://example.test/tracker.png")]
    public void Markdown_LeavesUnsafeAndExternalSourcesUnavailable(string source)
    {
        Assert.True(CardDescriptionImageMarkdown.TryParseLine(
            $"![Diagram]({source})",
            out var image));

        Assert.Null(image.AttachmentFileName);
    }

    [Fact]
    public void Markdown_DoesNotTreatInlineImageTextAsAnImageBlock()
    {
        Assert.False(CardDescriptionImageMarkdown.TryParseLine(
            "Read ![Diagram](boardoil-attachment:diagram.png) before continuing.",
            out _));
    }

    [Fact]
    public void Thumbnail_RendersTwoVerticalPixelsInEachHalfBlockCell()
    {
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(
            2,
            2,
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255
            ]));

        var line = Assert.Single(thumbnail.RenderLines(2, 1, new Rgb(1, 2, 3)));

        Assert.Equal(2, line.Spans.Count);
        Assert.Equal(new Rgb(255, 0, 0), line.Spans[0].Foreground);
        Assert.Equal(new Rgb(0, 0, 255), line.Spans[0].Background);
        Assert.Equal(new Rgb(0, 255, 0), line.Spans[1].Foreground);
        Assert.Equal(new Rgb(255, 255, 255), line.Spans[1].Background);
        Assert.All(line.Spans, span => Assert.Equal("▀", span.Text));
    }

    [Fact]
    public void Thumbnail_CompositesTransparencyAndUsesPaneBackgroundForOddBottomHalf()
    {
        var background = new Rgb(22, 29, 39);
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(1, 1, [255, 0, 0, 0]));

        var span = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, background)).Spans);

        Assert.Equal(background, span.Foreground);
        Assert.Equal(background, span.Background);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(22, 29, 39)]
    [InlineData(237, 242, 250)]
    public void Thumbnail_MulticolouredBottomEdgeDoesNotTintPadding(byte red, byte green, byte blue)
    {
        var background = new Rgb(red, green, blue);
        var pixels = Enumerable.Range(0, 4 * 6).SelectMany(index => index % 2 == 0
            ? new byte[] { 255, 255, 255, 255 }
            : new byte[] { 255, 0, 0, 255 }).ToArray();
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(4, 6, pixels));
        var lines = thumbnail.RenderLines(2, 2, background);

        Assert.Equal(2, lines.Count);
        Assert.Equal(2, lines[^1].Spans.Count);
        Assert.All(lines[^1].Spans, span =>
        {
            // These shapes leave the whole lower half in a single colour.
            var paddingColour = span.Text switch
            {
                "▀" or "▘" or "▝" => span.Background,
                "▅" or "▆" or "▇" => span.Foreground,
                _ => throw new InvalidOperationException($"Shape {span.Text} splits the padding.")
            };
            Assert.Equal(background, paddingColour);
            Assert.NotEqual(span.Foreground, span.Background);
        });
    }

    [Theory]
    [InlineData(1, "▇")]
    [InlineData(2, "▆")]
    [InlineData(3, "▅")]
    public void Thumbnail_PaddedCellRetainsEighthHeightDetail(int upperEighths, string glyph)
    {
        var background = new Rgb(22, 29, 39);
        var pixels = Enumerable.Range(0, 8 * 24).SelectMany(index =>
        {
            var y = index / 8;
            return y >= 16 && y < 16 + (upperEighths * 2)
                ? new byte[] { 255, 255, 255, 255 }
                : new byte[] { background.Red, background.Green, background.Blue, 255 };
        }).ToArray();
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(8, 24, pixels));
        var span = Assert.Single(thumbnail.RenderLines(1, 2, background)[^1].Spans);

        Assert.Equal(glyph, span.Text);
        Assert.Equal(background, span.Foreground);
        Assert.Equal(new Rgb(255, 255, 255), span.Background);
    }

    [Fact]
    public void Thumbnail_RecompositesTransparencyForEachThemeBackground()
    {
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(1, 1, [255, 0, 0, 128]));
        var darkBackground = new Rgb(22, 29, 39);
        var lightBackground = new Rgb(237, 242, 250);

        var dark = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, darkBackground)).Spans);
        var light = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, lightBackground)).Spans);

        Assert.NotEqual(dark.Foreground, light.Foreground);
        Assert.Equal(darkBackground, dark.Background);
        Assert.Equal(lightBackground, light.Background);
    }

    [Fact]
    public void Thumbnail_RejectsDimensionsBeyondBoardOilThumbnailLimit()
    {
        Assert.Throws<InvalidDataException>(() =>
            CardDescriptionThumbnail.DecodePng(Png(201, 1, new byte[201 * 4])));
    }

    [Fact]
    public void Thumbnail_DownsamplesByAveragingSourceColours()
    {
        // Each sample averages a 24×4 source region containing all four colours.
        byte[][] colours = [[255, 0, 0, 255], [0, 255, 0, 255], [0, 0, 255, 255], [255, 255, 255, 255]];
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(48, 48,
            Enumerable.Range(0, 48 * 48).SelectMany(index => colours[((index / 48) % 2) * 2 + index % 2]).ToArray()));

        var span = Assert.Single(Assert.Single(
            thumbnail.RenderLines(1, 1, new Rgb(0, 0, 0))).Spans);

        Assert.InRange(span.Foreground.Red, 187, 189);
        Assert.InRange(span.Foreground.Green, 187, 189);
        Assert.InRange(span.Foreground.Blue, 187, 189);
    }

    [Theory]
    [InlineData(1, "▘")]
    [InlineData(2, "▝")]
    [InlineData(3, "▀")]
    [InlineData(4, "▖")]
    [InlineData(5, "▌")]
    [InlineData(6, "▞")]
    [InlineData(7, "▛")]
    public void Thumbnail_PreservesQuadrantEdgesWithTwoColours(int mask, string glyph)
    {
        var pixels = Enumerable.Range(0, 16).SelectMany(index =>
        {
            var quadrant = index % 2 + (index / 8) * 2;
            var value = (byte)((mask & (1 << quadrant)) != 0 ? 255 : 0);
            return new byte[] { value, value, value, 255 };
        }).ToArray();
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(4, 4, pixels));
        var line = Assert.Single(thumbnail.RenderLines(2, 1, new Rgb(22, 29, 39)));

        Assert.Equal(2, line.Spans.Count);
        Assert.All(line.Spans, span =>
        {
            Assert.Equal(glyph, span.Text);
            Assert.Equal(new Rgb(255, 255, 255), span.Foreground);
            Assert.Equal(new Rgb(0, 0, 0), span.Background);
        });
    }

    [Theory]
    [InlineData(1, "▁")]
    [InlineData(2, "▂")]
    [InlineData(3, "▃")]
    [InlineData(4, "▀")]
    [InlineData(5, "▅")]
    [InlineData(6, "▆")]
    [InlineData(7, "▇")]
    public void Thumbnail_PreservesHorizontalEdgesAtEighthCellIntervals(int lowerEighths, string glyph)
    {
        foreach (var inverted in new[] { false, true })
        {
            var pixels = Enumerable.Range(0, 8 * 16).SelectMany(index =>
            {
                var lower = index / 8 >= 16 - (lowerEighths * 2);
                var value = (byte)(lower != inverted ? 255 : 0);
                return new byte[] { value, value, value, 255 };
            }).ToArray();
            var thumbnail = CardDescriptionThumbnail.DecodePng(Png(8, 16, pixels));
            var span = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, new Rgb(22, 29, 39))).Spans);

            Assert.Equal(glyph, span.Text);
            var foreground = (byte)((lowerEighths == 4) == inverted ? 255 : 0);
            var background = (byte)(255 - foreground);
            Assert.Equal(new Rgb(foreground, foreground, foreground), span.Foreground);
            Assert.Equal(new Rgb(background, background, background), span.Background);
        }
    }

    // Sextant names in Unicode code-point order, independently describing their shapes.
    private const string SextantSections =
        "1 2 12 3 13 23 123 4 14 24 124 34 134 234 1234 5 15 25 125 35 235 1235 45 145 245 1245 345 1345 2345 12345 6 16 26 126 36 136 236 1236 46 146 1246 346 1346 2346 12346 56 156 256 1256 356 1356 2356 12356 456 1456 2456 12456 3456 13456 23456";

    public static TheoryData<int, string> SextantPatterns
    {
        get
        {
            var patterns = new TheoryData<int, string>();
            var codePoint = 0x1FB00;
            foreach (var sections in SextantSections.Split(' '))
            {
                var mask = sections.Aggregate(0, (value, digit) => value | (1 << (digit - '1')));
                patterns.Add(mask, char.ConvertFromUtf32(codePoint++));
            }
            return patterns;
        }
    }

    [Theory]
    [MemberData(nameof(SextantPatterns))]
    public void Thumbnail_PreservesEverySextantPattern(int mask, string originalGlyph)
    {
        var pixels = Enumerable.Range(0, 8 * 48).SelectMany(index =>
        {
            var part = (index % 8) / 4 + ((index / 8) / 16) * 2;
            var value = (byte)((mask & (1 << part)) != 0 ? 255 : 0);
            return new byte[] { value, value, value, 255 };
        }).ToArray();
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(8, 48, pixels));
        var span = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, new Rgb(22, 29, 39), ImageGlyphSets.All)).Spans);
        var scalar = Assert.Single(span.Text.EnumerateRunes()).Value;
        Assert.InRange(scalar, 0x1FB00, 0x1FB3B);
        Assert.Equal(1, UnicodeDisplay.TextWidth(span.Text));
        Assert.Equal(1, UnicodeDisplay.TextWidth(originalGlyph));

        for (var row = 0; row < 24; row++)
        for (var column = 0; column < 2; column++)
        {
            var expected = (mask & (1 << ((row / 8) * 2 + column))) != 0 ? (byte)255 : (byte)0;
            Assert.Equal(new Rgb(expected, expected, expected), ImageCellColour(span, row, column));
        }
    }

    [Theory]
    [InlineData(22, 29, 39)]
    [InlineData(237, 242, 250)]
    public void Thumbnail_AllGlyphFittingPreservesPartialRowPadding(byte red, byte green, byte blue)
    {
        var background = new Rgb(red, green, blue);
        var pixels = Enumerable.Range(0, 24 * 72).SelectMany(index =>
            new byte[] { (byte)((index * 37) % 256), (byte)((index * 73) % 256), (byte)((index * 11) % 256), 255 }).ToArray();
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(24, 72, pixels));
        var lines = thumbnail.RenderLines(1, 2, background, ImageGlyphSets.All);
        Assert.Equal(2, lines.Count);
        Assert.All(lines[^1].Spans, span =>
        {
            for (var row = 12; row < 24; row++)
            for (var column = 0; column < 8; column++)
                Assert.Equal(background, ImageCellColour(span, row, column, columns: 8));
        });
    }

    [Theory]
    [InlineData(17)]
    [InlineData(83)]
    [InlineData(251)]
    public void Thumbnail_ChoosesMinimumErrorShapeForMixedColours(int seed)
    {
        var random = new Random(seed);
        var samples = Enumerable.Range(0, 192)
            .Select(_ => new Rgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))).ToArray();
        var pixels = Enumerable.Range(0, 24 * 48).SelectMany(index =>
        {
            var pixel = samples[(index / 24 / 2) * 8 + (index % 24 / 3)];
            return new byte[] { pixel.Red, pixel.Green, pixel.Blue, 255 };
        }).ToArray();
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(24, 48, pixels));
        var actual = Assert.Single(Assert.Single(thumbnail.RenderLines(1, 1, new Rgb(22, 29, 39), ImageGlyphSets.All)).Spans);
        var white = new Rgb(255, 255, 255);
        var black = new Rgb(0, 0, 0);
        var glyphs = new[] { "▀", "▘", "▝", "▖", "▌", "▞", "▛", "▁", "▂", "▃", "▅", "▆", "▇", "▏", "▎", "▍", "▋", "▊", "▉" }
            .Concat(Enumerable.Range(0x1FB00, 60).Select(char.ConvertFromUtf32))
            .Concat(Enumerable.Range(0x1FB3C, 22).Select(char.ConvertFromUtf32));
        var minimumError = glyphs.Min(glyph =>
        {
            var shape = new CardDetailSpan(glyph, white, black);
            var groups = Enumerable.Range(0, 192).GroupBy(index =>
                ImageCellColour(shape, index / 8, index % 8, columns: 8) == white);
            return groups.Sum(group =>
            {
                var colours = group.Select(index => samples[index]).ToArray();
                var mean = new Rgb((byte)Math.Round(colours.Average(colour => colour.Red)),
                    (byte)Math.Round(colours.Average(colour => colour.Green)),
                    (byte)Math.Round(colours.Average(colour => colour.Blue)));
                return colours.Sum(colour => SquaredDistance(colour, mean));
            });
        });
        var actualError = Enumerable.Range(0, 192).Sum(index =>
            SquaredDistance(samples[index], ImageCellColour(actual, index / 8, index % 8, columns: 8)!.Value));
        Assert.Equal(minimumError, actualError);
    }

    private static long SquaredDistance(Rgb left, Rgb right) =>
        (long)(left.Red - right.Red) * (left.Red - right.Red)
        + (long)(left.Green - right.Green) * (left.Green - right.Green)
        + (long)(left.Blue - right.Blue) * (left.Blue - right.Blue);

    private static Rgb? ImageCellColour(CardDetailSpan span, int row, int column, int columns = 2)
    {
        var scalar = Assert.Single(span.Text.EnumerateRunes()).Value;
        column = column * 8 / columns;
        bool foreground;
        if (scalar is >= 0x1FB00 and <= 0x1FB3B)
        {
            var sections = SextantSections.Split(' ')[scalar - 0x1FB00];
            foreground = sections.Contains((char)('1' + (row / 8) * 2 + column / 4));
        }
        else if (scalar is >= 0x2581 and <= 0x2587)
        {
            foreground = row >= (8 - (scalar - 0x2580)) * 3;
        }
        else if (scalar is >= 0x2589 and <= 0x258F)
        {
            foreground = column < 0x2590 - scalar;
        }
        else if (scalar is >= 0x1FB3C and <= 0x1FB51)
        {
            var line = DiagonalLines[scalar - 0x1FB3C];
            foreground = row + 0.5 >= line.Slope * (column + 0.5) + line.Intercept;
        }
        else
        {
            var mask = span.Text switch
            {
                "▀" => 3, "▘" => 1, "▝" => 2, "▖" => 4, "▌" => 5, "▞" => 6, "▛" => 7,
                _ => throw new InvalidOperationException($"Unexpected image character {span.Text}")
            };
            foreground = (mask & (1 << ((row / 12) * 2 + column / 4))) != 0;
        }
        return foreground ? span.Foreground : span.Background;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public void Thumbnail_UsesOnlyEnabledSetsAndSeparatesCachedVariants(int selected)
    {
        var sets = (ImageGlyphSets)selected;
        var random = new Random(83);
        var rgba = Enumerable.Range(0, 96 * 96).SelectMany(_ =>
            new byte[] { (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255 }).ToArray();
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(96, 96, rgba));
        var background = new Rgb(22, 29, 39);
        var all = thumbnail.RenderLines(30, 18, background, ImageGlyphSets.All);
        var lines = thumbnail.RenderLines(30, 18, background, sets);
        Assert.Same(lines, thumbnail.RenderLines(30, 18, background, sets));
        if (sets != ImageGlyphSets.All) Assert.NotSame(all, lines);
        foreach (var span in lines.SelectMany(line => line.Spans))
        {
            if (string.IsNullOrWhiteSpace(span.Text) || span.Text == "▀") continue;
            var scalar = Assert.Single(span.Text.EnumerateRunes()).Value;
            if (span.Text == "▌")
            {
                Assert.NotEqual(ImageGlyphSets.HalfBlocks, sets & (ImageGlyphSets.Quadrants | ImageGlyphSets.VerticalEighths));
                continue;
            }
            var family = scalar switch
            {
                >= 0x1FB00 and <= 0x1FB3B => ImageGlyphSets.Sextants,
                >= 0x2581 and <= 0x2587 => ImageGlyphSets.Eighths,
                >= 0x2589 and <= 0x258F => ImageGlyphSets.VerticalEighths,
                >= 0x1FB3C and <= 0x1FB67 => ImageGlyphSets.Diagonals,
                _ when "▘▝▖▌▞▛".Contains(span.Text, StringComparison.Ordinal) => ImageGlyphSets.Quadrants,
                _ => throw new InvalidOperationException($"Unexpected glyph {span.Text}")
            };
            Assert.True(sets.HasFlag(family), $"Disabled family {family} emitted {span.Text}.");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Detail_UsesConfiguredGlyphsInDescriptionsAndComments(bool inComment, bool quadrants)
    {
        var (data, sourceCard) = DetailData();
        const string markdown = "![Stripes](boardoil-attachment:stripes.png)";
        var card = sourceCard with { Description = inComment ? string.Empty : markdown };
        var baseline = new CardDetailLayoutEngine().Create(data, card, 80, 40);
        // Two source columns per terminal cell, preserving alternating black/white edges.
        var width = baseline.DescriptionTextWidth * 2;
        var pixels = Enumerable.Range(0, width * 4)
            .SelectMany(index => new byte[] { (byte)(index % 2 * 255), 0, 0, 255 }).ToArray();
        using var store = new CardDescriptionImageStore(
            _ => Task.FromResult(Attachments(new CardAttachment(7, "stripes.png", "image/png", 10,
                DateTime.UnixEpoch, null, true))),
            (_, _) => Task.FromResult(Png(width, 4, pixels)),
            quadrants ? ImageGlyphSets.Quadrants : ImageGlyphSets.HalfBlocks);
        var layout = new CardDetailLayoutEngine().Create(data, card, 80, 40,
            comments: inComment ? [new CardComment(1, card.Id, 7, markdown, DateTime.UnixEpoch, "Luke", null)] : [],
            descriptionImages: store);
        var lines = inComment ? layout.CommentLines : layout.DescriptionLines;
        if (quadrants)
        {
            Assert.Contains(lines.SelectMany(line => line.Spans), span => span.Text == "▌"
                && span.Foreground == new Rgb(0, 0, 0) && span.Background == new Rgb(255, 0, 0));
        }
        else
        {
            Assert.Contains(lines.SelectMany(line => line.Spans), span => span.Text == "▀");
            Assert.DoesNotContain(lines.SelectMany(line => line.Spans), span => span.Text == "▌");
        }
    }

    [Fact]
    public async Task Store_ListsOnceAndDownloadsEachAvailableThumbnailOnce()
    {
        var listCalls = 0;
        var downloadCalls = 0;
        using var store = new CardDescriptionImageStore(
            _ =>
            {
                listCalls++;
                return Task.FromResult(Attachments(
                    new CardAttachment(7, "Diagram.png", "image/png", 10, DateTime.UnixEpoch, null, true)));
            },
            (_, _) =>
            {
                downloadCalls++;
                return Task.FromResult(Png(1, 1, [10, 20, 30, 255]));
            });

        Assert.Equal(CardDescriptionThumbnailStatus.Ready, store.Get("diagram.PNG").Status);
        Assert.Equal(CardDescriptionThumbnailStatus.Ready, store.Get("Diagram.png").Status);
        await WaitForRevisionAsync(store, 1);

        Assert.Equal(1, listCalls);
        Assert.Equal(1, downloadCalls);
    }

    [Fact]
    public async Task Store_ReportsLoadingThenRequestsARedrawWhenThumbnailCompletes()
    {
        var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var store = new CardDescriptionImageStore(
            _ => Task.FromResult(Attachments(
                new CardAttachment(7, "diagram.png", "image/png", 10, DateTime.UnixEpoch, null, true))),
            (_, _) => pending.Task);

        var loading = store.Get("diagram.png");
        pending.SetResult(Png(1, 1, [10, 20, 30, 255]));
        await WaitForRevisionAsync(store, 1);
        var ready = store.Get("diagram.png");

        Assert.Equal(CardDescriptionThumbnailStatus.Loading, loading.Status);
        Assert.Equal(CardDescriptionThumbnailStatus.Ready, ready.Status);
        Assert.NotNull(ready.Thumbnail);
    }

    [Fact]
    public async Task Store_DoesNotDownloadOriginalWhenThumbnailIsUnavailable()
    {
        var downloadCalls = 0;
        using var store = new CardDescriptionImageStore(
            _ => Task.FromResult(Attachments(
                new CardAttachment(7, "diagram.png", "image/png", 10, DateTime.UnixEpoch, null, false))),
            (_, _) =>
            {
                downloadCalls++;
                return Task.FromResult(Array.Empty<byte>());
            });

        var snapshot = store.Get("diagram.png");
        await WaitForRevisionAsync(store, 1);

        Assert.Equal(CardDescriptionThumbnailStatus.Unavailable, snapshot.Status);
        Assert.Equal(0, downloadCalls);
    }

    [Fact]
    public void Detail_RendersThumbnailButEditsOriginalMarkdown()
    {
        var (data, sourceCard) = DetailData();
        var markdown = "Before\n\n![Diagram](boardoil-attachment:diagram.png)\n\nAfter";
        var card = sourceCard with { Description = markdown };
        using var store = new CardDescriptionImageStore(
            _ => Task.FromResult(Attachments(
                new CardAttachment(7, "diagram.png", "image/png", 10, DateTime.UnixEpoch, null, true))),
            (_, _) => Task.FromResult(Png(2, 2,
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255
            ])));
        var screen = new CardDetailScreen(data, card, "connected", store);
        var viewport = new TerminalViewport(80, 24);

        var viewed = screen.Render(viewport).Canvas;
        screen.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), viewport);
        var editing = PlainText(screen.Render(viewport).Canvas);

        Assert.Contains(Enumerable.Range(0, viewed.Height)
            .SelectMany(y => Enumerable.Range(0, viewed.Width).Select(x => viewed.CellAt(x, y))),
            cell => cell.Grapheme == "▀"
                    && cell.Foreground == new Rgb(255, 0, 0)
                    && cell.Background == new Rgb(0, 0, 255));
        Assert.Contains("![Diagram](boardoil-attachment:diagram.png)", editing);
        Assert.Equal(markdown, screen.Description);
    }

    [Fact]
    public void Detail_AllowsImageToUseTheFullDescriptionViewport()
    {
        var (data, sourceCard) = DetailData();
        var card = sourceCard with
        {
            Description = "![Diagram](boardoil-attachment:diagram.png)"
        };
        var rgba = Enumerable.Range(0, 40 * 40)
            .SelectMany(_ => new byte[] { 180, 40, 60, 255 })
            .ToArray();
        using var store = new CardDescriptionImageStore(
            _ => Task.FromResult(Attachments(
                new CardAttachment(7, "diagram.png", "image/png", 10, DateTime.UnixEpoch, null, true))),
            (_, _) => Task.FromResult(Png(40, 40, rgba)));

        var layout = new CardDetailLayoutEngine().Create(
            data,
            card,
            80,
            40,
            descriptionImages: store);

        Assert.Equal(20, layout.DescriptionLines.Count);
        Assert.True(layout.DescriptionLines.Count > 12);
        Assert.True(layout.DescriptionLines.Count <= layout.PaneViewportRows);
    }

    [Fact]
    public void Detail_DoesNotFetchExternalImages()
    {
        var (data, sourceCard) = DetailData();
        var card = sourceCard with
        {
            Description = "![Tracker](https://example.test/tracker.png)"
        };
        var listCalls = 0;
        using var store = new CardDescriptionImageStore(
            _ =>
            {
                listCalls++;
                return Task.FromResult(Attachments());
            },
            (_, _) => Task.FromResult(Array.Empty<byte>()));

        var rendered = PlainText(new CardDetailScreen(data, card, "connected", store)
            .Render(new TerminalViewport(80, 24)).Canvas);

        Assert.Contains("Tracker — preview unavailable", rendered);
        Assert.Equal(0, listCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Detail_ClipsImageRowsAndScrollsToFollowingText(bool inComment)
    {
        var (data, sourceCard) = DetailData();
        var rgba = Enumerable.Range(0, 20 * 20)
            .SelectMany(_ => new byte[] { 180, 40, 60, 255 })
            .ToArray();
        var card = sourceCard with
        {
            Description = inComment ? string.Empty : "Before\n\n![Diagram](boardoil-attachment:diagram.png)\n\nAfter"
        };
        using var store = new CardDescriptionImageStore(
            _ => Task.FromResult(Attachments(
                new CardAttachment(7, "diagram.png", "image/png", 10, DateTime.UnixEpoch, null, true))),
            (_, _) => Task.FromResult(Png(20, 20, rgba)));
        var viewport = new TerminalViewport(60, 14);
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var screen = new CardDetailScreen(data, card, "connected", store);
        if (inComment)
        {
            screen.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false), viewport);
            screen.ApplyComments([new CardComment(1, card.Id, 7,
                "Before\n\n![Diagram](boardoil-attachment:diagram.png)\n\nAfter", DateTime.UnixEpoch, "Luke", null)]);
        }

        var initial = screen.Render(viewport).Canvas;
        screen.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false), viewport);
        var scrolled = screen.Render(viewport).Canvas;

        var imageCells = Enumerable.Range(0, initial.Height)
            .SelectMany(y => Enumerable.Range(0, initial.Width)
                .Select(x => (X: x, Y: y, Cell: initial.CellAt(x, y))))
            .Where(candidate => candidate.Cell.Grapheme == "▀")
            .ToArray();
        Assert.NotEmpty(imageCells);
        Assert.All(imageCells, candidate =>
        {
            Assert.InRange(candidate.X, layout.MainX + 1, layout.MainX + layout.MainWidth - 2);
            Assert.InRange(candidate.Y, layout.PaneContentTop, layout.ContentBottom - 1);
        });
        Assert.Contains("After", PlainText(scrolled));
    }

    [Fact]
    public void Comments_RenderTruecolourImagesAndShareTheDescriptionCache()
    {
        var (data, sourceCard) = DetailData();
        const string markdown = "![Diagram](boardoil-attachment:diagram.png)";
        var card = sourceCard with { Description = markdown };
        var listCalls = 0;
        var downloadCalls = 0;
        using var store = new CardDescriptionImageStore(
            _ =>
            {
                listCalls++;
                return Task.FromResult(Attachments(
                    new CardAttachment(7, "diagram.png", "image/png", 10, DateTime.UnixEpoch, null, true)));
            },
            (_, _) =>
            {
                downloadCalls++;
                return Task.FromResult(Png(1, 2, [255, 0, 0, 255, 0, 0, 255, 255]));
            });
        var screen = new CardDetailScreen(data, card, "connected", store);
        var viewport = new TerminalViewport(100, 30);
        screen.Render(viewport);
        screen.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false), viewport);
        screen.ApplyComments(
        [
            new CardComment(1, card.Id, 7, $"Before\n\n{markdown}\n\nAfter", DateTime.UnixEpoch, "Luke", null),
            new CardComment(2, card.Id, 8, markdown, DateTime.UnixEpoch.AddMinutes(1), "Alex", null)
        ]);

        var rendered = screen.Render(viewport).Canvas;
        var text = PlainText(rendered);

        Assert.Equal(2, Enumerable.Range(0, rendered.Height)
            .SelectMany(y => Enumerable.Range(0, rendered.Width).Select(x => rendered.CellAt(x, y)))
            .Count(cell => cell.Grapheme == "▀" && cell.Foreground == new Rgb(255, 0, 0)
                && cell.Background == new Rgb(0, 0, 255)));
        Assert.Contains("Before", text);
        Assert.Contains("After", text);
        Assert.True(text.IndexOf("Alex", StringComparison.Ordinal) < text.IndexOf("Luke", StringComparison.Ordinal));
        Assert.DoesNotContain("boardoil-attachment:", text);
        Assert.Equal(1, listCalls);
        Assert.Equal(1, downloadCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommentDraft_RemainsMarkdownWithoutFetchingImages(bool editing)
    {
        var (data, card) = DetailData();
        const string markdown = "![Draft](boardoil-attachment:draft.png)";
        var listCalls = 0;
        using var store = new CardDescriptionImageStore(
            _ =>
            {
                listCalls++;
                return Task.FromResult(Attachments());
            },
            (_, _) => throw new InvalidOperationException("Drafts should not load images."));

        var layout = new CardDetailLayoutEngine().Create(data, card, 120, 24,
            editingField: editing ? CardDetailField.Comments : null,
            editor: editing ? new MultilineTextEditor(markdown) : null,
            comments: [], commentDraft: markdown, descriptionImages: store);

        var text = string.Join('\n', layout.CommentLines.Select(line => string.Concat(line.Spans.Select(span => span.Text))));
        Assert.Contains(markdown, text);
        Assert.DoesNotContain("preview unavailable", text);
        Assert.Equal(0, listCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommentImage_LoadCompletionRequestsRedrawAndShowsImageOrFallback(bool failed)
    {
        var (data, card) = DetailData();
        var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var store = new CardDescriptionImageStore(
            _ => Task.FromResult(Attachments(
                new CardAttachment(7, "diagram.png", "image/png", 10, DateTime.UnixEpoch, null, true))),
            (_, _) => pending.Task);
        var screen = new CardDetailScreen(data, card, "connected", store);
        var viewport = new TerminalViewport(100, 24);
        screen.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false), viewport);
        screen.ApplyComments([new CardComment(1, card.Id, 7,
            "![Diagram](boardoil-attachment:diagram.png)", DateTime.UnixEpoch, "Luke", null)]);

        Assert.Contains("Diagram — loading…", PlainText(screen.Render(viewport).Canvas));
        var revision = screen.RenderRevision;
        if (failed)
        {
            pending.SetException(new HttpRequestException("Offline"));
        }
        else
        {
            pending.SetResult(Png(1, 1, [10, 20, 30, 255]));
        }

        await WaitForRevisionAsync(store, revision + 1);
        Assert.True(screen.RenderRevision > revision);
        var rendered = PlainText(screen.Render(viewport).Canvas);
        Assert.DoesNotContain("loading…", rendered);
        Assert.Contains(failed ? "Diagram — preview unavailable" : "▀", rendered);
    }

    [Fact]
    public void Comments_ExternalAndMissingImagesShowAltText()
    {
        var (data, card) = DetailData();
        var listCalls = 0;
        using var store = new CardDescriptionImageStore(
            _ =>
            {
                listCalls++;
                return Task.FromResult(Attachments());
            },
            (_, _) => throw new InvalidOperationException("No thumbnail should be downloaded."));
        var screen = new CardDetailScreen(data, card, "connected", store);
        var viewport = new TerminalViewport(100, 24);
        screen.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false), viewport);
        screen.ApplyComments([new CardComment(1, card.Id, 7,
            "![External](https://example.test/tracker.png)", DateTime.UnixEpoch, "Luke", null)]);

        Assert.Contains("External — preview unavailable", PlainText(screen.Render(viewport).Canvas));
        Assert.Equal(0, listCalls);
        screen.ApplyComments([new CardComment(1, card.Id, 7,
            "![Missing](boardoil-attachment:missing.png)", DateTime.UnixEpoch, "Luke", null)]);
        Assert.Contains("Missing — preview unavailable", PlainText(screen.Render(viewport).Canvas));
        Assert.Equal(1, listCalls);
    }

    private static async Task WaitForRevisionAsync(CardDescriptionImageStore store, long revision)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (store.Revision < revision && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(store.Revision >= revision);
    }

    private static CardAttachmentList Attachments(params CardAttachment[] items) =>
        new(items, 10 * 1024 * 1024);

    private static byte[] Png(int width, int height, byte[] rgba)
    {
        Assert.Equal(width * height * 4, rgba.Length);
        using var result = new MemoryStream();
        result.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(result, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba, y * width * 4, width * 4);
            }
        }

        WriteChunk(result, "IDAT"u8, compressed.ToArray());
        WriteChunk(result, "IEND"u8, []);
        return result.ToArray();
    }

    private static void WriteChunk(Stream destination, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        destination.Write(length);
        destination.Write(type);
        destination.Write(data);

        var crc = uint.MaxValue;
        foreach (var value in type)
        {
            crc = UpdateCrc(crc, value);
        }

        foreach (var value in data)
        {
            crc = UpdateCrc(crc, value);
        }

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        destination.Write(checksum);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320u;
        }

        return crc;
    }

    private static (BoardData Data, BoardCard Card) DetailData()
    {
        var card = TestBoardFactory.Card(42, 2, "Image card");
        var board = new BoardSnapshot(
            1,
            "Test board",
            string.Empty,
            true,
            "Owner",
            [new BoardColumn(2, "Todo", "1", [card])]);
        return (new BoardData(
            board,
            new Dictionary<int, CardTypeDefinition>
            {
                [1] = new(1, "Story", "📙", "auto", "{}", IsSystem: true)
            },
            new Dictionary<int, SlickDefinition>(),
            [],
            []), card);
    }

    private static string PlainText(TerminalCanvas canvas)
    {
        var lines = new List<string>();
        for (var y = 0; y < canvas.Height; y++)
        {
            lines.Add(string.Concat(Enumerable.Range(0, canvas.Width)
                .Select(x => canvas.CellAt(x, y))
                .Where(cell => !cell.Continuation)
                .Select(cell => cell.Grapheme)));
        }

        return string.Join('\n', lines);
    }
}
