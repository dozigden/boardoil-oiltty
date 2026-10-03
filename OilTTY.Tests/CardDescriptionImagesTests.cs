using System.Buffers.Binary;
using System.IO.Compression;
using Xunit;

public sealed class CardDescriptionImagesTests
{
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
        // Each sample averages an 8×4 source region containing all four colours.
        byte[][] colours = [[255, 0, 0, 255], [0, 255, 0, 255], [0, 0, 255, 255], [255, 255, 255, 255]];
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(16, 16,
            Enumerable.Range(0, 256).SelectMany(index => colours[((index / 16) % 2) * 2 + index % 2]).ToArray()));

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Detail_UsesQuadrantsInDescriptionsAndComments(bool inComment)
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
            (_, _) => Task.FromResult(Png(width, 4, pixels)));
        var layout = new CardDetailLayoutEngine().Create(data, card, 80, 40,
            comments: inComment ? [new CardComment(1, card.Id, 7, markdown, DateTime.UnixEpoch, "Luke", null)] : [],
            descriptionImages: store);
        var lines = inComment ? layout.CommentLines : layout.DescriptionLines;
        Assert.Contains(lines.SelectMany(line => line.Spans), span => span.Text == "▌"
            && span.Foreground == new Rgb(0, 0, 0) && span.Background == new Rgb(255, 0, 0));
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
