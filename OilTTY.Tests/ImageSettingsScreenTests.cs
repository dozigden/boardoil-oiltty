using Xunit;

[Collection("Board palette")]
public sealed class ImageSettingsScreenTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"oiltty-image-ui-{Guid.NewGuid():N}");
    private static readonly TerminalViewport Viewport = new(80, 24);

    [Fact]
    public async Task TogglesPreviewWithoutSavingAndEscapeDiscards()
    {
        var store = new SettingsStore(_root);
        await store.SaveImageGlyphsAsync(ImageGlyphSets.Default, TestContext.Current.CancellationToken);
        var screen = new ImageSettingsScreen(ImageGlyphSets.Default);
        var before = screen.Render(Viewport).Canvas;
        screen.HandleKey(Key(ConsoleKey.Spacebar), Viewport);
        var after = screen.Render(Viewport).Canvas;

        Assert.Equal(ImageGlyphSets.Eighths | ImageGlyphSets.Sextants | ImageGlyphSets.VerticalEighths | ImageGlyphSets.Diagonals, screen.GlyphSets);
        Assert.Contains("[ ] Quadrants", Row(after, 4));
        Assert.Contains(Enumerable.Range(3, 17), y => Enumerable.Range(39, 39)
            .Any(x => before.CellAt(x, y) != after.CellAt(x, y)));
        var cancel = screen.HandleKey(Key(ConsoleKey.Escape), Viewport);
        Assert.True(cancel.IsComplete);
        Assert.Equal(ImageSettingsCommand.Cancel, cancel.Result);
        Assert.Equal(ImageGlyphSets.Default, await store.LoadImageGlyphsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveButtonPersistsAllFiveIndependentChoices()
    {
        var screen = new ImageSettingsScreen(ImageGlyphSets.HalfBlocks);
        for (var index = 0; index < 5; index++)
        {
            screen.HandleKey(Key(ConsoleKey.Spacebar), Viewport);
            screen.HandleKey(Key(ConsoleKey.DownArrow), Viewport);
        }
        var save = screen.HandleKey(Key(ConsoleKey.Enter), Viewport);
        Assert.True(save.IsComplete);
        Assert.Equal(ImageSettingsCommand.Save, save.Result);
        var store = new SettingsStore(_root);
        Assert.True(await screen.SaveAsync(store, TestContext.Current.CancellationToken));
        Assert.Equal(ImageGlyphSets.All, await store.LoadImageGlyphsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedSaveKeepsChoicesAndAllowsRetry()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "oiltty");
        await File.WriteAllTextAsync(blocker, "blocked", TestContext.Current.CancellationToken);
        var screen = new ImageSettingsScreen(ImageGlyphSets.Sextants);
        var store = new SettingsStore(_root);
        Assert.False(await screen.SaveAsync(store, TestContext.Current.CancellationToken));
        Assert.Equal(ImageGlyphSets.Sextants, screen.GlyphSets);
        Assert.Contains("Save failed:", Row(screen.Render(Viewport).Canvas, 21));
        File.Delete(blocker);
        Assert.True(await screen.SaveAsync(store, TestContext.Current.CancellationToken));
        Assert.Equal(ImageGlyphSets.Sextants, await store.LoadImageGlyphsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ShortcutsSaveCancelAndQuitWithoutChangingChoices()
    {
        var screen = new ImageSettingsScreen(ImageGlyphSets.Default);
        Assert.Equal(ImageSettingsCommand.Save, screen.HandleKey(Key(ConsoleKey.S, control: true), Viewport).Result);
        Assert.Equal(ImageSettingsCommand.Quit, screen.HandleKey(Key(ConsoleKey.C, control: true), Viewport).Result);
        screen.HandleKey(Key(ConsoleKey.Tab, shift: true), Viewport);
        Assert.Equal(ImageSettingsCommand.Cancel, screen.HandleKey(Key(ConsoleKey.Enter), Viewport).Result);
        Assert.Equal(ImageGlyphSets.Default, screen.GlyphSets);
    }

    [Theory]
    [InlineData(40, 12, false)]
    [InlineData(40, 24, true)]
    [InlineData(80, 24, false)]
    [InlineData(120, 40, true)]
    public void LayoutKeepsControlsAndPreviewClearInBothThemes(int width, int height, bool light)
    {
        try
        {
            BoardStyles.UseTheme(light ? OilTTYTheme.Light : OilTTYTheme.Dark);
            var screen = new ImageSettingsScreen(ImageGlyphSets.All);
            var canvas = screen.Render(new TerminalViewport(width, height)).Canvas;
            Assert.Equal(width, canvas.Width);
            Assert.Equal(height, canvas.Height);
            Assert.Contains("SAMPLE PREVIEW", Row(canvas, 2));
            Assert.Contains("[x] Quadrants", Row(canvas, height < 22 ? 3 : 4));
            Assert.Contains("[x] Eighths", Row(canvas, height < 22 ? 4 : 6));
            Assert.Contains("[x] Sextants", Row(canvas, height < 22 ? 5 : 8));
            Assert.Contains("[x] Diagonals", Row(canvas, height < 22 ? 7 : 12));
            Assert.Contains(width < 54 ? "[x] V-eighths" : "[x] Vertical eighths", Row(canvas, height < 22 ? 6 : 10));
            Assert.Contains(height < 22 ? "Save" : "[ Save ]", Row(canvas, height < 22 ? 8 : 16));
            Assert.Contains(height < 22 ? "Cancel" : "[ Cancel ]", Row(canvas, height < 22 ? 8 : 18));
            Assert.Contains("space toggle", Row(canvas, height - 1));
            Assert.All(Enumerable.Range(0, width), x => Assert.Equal("─", canvas.CellAt(x, height - 2).Grapheme));
            Assert.Equal(BoardStyles.RootBackground, canvas.CellAt(0, 3).Background);
        }
        finally { BoardStyles.UseTheme(OilTTYTheme.Dark); }
    }

    [Fact]
    public void ProvidedCardImageIsUsedForPreview()
    {
        var colour = new Rgb(234, 12, 34);
        var thumbnail = new CardDescriptionThumbnail(1, 2, [234, 12, 34, 255, 234, 12, 34, 255]);
        var screen = new ImageSettingsScreen(ImageGlyphSets.Default, thumbnail);
        var canvas = screen.Render(Viewport).Canvas;
        Assert.Contains("CARD PREVIEW", Row(canvas, 2));
        Assert.Contains(Enumerable.Range(3, 17), y => Enumerable.Range(39, 39)
            .Any(x => canvas.CellAt(x, y).Foreground == colour));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, bool shift = false, bool control = false) =>
        new('\0', key, shift, false, control);
    private static string Row(TerminalCanvas canvas, int y) => string.Concat(
        Enumerable.Range(0, canvas.Width).Select(x => canvas.CellAt(x, y)).Where(cell => !cell.Continuation).Select(cell => cell.Grapheme));
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
