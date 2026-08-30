using Xunit;

[CollectionDefinition("App options environment", DisableParallelization = true)]
public sealed class AppOptionsEnvironmentCollection;

[Collection("App options environment")]
public sealed class AppOptionsTests
{
    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Parse_RecognisesHelpWithoutRequiringConfiguration(string argument)
    {
        var options = AppOptions.Parse([argument]);

        Assert.True(options.ShowHelp);
        Assert.Null(options.Server);
    }

    [Fact]
    public void Help_ListsEnvironmentEquivalents()
    {
        Assert.StartsWith("Usage: OilTTY [options]", AppOptions.HelpText);
        Assert.Contains("BOARDOIL_URL", AppOptions.HelpText);
        Assert.Contains("BOARDOIL_BOARD_ID", AppOptions.HelpText);
        Assert.Contains("OILTTY_THEME", AppOptions.HelpText);
        Assert.Contains("BOARDOIL_API_TOKEN", AppOptions.HelpText);
        Assert.Contains("BOARDOIL_USERNAME", AppOptions.HelpText);
        Assert.Contains("BOARDOIL_PASSWORD", AppOptions.HelpText);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void Parse_RejectsInvalidBoardIdEnvironmentValue(string value)
    {
        var previous = Environment.GetEnvironmentVariable("BOARDOIL_BOARD_ID");
        try
        {
            Environment.SetEnvironmentVariable("BOARDOIL_BOARD_ID", value);

            var exception = Assert.Throws<ArgumentException>(() => AppOptions.Parse([]));

            Assert.Equal("BOARDOIL_BOARD_ID must be a positive integer.", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BOARDOIL_BOARD_ID", previous);
        }
    }

    [Fact]
    public void Parse_CommandLineBoardOverridesInvalidEnvironmentValue()
    {
        var previous = Environment.GetEnvironmentVariable("BOARDOIL_BOARD_ID");
        try
        {
            Environment.SetEnvironmentVariable("BOARDOIL_BOARD_ID", "invalid");

            var options = AppOptions.Parse(["--board", "7"]);

            Assert.Equal(7, options.BoardId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BOARDOIL_BOARD_ID", previous);
        }
    }

    [Fact]
    public void Parse_HelpIgnoresInvalidEnvironmentValue()
    {
        var previous = Environment.GetEnvironmentVariable("BOARDOIL_BOARD_ID");
        try
        {
            Environment.SetEnvironmentVariable("BOARDOIL_BOARD_ID", "invalid");

            var options = AppOptions.Parse(["--help"]);

            Assert.True(options.ShowHelp);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BOARDOIL_BOARD_ID", previous);
        }
    }

    [Theory]
    [InlineData("light", true)]
    [InlineData("LIGHT", true)]
    [InlineData("dark", false)]
    public void Parse_UsesRequestedTheme(string value, bool expectedLight)
    {
        var options = AppOptions.Parse(["--theme", value]);

        Assert.Equal(expectedLight ? OilTTYTheme.Light : OilTTYTheme.Dark, options.Theme);
    }

    [Fact]
    public void Parse_RejectsUnknownTheme()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => AppOptions.Parse(["--theme", "sepia"]));

        Assert.Equal("Theme must be 'light' or 'dark'.", exception.Message);
    }
}
