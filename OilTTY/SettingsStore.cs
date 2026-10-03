using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class SettingsStore
{
    public SettingsStore(string? configurationRoot = null)
    {
        FilePath = Path.Combine(configurationRoot ?? ServerStore.ResolveConfigurationRoot(), "oiltty", "settings.json");
    }

    public string FilePath { get; }

    public async Task<ImageGlyphSets> ResolveImageGlyphsAsync(
        ImageGlyphSets? overrideSets,
        CancellationToken cancellationToken = default) =>
        overrideSets ?? await LoadImageGlyphsAsync(cancellationToken) ?? ImageGlyphSets.Default;

    public async Task<ImageGlyphSets?> LoadImageGlyphsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await ReadAsync(cancellationToken);
        if (settings["imageGlyphs"] is not JsonNode value)
        {
            return null;
        }
        try
        {
            return ImageGlyphSettings.Parse(value.GetValue<string>());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException($"Invalid imageGlyphs in {FilePath}: {exception.Message}", exception);
        }
    }

    public async Task SaveImageGlyphsAsync(ImageGlyphSets sets, CancellationToken cancellationToken = default)
    {
        var value = ImageGlyphSettings.Format(sets);
        var settings = await ReadAsync(cancellationToken);
        settings["imageGlyphs"] = value;
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath,
                settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
                cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task<JsonObject> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return new JsonObject();
        }
        try
        {
            var json = await File.ReadAllTextAsync(FilePath, cancellationToken);
            return JsonNode.Parse(json) as JsonObject
                ?? throw new JsonException("Settings must be a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid settings file {FilePath}: {exception.Message}", exception);
        }
    }
}
