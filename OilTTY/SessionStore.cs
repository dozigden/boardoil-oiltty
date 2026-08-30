using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record StoredSession(
    string Server,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    AuthUser User);

internal interface IBoardOilSessionStore
{
    Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(MachineAuthSession session, CancellationToken cancellationToken = default);

    Task DeleteAsync();
}

internal sealed class SessionStore : IBoardOilSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Uri _server;
    private readonly string _sessionPath;

    public SessionStore(Uri server)
    {
        _server = server;
        var serverKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormaliseServer(server))));
        _sessionPath = Path.Combine(ResolveConfigurationRoot(), "oiltty", "sessions", $"{serverKey}.json");
    }

    public async Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_sessionPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_sessionPath, cancellationToken);
            var session = JsonSerializer.Deserialize<StoredSession>(json, JsonOptions);
            if (session is null
                || !string.Equals(session.Server, NormaliseServer(_server), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(session.RefreshToken))
            {
                await DeleteAsync();
                return null;
            }

            if (session.RefreshTokenExpiresAtUtc <= DateTime.UtcNow)
            {
                await DeleteAsync();
                return null;
            }

            return session;
        }
        catch (JsonException)
        {
            await DeleteAsync();
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(MachineAuthSession session, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_sessionPath)!;
        Directory.CreateDirectory(directory);
        RestrictDirectory(Path.GetDirectoryName(directory)!);
        RestrictDirectory(directory);

        var storedSession = new StoredSession(
            NormaliseServer(_server),
            session.RefreshToken,
            session.RefreshTokenExpiresAtUtc,
            session.User);
        var json = JsonSerializer.Serialize(storedSession, JsonOptions);
        var temporaryPath = $"{_sessionPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            RestrictFile(temporaryPath);
            File.Move(temporaryPath, _sessionPath, overwrite: true);
            RestrictFile(_sessionPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task DeleteAsync()
    {
        if (File.Exists(_sessionPath))
        {
            File.Delete(_sessionPath);
        }

        return Task.CompletedTask;
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

    private static string NormaliseServer(Uri server) =>
        server.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/');

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
