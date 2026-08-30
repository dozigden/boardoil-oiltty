using System.Globalization;
using System.Text.Json;

internal readonly record struct SurfaceStyle(
    Rgb LeftBackground,
    Rgb RightBackground,
    Rgb Foreground,
    Rgb Border,
    bool ShowBorder = true)
{
    public Rgb BackgroundAt(int offset, int width)
    {
        if (width <= 1 || LeftBackground == RightBackground)
        {
            return LeftBackground;
        }

        var amount = Math.Clamp(offset / (double)(width - 1), 0, 1);
        return BoardStyles.Mix(LeftBackground, RightBackground, amount);
    }
}

internal static class BoardStyles
{
    public static readonly Rgb RootBackground = new(16, 23, 34);
    public static readonly Rgb PanelBackground = new(22, 29, 39);
    public static readonly Rgb CardBackground = new(30, 38, 51);
    public static readonly Rgb TextStrong = new(233, 238, 246);
    public static readonly Rgb TextMuted = new(147, 161, 186);
    public static readonly Rgb BorderSoft = new(63, 76, 99);
    public static readonly Rgb Selection = new(202, 184, 239);
    public static readonly Rgb CardShadow = Mix(RootBackground, Selection, 0.22);
    public static readonly Rgb FieldAnchorPlaceholder = Mix(BorderSoft, Selection, 0.42);
    public static readonly Rgb ScrollIndicator = Mix(BorderSoft, Selection, 0.4);
    public static readonly Rgb Connected = new(50, 205, 160);
    public static readonly Rgb Danger = new(255, 159, 164);
    public static readonly Rgb InputActiveBackground = Mix(RootBackground, Selection, 0.18);

    private static readonly Rgb MixDark = new(17, 24, 39);
    private static readonly Rgb TagAutoBackground = new(42, 34, 56);
    private static readonly Rgb TagAutoText = new(239, 231, 255);
    private static readonly Rgb TagAutoBorder = new(94, 77, 118);

    private static readonly Rgb[] Presets =
    [
        new(89, 65, 127),
        new(141, 83, 119),
        new(40, 107, 120),
        new(115, 93, 50),
        new(130, 59, 71),
        new(59, 92, 148),
        new(133, 75, 50),
        new(40, 105, 76),
        new(95, 119, 48),
        new(111, 102, 96),
        new(117, 107, 45),
        new(65, 74, 87)
    ];

    public static SurfaceStyle ResolveCard(CardTypeDefinition? cardType)
    {
        if (cardType is null)
        {
            return DefaultCard();
        }

        return ResolveSurface(cardType.StyleName, cardType.StylePropertiesJson, DefaultCard(), isTag: false);
    }

    public static SurfaceStyle ResolveTag(CardTag tag) =>
        ResolveSurface(
            tag.StyleName,
            tag.StylePropertiesJson,
            new SurfaceStyle(TagAutoBackground, TagAutoBackground, TagAutoText, TagAutoBorder),
            isTag: true);

    public static Rgb ResolveSlick(SlickDefinition? slick, int slickId)
    {
        if (slick is null)
        {
            return Presets[Math.Abs(slickId) % Presets.Length];
        }

        if (slick.StyleName.Equals("presets", StringComparison.OrdinalIgnoreCase)
            && TryReadProperties(slick.StylePropertiesJson, out var presetProperties))
        {
            using (presetProperties)
            {
                var presetIndex = ReadPresetIndex(presetProperties.RootElement);
                return Mix(Presets[presetIndex], CardBackground, 0.12);
            }
        }

        if (slick.StyleName.Equals("solid", StringComparison.OrdinalIgnoreCase)
            && TryReadProperties(slick.StylePropertiesJson, out var solidProperties)
            )
        {
            using (solidProperties)
            {
                if (TryReadColour(solidProperties.RootElement, "backgroundColor", out var solidColour))
                {
                    return solidColour;
                }
            }
        }

        return Presets[Math.Abs(slickId) % Presets.Length];
    }

    public static Rgb Mix(Rgb left, Rgb right, double rightAmount)
    {
        var amount = Math.Clamp(rightAmount, 0, 1);
        return new Rgb(
            (byte)Math.Round(left.Red + ((right.Red - left.Red) * amount)),
            (byte)Math.Round(left.Green + ((right.Green - left.Green) * amount)),
            (byte)Math.Round(left.Blue + ((right.Blue - left.Blue) * amount)));
    }

    private static SurfaceStyle ResolveSurface(
        string styleName,
        string stylePropertiesJson,
        SurfaceStyle fallback,
        bool isTag)
    {
        if (styleName.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        if (!TryReadProperties(stylePropertiesJson, out var properties))
        {
            return fallback;
        }

        using (properties)
        {
            var root = properties.RootElement;
            if (styleName.Equals("presets", StringComparison.OrdinalIgnoreCase))
            {
                var presetBackground = Presets[ReadPresetIndex(root)];
                var presetBorder = isTag
                    ? Mix(presetBackground, BorderSoft, 0.45)
                    : Mix(presetBackground, MixDark, 0.28);
                return new SurfaceStyle(
                    presetBackground,
                    presetBackground,
                    AutoText(presetBackground),
                    presetBorder);
            }

            Rgb left;
            Rgb right;
            if (styleName.Equals("solid", StringComparison.OrdinalIgnoreCase)
                && TryReadColour(root, "backgroundColor", out var background))
            {
                left = background;
                right = background;
            }
            else if (styleName.Equals("gradient", StringComparison.OrdinalIgnoreCase)
                     && TryReadColour(root, "leftColor", out left)
                     && TryReadColour(root, "rightColor", out right))
            {
            }
            else
            {
                return fallback;
            }

            var foreground = ReadMode(root, "textColorMode") == "custom"
                             && TryReadColour(root, "textColor", out var customText)
                ? customText
                : AutoText(left);

            var borderMode = ReadMode(root, "borderMode");
            var showBorder = borderMode != "none";
            var border = borderMode == "custom"
                         && TryReadColour(root, "borderColor", out var customBorder)
                ? customBorder
                : Mix(left, isTag ? TagAutoBorder : BorderSoft, 0.45);
            return new SurfaceStyle(left, right, foreground, border, showBorder);
        }
    }

    private static SurfaceStyle DefaultCard() =>
        new(CardBackground, CardBackground, TextStrong, BorderSoft);

    private static Rgb AutoText(Rgb background)
    {
        var brightness = ((background.Red * 299) + (background.Green * 587) + (background.Blue * 114)) / 1000;
        return brightness >= 150 ? new Rgb(17, 24, 39) : new Rgb(255, 255, 255);
    }

    private static int ReadPresetIndex(JsonElement root)
    {
        if (!root.TryGetProperty("presetIndex", out var element)
            || !element.TryGetInt32(out var index)
            || index < 0
            || index >= Presets.Length)
        {
            return 2;
        }

        return index;
    }

    private static string? ReadMode(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool TryReadProperties(string json, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
            document = null!;
            return false;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private static bool TryReadColour(JsonElement root, string propertyName, out Rgb colour)
    {
        colour = default;
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = element.GetString();
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        return byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
               && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
               && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue)
               && SetColour(red, green, blue, out colour);
    }

    private static bool SetColour(byte red, byte green, byte blue, out Rgb colour)
    {
        colour = new Rgb(red, green, blue);
        return true;
    }
}
