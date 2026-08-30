using Xunit;

public sealed class ServerStoreTests
{
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsTheLastServer()
    {
        var configurationRoot = Path.Combine(
            Path.GetTempPath(),
            $"oiltty-server-store-{Guid.NewGuid():N}");
        try
        {
            var store = new ServerStore(configurationRoot);

            Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
            await store.SaveAsync(
                new Uri("https://boardoil.test/team/"),
                TestContext.Current.CancellationToken);

            var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new Uri("https://boardoil.test/team/"), loaded);
        }
        finally
        {
            if (Directory.Exists(configurationRoot))
            {
                Directory.Delete(configurationRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_IgnoresAnInvalidStoredValue()
    {
        var configurationRoot = Path.Combine(
            Path.GetTempPath(),
            $"oiltty-server-store-{Guid.NewGuid():N}");
        var directory = Path.Combine(configurationRoot, "oiltty");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "server"),
            "not a URL",
            TestContext.Current.CancellationToken);
        try
        {
            var store = new ServerStore(configurationRoot);

            Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(configurationRoot, recursive: true);
        }
    }
}
