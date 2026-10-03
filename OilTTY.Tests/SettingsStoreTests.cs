using Xunit;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"oiltty-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task Resolve_UsesOverrideThenSavedSettingThenDefault()
    {
        var token = TestContext.Current.CancellationToken;
        var store = new SettingsStore(_root);
        Assert.Null(await store.LoadImageGlyphsAsync(token));
        Assert.Equal(ImageGlyphSets.Default, await store.ResolveImageGlyphsAsync(null, token));
        Assert.False(Directory.Exists(_root));

        await store.SaveImageGlyphsAsync(ImageGlyphSets.All, token);
        Assert.Equal(ImageGlyphSets.All, await new SettingsStore(_root).ResolveImageGlyphsAsync(null, token));
        Assert.Equal(ImageGlyphSets.HalfBlocks, await store.ResolveImageGlyphsAsync(ImageGlyphSets.HalfBlocks, token));
        Assert.Equal(ImageGlyphSets.All, await store.LoadImageGlyphsAsync(token));
    }

    [Fact]
    public async Task Save_PreservesOtherSettingsAndExistingServer()
    {
        var token = TestContext.Current.CancellationToken;
        var store = new SettingsStore(_root);
        var directory = Path.GetDirectoryName(store.FilePath)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(store.FilePath, """{"futureSetting":{"value":7}}""", token);
        var server = new ServerStore(_root);
        await server.SaveAsync(new Uri("https://boardoil.test/"), token);

        await store.SaveImageGlyphsAsync(ImageGlyphSets.Sextants, token);
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(store.FilePath, token))!;
        Assert.Equal(7, json["futureSetting"]!["value"]!.GetValue<int>());
        Assert.Equal("sextants", json["imageGlyphs"]!.GetValue<string>());
        Assert.Equal(new Uri("https://boardoil.test/"), await server.LoadAsync(token));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"imageGlyphs\":null}")]
    public async Task Resolve_MissingSettingUsesDefault(string json)
    {
        var token = TestContext.Current.CancellationToken;
        var store = await WriteSettingsAsync(json);
        Assert.Equal(ImageGlyphSets.Default, await store.ResolveImageGlyphsAsync(null, token));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"imageGlyphs\":\"octants\"}")]
    [InlineData("{\"imageGlyphs\":[]}")]
    [InlineData("{\"imageGlyphs\":7}")]
    public async Task Resolve_InvalidSettingsIdentifyTheFileButAllowOverride(string json)
    {
        var token = TestContext.Current.CancellationToken;
        var store = await WriteSettingsAsync(json);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.ResolveImageGlyphsAsync(null, token));
        Assert.Contains(store.FilePath, error.Message);
        Assert.Equal(ImageGlyphSets.HalfBlocks, await store.ResolveImageGlyphsAsync(ImageGlyphSets.HalfBlocks, token));
        Assert.Equal(json, await File.ReadAllTextAsync(store.FilePath, token));
    }

    [Fact]
    public async Task Save_CancellationLeavesPreviousSettingsIntact()
    {
        var token = TestContext.Current.CancellationToken;
        var store = new SettingsStore(_root);
        await store.SaveImageGlyphsAsync(ImageGlyphSets.All, token);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveImageGlyphsAsync(ImageGlyphSets.HalfBlocks, cancellation.Token));
        Assert.Equal(ImageGlyphSets.All, await store.LoadImageGlyphsAsync(token));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
    }

    private async Task<SettingsStore> WriteSettingsAsync(string json)
    {
        var store = new SettingsStore(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        await File.WriteAllTextAsync(store.FilePath, json, TestContext.Current.CancellationToken);
        return store;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
