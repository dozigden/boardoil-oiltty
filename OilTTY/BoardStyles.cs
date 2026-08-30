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

internal enum OilTTYTheme
{
    Dark,
    Light
}

internal sealed record BoardPalette(
    Rgb RootBackground,
    Rgb PanelBackground,
    Rgb CardBackground,
    Rgb TextStrong,
    Rgb TextMuted,
    Rgb BorderSoft,
    Rgb Selection,
    Rgb Connected,
    Rgb Danger,
    Rgb TagAutoBackground,
    Rgb TagAutoText,
    Rgb TagAutoBorder,
    IReadOnlyList<Rgb> Presets,
    double SlickSurfaceAmount);

internal static class BoardStyles
{
    private static readonly Rgb MixDark = new(17, 24, 39);
    private static readonly Rgb MixLight = new(255, 255, 255);

    private static readonly BoardPalette DarkPalette = new(
        RootBackground: new(16, 23, 34),
        PanelBackground: new(22, 29, 39),
        CardBackground: new(30, 38, 51),
        TextStrong: new(233, 238, 246),
        TextMuted: new(147, 161, 186),
        BorderSoft: new(63, 76, 99),
        Selection: new(202, 184, 239),
        Connected: new(50, 205, 160),
        Danger: new(255, 159, 164),
        TagAutoBackground: new(42, 34, 56),
        TagAutoText: new(239, 231, 255),
        TagAutoBorder: new(94, 77, 118),
        Presets:
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
        ],
        SlickSurfaceAmount: 0.12);

    private static readonly BoardPalette LightPalette = new(
        RootBackground: new(255, 255, 255),
        PanelBackground: new(237, 242, 250),
        CardBackground: new(255, 255, 255),
        TextStrong: new(31, 41, 55),
        TextMuted: new(111, 131, 160),
        BorderSoft: new(201, 211, 227),
        Selection: new(91, 37, 148),
        Connected: new(23, 97, 61),
        Danger: new(159, 43, 43),
        TagAutoBackground: new(241, 235, 251),
        TagAutoText: new(43, 18, 71),
        TagAutoBorder: new(216, 205, 236),
        Presets:
        [
            new(95, 59, 138),
            new(217, 130, 184),
            new(79, 158, 174),
            new(213, 168, 77),
            new(169, 59, 73),
            new(127, 165, 224),
            new(226, 116, 61),
            new(59, 147, 104),
            new(145, 182, 74),
            new(194, 184, 177),
            new(230, 212, 90),
            new(81, 89, 102)
        ],
        SlickSurfaceAmount: 0.10);

    private static BoardPalette _palette = DarkPalette;
    private static OilTTYTheme _theme = OilTTYTheme.Dark;

    public static Rgb RootBackground => _palette.RootBackground;
    public static Rgb PanelBackground => _palette.PanelBackground;
    public static Rgb CardBackground => _palette.CardBackground;
    public static Rgb TextStrong => _palette.TextStrong;
    public static Rgb TextMuted => _palette.TextMuted;
    public static Rgb BorderSoft => _palette.BorderSoft;
    public static Rgb Selection => _palette.Selection;
    public static Rgb CardShadow => Mix(RootBackground, Selection, 0.22);
    public static Rgb FieldAnchorPlaceholder => Mix(BorderSoft, Selection, 0.42);
    public static Rgb ScrollIndicator => Mix(BorderSoft, Selection, 0.4);
    public static Rgb Connected => _palette.Connected;
    public static Rgb Danger => _palette.Danger;
    public static Rgb InputActiveBackground => Mix(RootBackground, Selection, 0.18);
    public static OilTTYTheme Theme => _theme;

    public static void UseTheme(OilTTYTheme theme)
    {
        _theme = theme;
        _palette = PaletteFor(theme);
    }

    public static bool TryToggleTheme(ConsoleKeyInfo key)
    {
        if (key.Key != ConsoleKey.T
            || !key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return false;
        }

        UseTheme(_theme == OilTTYTheme.Dark ? OilTTYTheme.Light : OilTTYTheme.Dark);
        return true;
    }

    public static BoardPalette PaletteFor(OilTTYTheme theme) =>
        theme == OilTTYTheme.Light ? LightPalette : DarkPalette;

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
            new SurfaceStyle(
                _palette.TagAutoBackground,
                _palette.TagAutoBackground,
                _palette.TagAutoText,
                _palette.TagAutoBorder),
            isTag: true);

    public static Rgb ResolveSlick(SlickDefinition? slick, int slickId)
    {
        if (slick is null)
        {
            return PresetAt(slickId);
        }

        if (slick.StyleName.Equals("presets", StringComparison.OrdinalIgnoreCase)
            && TryReadProperties(slick.StylePropertiesJson, out var presetProperties))
        {
            using (presetProperties)
            {
                var presetIndex = ReadPresetIndex(presetProperties.RootElement);
                return Mix(_palette.Presets[presetIndex], CardBackground, _palette.SlickSurfaceAmount);
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

        return PresetAt(slickId);
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
                var presetIndex = ReadPresetIndex(root);
                var presetBackground = _palette.Presets[presetIndex];
                var presetBorder = isTag
                    ? Mix(presetBackground, BorderSoft, 0.45)
                    : PresetCardBorder(presetBackground, presetIndex);
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
                : Mix(left, isTag ? _palette.TagAutoBorder : BorderSoft, 0.45);
            return new SurfaceStyle(left, right, foreground, border, showBorder);
        }
    }

    private static SurfaceStyle DefaultCard() =>
        new(CardBackground, CardBackground, TextStrong, BorderSoft);

    private static Rgb PresetAt(int id) =>
        _palette.Presets[Math.Abs(id) % _palette.Presets.Count];

    private static Rgb PresetCardBorder(Rgb background, int presetIndex)
    {
        if (_theme == OilTTYTheme.Dark)
        {
            return Mix(background, MixDark, 0.28);
        }

        return presetIndex is 0 or 4 or 11
            ? Mix(background, MixLight, 0.40)
            : Mix(background, MixDark, 0.50);
    }

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
            || index >= _palette.Presets.Count)
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
