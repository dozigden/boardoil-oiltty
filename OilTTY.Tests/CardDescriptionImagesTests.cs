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
        var thumbnail = CardDescriptionThumbnail.DecodePng(Png(
            2,
            2,
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255
            ]));

        var span = Assert.Single(Assert.Single(
            thumbnail.RenderLines(1, 1, new Rgb(0, 0, 0))).Spans);

        Assert.InRange(span.Foreground.Red, 187, 189);
        Assert.InRange(span.Foreground.Green, 187, 189);
        Assert.InRange(span.Foreground.Blue, 187, 189);
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

    [Fact]
    public void Detail_ClipsImageRowsAndScrollsToFollowingText()
    {
        var (data, sourceCard) = DetailData();
        var rgba = Enumerable.Range(0, 20 * 20)
            .SelectMany(_ => new byte[] { 180, 40, 60, 255 })
            .ToArray();
        var card = sourceCard with
        {
            Description = "Before\n\n![Diagram](boardoil-attachment:diagram.png)\n\nAfter"
        };
        using var store = new CardDescriptionImageStore(
            _ => Task.FromResult(Attachments(
                new CardAttachment(7, "diagram.png", "image/png", 10, DateTime.UnixEpoch, null, true))),
            (_, _) => Task.FromResult(Png(20, 20, rgba)));
        var viewport = new TerminalViewport(60, 14);
        var layout = new CardDetailLayoutEngine().Create(data, card, viewport.Width, viewport.Height);
        var screen = new CardDetailScreen(data, card, "connected", store);

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
