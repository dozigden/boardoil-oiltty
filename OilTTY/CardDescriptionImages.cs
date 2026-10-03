using System.Buffers.Binary;
using StbImageSharp;

internal sealed record CardDescriptionImageReference(
    string AltText,
    string? AttachmentFileName);

internal static class CardDescriptionImageMarkdown
{
    private const string AttachmentPrefix = "boardoil-attachment:";
    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryParseLine(
        string sourceLine,
        out CardDescriptionImageReference reference)
    {
        reference = null!;
        var line = sourceLine.Trim();
        if (!line.StartsWith("![", StringComparison.Ordinal) || !line.EndsWith(')'))
        {
            return false;
        }

        var separator = FindAltTextEnd(line);
        if (separator < 0)
        {
            return false;
        }

        var destination = line[(separator + 2)..^1].Trim();
        if (destination.StartsWith('<') && destination.EndsWith('>'))
        {
            destination = destination[1..^1];
        }
        else
        {
            var titleSeparator = destination.IndexOfAny([' ', '\t']);
            if (titleSeparator >= 0)
            {
                destination = destination[..titleSeparator];
            }
        }

        if (destination.Length == 0)
        {
            return false;
        }

        var altText = UnescapeAltText(line[2..separator]);
        var attachmentFileName = destination.StartsWith(AttachmentPrefix, StringComparison.Ordinal)
            ? DecodeAttachmentFileName(destination[AttachmentPrefix.Length..])
            : null;
        reference = new CardDescriptionImageReference(altText, attachmentFileName);
        return true;
    }

    private static int FindAltTextEnd(string line)
    {
        for (var index = 2; index < line.Length - 1; index++)
        {
            if (line[index] == ']'
                && line[index + 1] == '('
                && !IsEscaped(line, index))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsEscaped(string text, int index)
    {
        var slashCount = 0;
        for (var candidate = index - 1; candidate >= 0 && text[candidate] == '\\'; candidate--)
        {
            slashCount++;
        }

        return slashCount % 2 != 0;
    }

    private static string UnescapeAltText(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\'
                && index + 1 < text.Length
                && text[index + 1] is '\\' or '[' or ']')
            {
                index++;
            }

            result.Append(text[index]);
        }

        return result.ToString();
    }

    private static string? DecodeAttachmentFileName(string encoded)
    {
        if (encoded.Length == 0 || HasInvalidPercentEncoding(encoded))
        {
            return null;
        }

        string fileName;
        try
        {
            fileName = Uri.UnescapeDataString(encoded);
        }
        catch (UriFormatException)
        {
            return null;
        }

        return fileName.Length is > 0 and <= 255
               && fileName is not "." and not ".."
               && !fileName.Any(character => character is '/' or '\\' || char.IsControl(character))
            ? fileName
            : null;
    }

    private static bool HasInvalidPercentEncoding(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }

            var bytes = new List<byte>();
            while (index < value.Length && value[index] == '%')
            {
                if (index + 2 >= value.Length
                    || !Uri.IsHexDigit(value[index + 1])
                    || !Uri.IsHexDigit(value[index + 2]))
                {
                    return true;
                }

                bytes.Add(Convert.ToByte(value.Substring(index + 1, 2), 16));
                index += 3;
            }

            try
            {
                StrictUtf8.GetString(bytes.ToArray());
            }
            catch (System.Text.DecoderFallbackException)
            {
                return true;
            }

            index--;
        }

        return false;
    }
}

internal enum CardDescriptionThumbnailStatus
{
    Loading,
    Ready,
    Unavailable
}

internal readonly record struct CardDescriptionThumbnailSnapshot(
    CardDescriptionThumbnailStatus Status,
    CardDescriptionThumbnail? Thumbnail = null);

internal sealed class CardDescriptionImageStore : IDisposable
{
    private const int MaximumConcurrentDownloads = 2;

    private readonly Func<CancellationToken, Task<CardAttachmentList>> _loadAttachments;
    private readonly Func<int, CancellationToken, Task<byte[]>> _loadThumbnail;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _downloadSlots = new(MaximumConcurrentDownloads);
    private readonly Dictionary<string, ImageState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private Task<CardAttachmentList>? _attachmentsTask;
    private long _revision;
    private bool _disposed;

    public CardDescriptionImageStore(BoardOilClient client, int boardId, int cardId)
        : this(
            cancellationToken => client.LoadCardAttachmentsAsync(boardId, cardId, cancellationToken),
            (attachmentId, cancellationToken) =>
                client.LoadAttachmentThumbnailAsync(boardId, attachmentId, cancellationToken))
    {
    }

    internal CardDescriptionImageStore(
        Func<CancellationToken, Task<CardAttachmentList>> loadAttachments,
        Func<int, CancellationToken, Task<byte[]>> loadThumbnail)
    {
        _loadAttachments = loadAttachments;
        _loadThumbnail = loadThumbnail;
    }

    public long Revision => Interlocked.Read(ref _revision);

    public CardDescriptionThumbnailSnapshot Get(string fileName)
    {
        ImageState state;
        var startLoading = false;
        var cancellationToken = default(CancellationToken);
        lock (_gate)
        {
            if (!_states.TryGetValue(fileName, out state!))
            {
                state = new ImageState();
                _states.Add(fileName, state);
                startLoading = !_disposed;
                if (startLoading)
                {
                    cancellationToken = _cancellation.Token;
                }
            }
        }

        if (startLoading)
        {
            _ = LoadAsync(fileName, state, cancellationToken);
        }

        lock (_gate)
        {
            return new CardDescriptionThumbnailSnapshot(state.Status, state.Thumbnail);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task LoadAsync(
        string fileName,
        ImageState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var attachments = await GetAttachmentsAsync(cancellationToken);
            var attachment = attachments.Items.FirstOrDefault(candidate =>
                string.Equals(candidate.OriginalFileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (attachment is not { HasThumbnail: true })
            {
                Complete(state, null);
                return;
            }

            await _downloadSlots.WaitAsync(cancellationToken);
            try
            {
                var bytes = await _loadThumbnail(attachment.Id, cancellationToken);
                Complete(state, CardDescriptionThumbnail.DecodePng(bytes));
            }
            finally
            {
                _downloadSlots.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            Complete(state, null);
        }
    }

    private Task<CardAttachmentList> GetAttachmentsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _attachmentsTask ??= _loadAttachments(cancellationToken);
        }
    }

    private void Complete(ImageState state, CardDescriptionThumbnail? thumbnail)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            state.Thumbnail = thumbnail;
            state.Status = thumbnail is null
                ? CardDescriptionThumbnailStatus.Unavailable
                : CardDescriptionThumbnailStatus.Ready;
        }

        Interlocked.Increment(ref _revision);
    }

    private sealed class ImageState
    {
        public CardDescriptionThumbnailStatus Status { get; set; }

        public CardDescriptionThumbnail? Thumbnail { get; set; }
    }
}

internal sealed class CardDescriptionThumbnail
{
    private const int MaximumEdgeLength = 200;
    private const int MaximumRenderedVariants = 8;
    private readonly byte[] _rgba;
    private readonly Dictionary<RenderKey, IReadOnlyList<CardDetailLine>> _renderCache = [];
    private readonly object _renderGate = new();

    private CardDescriptionThumbnail(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        _rgba = rgba;
    }

    public int Width { get; }

    public int Height { get; }

    public static CardDescriptionThumbnail DecodePng(byte[] bytes)
    {
        if (bytes.Length < 24
            || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("The attachment thumbnail is not a PNG image.");
        }

        var declaredWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var declaredHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (declaredWidth is 0 or > MaximumEdgeLength
            || declaredHeight is 0 or > MaximumEdgeLength)
        {
            throw new InvalidDataException("The attachment thumbnail dimensions are invalid.");
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        if (image.Width != declaredWidth
            || image.Height != declaredHeight
            || image.Data.Length != image.Width * image.Height * 4)
        {
            throw new InvalidDataException("The attachment thumbnail pixels are invalid.");
        }

        return new CardDescriptionThumbnail(image.Width, image.Height, image.Data);
    }

    public IReadOnlyList<CardDetailLine> RenderLines(
        int maximumColumns,
        int maximumRows,
        Rgb background)
    {
        var key = new RenderKey(maximumColumns, maximumRows, background);
        lock (_renderGate)
        {
            if (_renderCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var rendered = RenderLinesCore(maximumColumns, maximumRows, background);
        lock (_renderGate)
        {
            if (_renderCache.Count >= MaximumRenderedVariants)
            {
                _renderCache.Clear();
            }

            _renderCache[key] = rendered;
        }

        return rendered;
    }

    private IReadOnlyList<CardDetailLine> RenderLinesCore(
        int maximumColumns,
        int maximumRows,
        Rgb background)
    {
        maximumColumns = Math.Max(1, maximumColumns);
        maximumRows = Math.Max(1, maximumRows);
        var scale = Math.Min(
            1d,
            Math.Min(maximumColumns / (double)Width, maximumRows * 2d / Height));
        var pixelWidth = Math.Clamp((int)Math.Round(Width * scale), 1, maximumColumns);
        var pixelHeight = Math.Clamp((int)Math.Round(Height * scale), 1, maximumRows * 2);
        // Keep the existing physical size, sampling a 2×8 grid per cell so both
        // quadrant shapes and eighth-height horizontal edges use the same evidence.
        var sampleWidth = pixelWidth * 2;
        var sampleHeight = pixelHeight * 4;
        var pixels = Resize(sampleWidth, sampleHeight, background);
        Span<Rgb> cellPixels = stackalloc Rgb[16];
        var leftPadding = Math.Max(0, (maximumColumns - pixelWidth) / 2);
        var lines = new List<CardDetailLine>((pixelHeight + 1) / 2);
        for (var sourceY = 0; sourceY < pixelHeight; sourceY += 2)
        {
            var spans = new List<CardDetailSpan>(pixelWidth + 1);
            if (leftPadding > 0)
            {
                spans.Add(new CardDetailSpan(new string(' ', leftPadding), BoardStyles.TextStrong, background));
            }

            for (var x = 0; x < pixelWidth; x++)
            {
                for (var row = 0; row < 8; row++)
                {
                    var sampleY = (sourceY * 4) + row;
                    var offset = (sampleY * sampleWidth) + (x * 2);
                    // An odd final half-row retains the pane background below it.
                    cellPixels[row * 2] = sampleY < sampleHeight ? pixels[offset] : background;
                    cellPixels[(row * 2) + 1] = sampleY < sampleHeight ? pixels[offset + 1] : background;
                }
                spans.Add(FitImageCell(cellPixels, sourceY + 1 < pixelHeight ? null : background));
            }

            lines.Add(new CardDetailLine(spans));
        }

        return lines;
    }

    private static readonly (string Glyph, int Mask)[] ImageGlyphs = CreateImageGlyphs();

    private static (string Glyph, int Mask)[] CreateImageGlyphs()
    {
        var glyphs = new List<(string, int)>();
        string[] quadrants = [" ", "▘", "▝", "▀", "▖", "▌", "▞", "▛"];
        // Complementary masks have the same fit with swapped colours. Prefer the
        // existing shapes on ties, especially the half block for flat colours.
        foreach (var mask in new[] { 3, 1, 2, 4, 5, 6, 7 })
        {
            var expanded = 0;
            for (var row = 0; row < 8; row++)
            {
                var quadrantRow = row < 4 ? 0 : 2;
                for (var column = 0; column < 2; column++)
                {
                    if ((mask & (1 << (quadrantRow + column))) != 0)
                    {
                        expanded |= 1 << ((row * 2) + column);
                    }
                }
            }
            glyphs.Add((quadrants[mask], expanded));
        }

        string[] lowerBlocks = [" ", "▁", "▂", "▃", "▄", "▅", "▆", "▇"];
        for (var eighths = 1; eighths < 8; eighths++)
        {
            if (eighths != 4) // Already represented by the upper half block.
            {
                glyphs.Add((lowerBlocks[eighths], (0xffff << ((8 - eighths) * 2)) & 0xffff));
            }
        }
        return glyphs.ToArray();
    }

    private static CardDetailSpan FitImageCell(ReadOnlySpan<Rgb> pixels, Rgb? bottomPadding)
    {
        var bestError = long.MaxValue;
        var result = new CardDetailSpan("▀", pixels[0], pixels[8]);
        foreach (var (glyph, mask) in ImageGlyphs)
        {
            const int lowerHalfMask = 0xff00;
            var coveredPadding = mask & lowerHalfMask;
            if (bottomPadding is not null && coveredPadding != 0 && coveredPadding != lowerHalfMask)
            {
                // Splitting the padding would force both colours to the pane background,
                // leaving no colour available to represent the image above it.
                continue;
            }

            var foreground = AverageSamples(pixels, mask, true);
            var background = AverageSamples(pixels, mask, false);
            if (bottomPadding is Rgb padding)
            {
                // Padding is outside the image, so its colour must be exact rather than
                // averaged with image samples that share this part of the glyph.
                if (coveredPadding == lowerHalfMask)
                {
                    foreground = padding;
                }
                else
                {
                    background = padding;
                }
            }
            var error = 0L;
            for (var index = 0; index < pixels.Length; index++)
            {
                var colour = (mask & (1 << index)) != 0 ? foreground : background;
                var red = pixels[index].Red - colour.Red;
                var green = pixels[index].Green - colour.Green;
                var blue = pixels[index].Blue - colour.Blue;
                error += (red * red) + (green * green) + (blue * blue);
            }

            if (error < bestError)
            {
                bestError = error;
                result = new CardDetailSpan(glyph, foreground, background);
            }
        }

        return result;
    }

    private static Rgb AverageSamples(ReadOnlySpan<Rgb> pixels, int mask, bool selected)
    {
        var red = 0;
        var green = 0;
        var blue = 0;
        var count = 0;
        for (var index = 0; index < pixels.Length; index++)
        {
            if (((mask & (1 << index)) != 0) != selected)
            {
                continue;
            }

            red += pixels[index].Red;
            green += pixels[index].Green;
            blue += pixels[index].Blue;
            count++;
        }

        return new Rgb((byte)Math.Round(red / (double)count),
            (byte)Math.Round(green / (double)count), (byte)Math.Round(blue / (double)count));
    }

    private Rgb[] Resize(int targetWidth, int targetHeight, Rgb background)
    {
        var target = new Rgb[targetWidth * targetHeight];
        var backgroundRed = ToLinear(background.Red);
        var backgroundGreen = ToLinear(background.Green);
        var backgroundBlue = ToLinear(background.Blue);
        for (var targetY = 0; targetY < targetHeight; targetY++)
        {
            var sourceTop = targetY * Height / (double)targetHeight;
            var sourceBottom = (targetY + 1) * Height / (double)targetHeight;
            for (var targetX = 0; targetX < targetWidth; targetX++)
            {
                var sourceLeft = targetX * Width / (double)targetWidth;
                var sourceRight = (targetX + 1) * Width / (double)targetWidth;
                var red = 0d;
                var green = 0d;
                var blue = 0d;
                var alpha = 0d;
                var area = 0d;
                for (var sourceY = (int)Math.Floor(sourceTop); sourceY < Math.Ceiling(sourceBottom); sourceY++)
                {
                    var verticalWeight = Math.Min(sourceBottom, sourceY + 1) - Math.Max(sourceTop, sourceY);
                    for (var sourceX = (int)Math.Floor(sourceLeft); sourceX < Math.Ceiling(sourceRight); sourceX++)
                    {
                        var horizontalWeight = Math.Min(sourceRight, sourceX + 1) - Math.Max(sourceLeft, sourceX);
                        var weight = horizontalWeight * verticalWeight;
                        var offset = ((sourceY * Width) + sourceX) * 4;
                        var sourceAlpha = _rgba[offset + 3] / 255d;
                        red += ToLinear(_rgba[offset]) * sourceAlpha * weight;
                        green += ToLinear(_rgba[offset + 1]) * sourceAlpha * weight;
                        blue += ToLinear(_rgba[offset + 2]) * sourceAlpha * weight;
                        alpha += sourceAlpha * weight;
                        area += weight;
                    }
                }

                var opacity = alpha / area;
                target[(targetY * targetWidth) + targetX] = new Rgb(
                    ToSrgb((red / area) + (backgroundRed * (1 - opacity))),
                    ToSrgb((green / area) + (backgroundGreen * (1 - opacity))),
                    ToSrgb((blue / area) + (backgroundBlue * (1 - opacity))));
            }
        }

        return target;
    }

    private static double ToLinear(byte value)
    {
        var component = value / 255d;
        return component <= 0.04045
            ? component / 12.92
            : Math.Pow((component + 0.055) / 1.055, 2.4);
    }

    private static byte ToSrgb(double value)
    {
        value = Math.Clamp(value, 0, 1);
        var component = value <= 0.0031308
            ? value * 12.92
            : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
        return (byte)Math.Round(component * 255);
    }

    private readonly record struct RenderKey(int MaximumColumns, int MaximumRows, Rgb Background);
}
