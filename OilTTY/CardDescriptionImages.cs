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

    public CardDescriptionImageStore(
        BoardOilClient client, int boardId, int cardId, ImageGlyphSets glyphSets = ImageGlyphSets.Default)
        : this(
            cancellationToken => client.LoadCardAttachmentsAsync(boardId, cardId, cancellationToken),
            (attachmentId, cancellationToken) =>
                client.LoadAttachmentThumbnailAsync(boardId, attachmentId, cancellationToken), glyphSets)
    {
    }

    internal CardDescriptionImageStore(
        Func<CancellationToken, Task<CardAttachmentList>> loadAttachments,
        Func<int, CancellationToken, Task<byte[]>> loadThumbnail,
        ImageGlyphSets glyphSets = ImageGlyphSets.Default)
    {
        GlyphSets = glyphSets;
        _loadAttachments = loadAttachments;
        _loadThumbnail = loadThumbnail;
    }

    public ImageGlyphSets GlyphSets { get; private set; }

    public void SetGlyphSets(ImageGlyphSets glyphSets)
    {
        if (GlyphSets == glyphSets) return;
        GlyphSets = glyphSets;
        Interlocked.Increment(ref _revision);
    }

    public CardDescriptionThumbnail? LoadedPreview()
    {
        lock (_gate)
        {
            return _states.Values.FirstOrDefault(state => state.Status == CardDescriptionThumbnailStatus.Ready)?.Thumbnail;
        }
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
    // 24 divides evenly into halves, thirds, and eighths.
    private const int SamplesPerCellRow = 24;
    private const int SamplesPerCellColumn = 8;
    private const int SamplesPerCell = SamplesPerCellRow * SamplesPerCellColumn;
    private readonly byte[] _rgba;
    private readonly Dictionary<RenderKey, IReadOnlyList<CardDetailLine>> _renderCache = [];
    private readonly object _renderGate = new();

    internal CardDescriptionThumbnail(int width, int height, byte[] rgba)
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
        Rgb background,
        ImageGlyphSets glyphSets = ImageGlyphSets.Default)
    {
        var key = new RenderKey(maximumColumns, maximumRows, background, glyphSets);
        lock (_renderGate)
        {
            if (_renderCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var rendered = RenderLinesCore(maximumColumns, maximumRows, background, glyphSets);
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
        Rgb background,
        ImageGlyphSets glyphSets)
    {
        maximumColumns = Math.Max(1, maximumColumns);
        maximumRows = Math.Max(1, maximumRows);
        var scale = Math.Min(
            1d,
            Math.Min(maximumColumns / (double)Width, maximumRows * 2d / Height));
        var pixelWidth = Math.Clamp((int)Math.Round(Width * scale), 1, maximumColumns);
        var pixelHeight = Math.Clamp((int)Math.Round(Height * scale), 1, maximumRows * 2);
        // Keep the existing physical size. An 8×24 sample grid represents quadrant,
        // sextant, and both eighth-block directions exactly.
        var sampleWidth = pixelWidth * SamplesPerCellColumn;
        var sampleHeight = pixelHeight * (SamplesPerCellRow / 2);
        var pixels = Resize(sampleWidth, sampleHeight, background);
        Span<Rgb> cellPixels = stackalloc Rgb[SamplesPerCell];
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
                for (var row = 0; row < SamplesPerCellRow; row++)
                {
                    var sampleY = (sourceY * (SamplesPerCellRow / 2)) + row;
                    var offset = (sampleY * sampleWidth) + (x * SamplesPerCellColumn);
                    // An odd final half-row retains the pane background below it.
                    for (var column = 0; column < SamplesPerCellColumn; column++)
                        cellPixels[row * SamplesPerCellColumn + column] = sampleY < sampleHeight
                            ? pixels[offset + column] : background;
                }
                spans.Add(FitImageCell(cellPixels, sourceY + 1 < pixelHeight ? null : background, glyphSets));
            }

            lines.Add(new CardDetailLine(spans));
        }

        return lines;
    }

    private static readonly ImageGlyph[] ImageGlyphs = CreateImageGlyphs();

    private static ImageGlyph[] CreateImageGlyphs()
    {
        var glyphs = new List<ImageGlyph>();
        void Add(string text, ImageGlyphSets set, Func<int, int, bool> covers)
        {
            var indices = Enumerable.Range(0, SamplesPerCell)
                .Where(index => covers(index / SamplesPerCellColumn, index % SamplesPerCellColumn)).ToArray();
            var paddingCount = indices.Count(index => index >= SamplesPerCell / 2);
            glyphs.Add(new ImageGlyph(text, set, indices, paddingCount));
        }
        void AddParts(string text, ImageGlyphSets set, int mask, int rows) =>
            Add(text, set, (row, column) =>
                (mask & (1 << ((row / (SamplesPerCellRow / rows)) * 2 + column / 4))) != 0);

        string[] quadrants = [" ", "▘", "▝", "▀", "▖", "▌", "▞", "▛"];
        // Complementary masks have the same fit with swapped colours. Prefer the
        // existing shapes on ties, especially the half block for flat colours.
        foreach (var mask in new[] { 3, 1, 2, 4, 5, 6, 7 })
            AddParts(quadrants[mask], mask == 3 ? ImageGlyphSets.HalfBlocks : ImageGlyphSets.Quadrants, mask, 2);

        string[] lowerBlocks = [" ", "▁", "▂", "▃", "▄", "▅", "▆", "▇"];
        for (var eighths = 1; eighths < 8; eighths++)
        {
            if (eighths != 4) // Already represented by the upper half block.
                Add(lowerBlocks[eighths], ImageGlyphSets.Eighths,
                    (row, _) => row >= (8 - eighths) * (SamplesPerCellRow / 8));
        }
        // Unicode U+1FB00–U+1FB3B enumerates non-empty sextant masks, omitting
        // the existing left/right half blocks (21/42) and full block (63).
        var codePoint = 0x1FB00;
        for (var mask = 1; mask < 32; mask++)
        {
            if (mask == 21) continue;
            AddParts(char.ConvertFromUtf32(codePoint++), ImageGlyphSets.Sextants, mask, 3);
        }
        string[] leftBlocks = [" ", "▏", "▎", "▍", "▌", "▋", "▊", "▉"];
        for (var eighths = 1; eighths < 8; eighths++)
        {
            // Include the half even when quadrants are disabled.
            Add(leftBlocks[eighths], ImageGlyphSets.VerticalEighths, (_, column) => column < eighths);
        }
        // Unicode smooth mosaics U+1FB3C–U+1FB51 use endpoints on the cell
        // boundary. X is in halves, Y in thirds, matching their Unicode names.
        // Unlike geometric triangles, these are terminal block graphics.
        (int X1, int Y1, int X2, int Y2)[] diagonalEdges =
        [
            (0, 2, 1, 3), (0, 2, 2, 3), (0, 1, 1, 3), (0, 1, 2, 3),
            (0, 0, 1, 3), (0, 1, 1, 0), (0, 1, 2, 0), (0, 2, 1, 0),
            (0, 2, 2, 0), (0, 3, 1, 0), (0, 2, 2, 1), (1, 3, 2, 2),
            (0, 3, 2, 2), (1, 3, 2, 1), (0, 3, 2, 1), (1, 3, 2, 0),
            (1, 0, 2, 1), (0, 0, 2, 1), (1, 0, 2, 2), (0, 0, 2, 2),
            (1, 0, 2, 3), (0, 1, 2, 2)
        ];
        for (var index = 0; index < diagonalEdges.Length; index++)
        {
            var edge = diagonalEdges[index];
            var x1 = edge.X1 * SamplesPerCellColumn;
            var y1 = edge.Y1 * (SamplesPerCellRow * 2 / 3);
            var dx = (edge.X2 - edge.X1) * SamplesPerCellColumn;
            var dy = (edge.Y2 - edge.Y1) * (SamplesPerCellRow * 2 / 3);
            // Filled below the line; swapped colours cover U+1FB52–U+1FB67.
            // Doubled coordinates keep sample-centre comparisons integral.
            Add(char.ConvertFromUtf32(0x1FB3C + index), ImageGlyphSets.Diagonals,
                (row, column) => (2 * row + 1 - y1) * dx >= (2 * column + 1 - x1) * dy);
        }
        return glyphs.ToArray();
    }

    private sealed record ImageGlyph(string Text, ImageGlyphSets Set, int[] ForegroundSamples, int PaddingCount);

    private static CardDetailSpan FitImageCell(ReadOnlySpan<Rgb> pixels, Rgb? bottomPadding, ImageGlyphSets glyphSets)
    {
        var totalRed = 0;
        var totalGreen = 0;
        var totalBlue = 0;
        long squaredSamples = 0;
        foreach (var pixel in pixels)
        {
            totalRed += pixel.Red;
            totalGreen += pixel.Green;
            totalBlue += pixel.Blue;
            squaredSamples += pixel.Red * pixel.Red + pixel.Green * pixel.Green + pixel.Blue * pixel.Blue;
        }

        var bestError = long.MaxValue;
        var bestGlyph = "▀";
        var bestForeground = pixels[0];
        var bestBackground = pixels[SamplesPerCell / 2];
        foreach (var glyph in ImageGlyphs)
        {
            if (glyph.Set != ImageGlyphSets.HalfBlocks && (glyphSets & glyph.Set) == 0)
            {
                continue;
            }
            var coveredPadding = glyph.PaddingCount;
            if (bottomPadding is not null && coveredPadding != 0 && coveredPadding != SamplesPerCell / 2)
            {
                // Splitting the padding would force both colours to the pane background,
                // leaving no colour available to represent the image above it.
                continue;
            }

            var red = 0;
            var green = 0;
            var blue = 0;
            foreach (var index in glyph.ForegroundSamples)
            {
                red += pixels[index].Red;
                green += pixels[index].Green;
                blue += pixels[index].Blue;
            }
            var count = glyph.ForegroundSamples.Length;
            var backgroundCount = pixels.Length - count;
            var foreground = AverageColour(red, green, blue, count);
            var background = AverageColour(totalRed - red, totalGreen - green, totalBlue - blue, backgroundCount);
            if (bottomPadding is Rgb padding)
            {
                // Padding is outside the image, so its colour must be exact rather than
                // averaged with image samples that share this part of the glyph.
                if (coveredPadding == SamplesPerCell / 2)
                {
                    foreground = padding;
                }
                else
                {
                    background = padding;
                }
            }

            // Restore the full non-negative squared error before weighting it;
            // the common source term cannot be omitted with unequal weights.
            var error = squaredSamples + ColourError(foreground, red, green, blue, count)
                + ColourError(background, totalRed - red, totalGreen - green, totalBlue - blue, backgroundCount);
            // Thin vertical bands can look striped despite a marginally better
            // fit. Require a meaningful improvement (20% error penalty), while
            // preserving exact edges and the ordinary left half block.
            error *= glyph.Set == ImageGlyphSets.VerticalEighths && glyph.Text != "▌" ? 6 : 5;
            if (error < bestError)
            {
                bestError = error;
                bestGlyph = glyph.Text;
                bestForeground = foreground;
                bestBackground = background;
            }
        }

        return new CardDetailSpan(bestGlyph, bestForeground, bestBackground);
    }

    private static Rgb AverageColour(int red, int green, int blue, int count) =>
        new((byte)Math.Round(red / (double)count), (byte)Math.Round(green / (double)count),
            (byte)Math.Round(blue / (double)count));

    private static long ColourError(Rgb colour, int red, int green, int blue, int count) =>
        (long)count * ((colour.Red * colour.Red) + (colour.Green * colour.Green) + (colour.Blue * colour.Blue))
        - 2L * ((colour.Red * red) + (colour.Green * green) + (colour.Blue * blue));

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

    private readonly record struct RenderKey(int MaximumColumns, int MaximumRows, Rgb Background, ImageGlyphSets GlyphSets);
}
