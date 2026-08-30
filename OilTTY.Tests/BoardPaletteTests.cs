using Xunit;

[CollectionDefinition("Board palette", DisableParallelization = true)]
public sealed class BoardPaletteCollection;

[Collection("Board palette")]
public sealed class BoardPaletteTests
{
    [Fact]
    public void LightPalette_MatchesBoardOilThemeTokens()
    {
        var palette = BoardStyles.PaletteFor(OilTTYTheme.Light);

        Assert.Equal(new Rgb(255, 255, 255), palette.RootBackground);
        Assert.Equal(new Rgb(255, 255, 255), palette.CardBackground);
        Assert.Equal(new Rgb(31, 41, 55), palette.TextStrong);
        Assert.Equal(new Rgb(201, 211, 227), palette.BorderSoft);
        Assert.Equal(new Rgb(91, 37, 148), palette.Selection);
        Assert.Equal(new Rgb(241, 235, 251), palette.TagAutoBackground);
        Assert.Equal(new Rgb(95, 59, 138), palette.Presets[0]);
        Assert.Equal(new Rgb(81, 89, 102), palette.Presets[11]);
    }

    [Fact]
    public void LightTheme_FlowsIntoLoginAndPresetCardRendering()
    {
        var palette = BoardStyles.PaletteFor(OilTTYTheme.Light);
        try
        {
            BoardStyles.UseTheme(OilTTYTheme.Light);

            var login = new LoginApplication(initialServer: null, new TerminalRuntime())
                .Render(new TerminalViewport(80, 24))
                .Canvas;
            var cardType = new CardTypeDefinition(
                1,
                "Feature",
                "🎬",
                "presets",
                "{\"presetIndex\":0}");
            var cardStyle = BoardStyles.ResolveCard(cardType);

            Assert.Equal(palette.RootBackground, login.CellAt(0, 0).Background);
            Assert.Equal(palette.TextStrong, login.CellAt(30, 6).Foreground);
            Assert.Equal(palette.Presets[0], cardStyle.LeftBackground);
        }
        finally
        {
            BoardStyles.UseTheme(OilTTYTheme.Dark);
        }
    }

    [Fact]
    public void ControlT_TogglesThemeFromLoginWithoutEnteringText()
    {
        try
        {
            BoardStyles.UseTheme(OilTTYTheme.Dark);
            var screen = new LoginApplication(initialServer: null, new TerminalRuntime());
            var viewport = new TerminalViewport(80, 24);
            var key = new ConsoleKeyInfo(
                '\u0014',
                ConsoleKey.T,
                shift: false,
                alt: false,
                control: true);

            var update = screen.HandleKey(key, viewport);
            var lightFrame = screen.Render(viewport);

            Assert.True(update.Redraw);
            Assert.Equal(OilTTYTheme.Light, BoardStyles.Theme);
            Assert.Equal(
                BoardStyles.PaletteFor(OilTTYTheme.Light).RootBackground,
                lightFrame.Canvas.CellAt(0, 0).Background);

            screen.HandleKey(key, viewport);
            Assert.Equal(OilTTYTheme.Dark, BoardStyles.Theme);
        }
        finally
        {
            BoardStyles.UseTheme(OilTTYTheme.Dark);
        }
    }

    [Fact]
    public void BoardPickerSelectionBackgroundChangesWithTheme()
    {
        try
        {
            BoardStyles.UseTheme(OilTTYTheme.Dark);
            var renderer = new BoardPickerRenderer();
            BoardSummary[] boards = [new(1, "Board", "")];
            var darkBackground = renderer.Render(boards, 0, 1, 80, 24, "connected")
                .Canvas
                .CellAt(4, 9)
                .Background;

            BoardStyles.UseTheme(OilTTYTheme.Light);
            var lightBackground = renderer.Render(boards, 0, 1, 80, 24, "connected")
                .Canvas
                .CellAt(4, 9)
                .Background;

            Assert.NotEqual(darkBackground, lightBackground);
            Assert.Equal(BoardStyles.InputActiveBackground, lightBackground);
        }
        finally
        {
            BoardStyles.UseTheme(OilTTYTheme.Dark);
        }
    }
}
