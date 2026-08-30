using System.Text;

internal sealed class ServerStore
{
    private readonly string _serverPath;

    public ServerStore(string? configurationRoot = null)
    {
        var root = configurationRoot ?? ResolveConfigurationRoot();
        _serverPath = Path.Combine(root, "oiltty", "server");
    }

    public async Task<Uri?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_serverPath))
        {
            return null;
        }

        try
        {
            var value = await File.ReadAllTextAsync(_serverPath, cancellationToken);
            return AppOptions.ParseServer(value.Trim());
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(Uri server, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_serverPath)!;
        Directory.CreateDirectory(directory);
        RestrictDirectory(directory);

        var value = server.AbsoluteUri;
        var temporaryPath = $"{_serverPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                value,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            RestrictFile(temporaryPath);
            File.Move(temporaryPath, _serverPath, overwrite: true);
            RestrictFile(_serverPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ResolveConfigurationRoot()
    {
        var xdgConfigurationHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigurationHome))
        {
            return Path.GetFullPath(xdgConfigurationHome);
        }

        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(applicationData))
        {
            return applicationData;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("The user configuration directory could not be resolved.");
        }

        return Path.Combine(userProfile, ".config");
    }

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
