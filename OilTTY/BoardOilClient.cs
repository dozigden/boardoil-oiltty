using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

internal sealed class BoardOilClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthenticatedBoardOilTransport _transport;

    internal BoardOilClient(AuthenticatedBoardOilTransport transport)
    {
        _transport = transport;
    }

    public string IdentityLabel => _transport.IdentityLabel;

    public Uri Server => _transport.Server;

    public static async Task<BoardOilClient> ConnectAsync(
        AppOptions options,
        Func<string?, string?, Task<LoginCredentials>>? credentialProvider = null,
        ServerStore? serverStore = null)
    {
        var accessToken = Environment.GetEnvironmentVariable("BOARDOIL_API_TOKEN");
        var userName = Environment.GetEnvironmentVariable("BOARDOIL_USERNAME");
        var password = Environment.GetEnvironmentVariable("BOARDOIL_PASSWORD");
        string? loginMessage = null;

        if (options.Server is Uri initialServer)
        {
            var sessionStore = options.NoSession ? null : new SessionStore(initialServer);
            var client = Create(initialServer, sessionStore);
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                client._transport.UseApiToken(accessToken.Trim());
                return client;
            }

            if (sessionStore is not null)
            {
                var storedSession = await sessionStore.LoadAsync();
                if (storedSession is not null)
                {
                    try
                    {
                        await client._transport.ResumeAsync(storedSession);
                        if (serverStore is not null)
                        {
                            await serverStore.SaveAsync(initialServer);
                        }

                        return client;
                    }
                    catch (BoardOilRequestException exception)
                        when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                    {
                        await sessionStore.DeleteAsync();
                        loginMessage = "Saved BoardOil session expired; sign in again.";
                    }
                    catch (Exception exception) when (IsRecoverableLoginException(exception))
                    {
                        loginMessage = LoginErrorMessage(exception);
                    }
                    catch
                    {
                        await client.DisposeAsync();
                        throw;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrEmpty(password))
            {
                try
                {
                    await client._transport.LoginAsync(userName.Trim(), password);
                    if (serverStore is not null)
                    {
                        await serverStore.SaveAsync(initialServer);
                    }

                    return client;
                }
                catch (Exception exception)
                    when (credentialProvider is not null && IsRecoverableLoginException(exception))
                {
                    loginMessage = LoginErrorMessage(exception);
                }
                catch
                {
                    await client.DisposeAsync();
                    throw;
                }
            }

            await client.DisposeAsync();
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "Set BOARDOIL_URL or --server when using BOARDOIL_API_TOKEN.");
        }

        if (Console.IsInputRedirected
            && (options.Server is null
                || string.IsNullOrWhiteSpace(userName)
                || string.IsNullOrEmpty(password)))
        {
            throw new InvalidOperationException(
                "Set BOARDOIL_URL (or --server) and provide BOARDOIL_API_TOKEN, or BOARDOIL_USERNAME and BOARDOIL_PASSWORD, when input is redirected.");
        }

        if (credentialProvider is not null)
        {
            while (true)
            {
                var credentials = await credentialProvider(userName, loginMessage);
                userName = credentials.UserName;
                if (serverStore is not null)
                {
                    await serverStore.SaveAsync(credentials.Server);
                }

                var sessionStore = options.NoSession ? null : new SessionStore(credentials.Server);
                var client = Create(credentials.Server, sessionStore);
                try
                {
                    await client._transport.LoginAsync(credentials.UserName, credentials.Password);
                    return client;
                }
                catch (Exception exception) when (IsRecoverableLoginException(exception))
                {
                    loginMessage = LoginErrorMessage(exception);
                }
                catch
                {
                    await client.DisposeAsync();
                    throw;
                }

                await client.DisposeAsync();
            }
        }

        if (options.Server is not Uri server)
        {
            throw new InvalidOperationException("Set BOARDOIL_URL or --server.");
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.Write("BoardOil username: ");
            userName = Console.ReadLine();
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("A BoardOil username is required.");
        }

        if (string.IsNullOrEmpty(password))
        {
            Console.Write("BoardOil password: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        var fallbackSessionStore = options.NoSession ? null : new SessionStore(server);
        var fallbackClient = Create(server, fallbackSessionStore);
        try
        {
            await fallbackClient._transport.LoginAsync(userName.Trim(), password);
            if (serverStore is not null)
            {
                await serverStore.SaveAsync(server);
            }

            return fallbackClient;
        }
        catch
        {
            await fallbackClient.DisposeAsync();
            throw;
        }
    }

    public static async Task<string> LogoutStoredSessionAsync(AppOptions options)
    {
        if (options.Server is not Uri server)
        {
            return "No BoardOil server has been configured.";
        }

        var sessionStore = new SessionStore(server);
        var storedSession = await sessionStore.LoadAsync();
        if (storedSession is null)
        {
            return $"No saved BoardOil session for {server.Host}.";
        }

        var revoked = false;
        try
        {
            using var httpClient = CreateHttpClient(server);
            using var response = await httpClient.PostAsJsonAsync(
                "api/auth/machine/logout",
                new { refreshToken = storedSession.RefreshToken },
                JsonOptions);
            revoked = response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // Local removal still prevents OilTTY from reusing the session.
        }
        finally
        {
            await sessionStore.DeleteAsync();
        }

        return revoked
            ? $"Logged out of BoardOil at {server.Host}."
            : $"Removed the local BoardOil session for {server.Host}; server revocation could not be confirmed.";
    }

    public async Task<BoardData> LoadBoardAsync(
        int boardId,
        CancellationToken cancellationToken = default)
    {
        var boardTask = _transport.GetAsync<BoardSnapshot>(
            $"api/boards/{boardId}",
            cancellationToken);
        var cardTypesTask = _transport.GetAsync<IReadOnlyList<CardTypeDefinition>>(
            $"api/boards/{boardId}/card-types",
            cancellationToken);
        var slicksTask = _transport.GetAsync<IReadOnlyList<SlickDefinition>>(
            $"api/boards/{boardId}/slicks",
            cancellationToken);
        var tagsTask = _transport.GetAsync<IReadOnlyList<CardTag>>(
            $"api/boards/{boardId}/tags",
            cancellationToken);
        var membersTask = _transport.GetAsync<IReadOnlyList<BoardMember>>(
            $"api/boards/{boardId}/members",
            cancellationToken);

        await Task.WhenAll(boardTask, cardTypesTask, slicksTask, tagsTask, membersTask);
        var board = await boardTask;
        var cardTypes = await cardTypesTask;
        var slicks = await slicksTask;

        return new BoardData(
            board,
            cardTypes.ToDictionary(cardType => cardType.Id),
            slicks.ToDictionary(slick => slick.Id),
            await tagsTask,
            await membersTask);
    }

    public Task<IReadOnlyList<BoardSummary>> LoadBoardsAsync(CancellationToken cancellationToken = default) =>
        _transport.GetAsync<IReadOnlyList<BoardSummary>>("api/boards", cancellationToken);

    public Task<BoardCard> CreateCardAsync(
        int boardId,
        CardDraft draft,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync<BoardCard>(
            () => new HttpRequestMessage(HttpMethod.Post, $"api/boards/{boardId}/cards")
            {
                Content = JsonContent.Create(draft, options: JsonOptions)
            },
            cancellationToken);

    public Task<BoardCard> UpdateCardAsync(
        int boardId,
        int cardId,
        CardDraft draft,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync<BoardCard>(
            () => new HttpRequestMessage(HttpMethod.Put, $"api/boards/{boardId}/cards/{cardId}")
            {
                Content = JsonContent.Create(draft, options: JsonOptions)
            },
            cancellationToken);

    public Task<BoardCard> MoveCardAsync(
        int boardId,
        CardMove move,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync<BoardCard>(
            () => new HttpRequestMessage(
                HttpMethod.Patch,
                $"api/boards/{boardId}/cards/{move.CardId}/move")
            {
                Content = JsonContent.Create(
                    new
                    {
                        boardColumnId = move.BoardColumnId,
                        positionAfterCardId = move.PositionAfterCardId
                    },
                    options: JsonOptions)
            },
            cancellationToken);

    public ValueTask DisposeAsync() => _transport.DisposeAsync();

    private static HttpClient CreateHttpClient(Uri server)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = server,
            Timeout = TimeSpan.FromSeconds(20)
        };
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return httpClient;
    }

    private static BoardOilClient Create(Uri server, IBoardOilSessionStore? sessionStore) =>
        new(new AuthenticatedBoardOilTransport(CreateHttpClient(server), sessionStore));

    private static bool IsRecoverableLoginException(Exception exception) =>
        exception is BoardOilRequestException
            or HttpRequestException
            or InvalidOperationException
            or TaskCanceledException;

    private static string LoginErrorMessage(Exception exception) =>
        exception is TaskCanceledException ? "Connection to the BoardOil server timed out." : exception.Message;

    private static string ReadPassword()
    {
        var password = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                return new string([.. password]);
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Count > 0)
                {
                    password.RemoveAt(password.Count - 1);
                }

                continue;
            }

            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                throw new OperationCanceledException();
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Add(key.KeyChar);
            }
        }
    }
}

internal sealed class BoardOilRequestException(
    HttpStatusCode statusCode,
    string message,
    IReadOnlyDictionary<string, string[]>? validationErrors = null) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; } =
        validationErrors ?? new Dictionary<string, string[]>();
}
