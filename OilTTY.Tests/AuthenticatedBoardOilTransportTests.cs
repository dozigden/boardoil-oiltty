using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

public sealed class AuthenticatedBoardOilTransportTests
{
    private static readonly Uri Server = new("https://boardoil.test/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetAsync_SendsApiTokenWithoutRefreshSession()
    {
        var handler = new StubHttpMessageHandler((request, call, _) =>
        {
            Assert.Equal(0, call);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("api-token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(Success(new TestResult("loaded")));
        });
        await using var transport = CreateTransport(handler);
        transport.UseApiToken("api-token");

        var result = await transport.GetAsync<TestResult>(
            "api/boards",
            TestContext.Current.CancellationToken);

        Assert.Equal("loaded", result.Value);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_RefreshesAndReplaysPutRequestAfterUnauthorized()
    {
        var requestBodies = new List<string>();
        var handler = new StubHttpMessageHandler(async (request, call, cancellationToken) =>
        {
            switch (call)
            {
                case 0:
                    Assert.Equal("api/auth/machine/login", request.RequestUri?.PathAndQuery.TrimStart('/'));
                    return Success(Session("old-access", "refresh-one"));
                case 1:
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Equal("api/auth/csrf", request.RequestUri?.PathAndQuery.TrimStart('/'));
                    Assert.Equal("old-access", request.Headers.Authorization?.Parameter);
                    return Success(new CsrfTokenDto("csrf-token"));
                case 2:
                    Assert.Equal(HttpMethod.Put, request.Method);
                    Assert.Equal("old-access", request.Headers.Authorization?.Parameter);
                    Assert.Equal("csrf-token", request.Headers.GetValues("X-BoardOil-CSRF").Single());
                    requestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                    return Failure(HttpStatusCode.Unauthorized, "expired");
                case 3:
                    Assert.Equal("api/auth/machine/refresh", request.RequestUri?.PathAndQuery.TrimStart('/'));
                    Assert.Contains("refresh-one", await request.Content!.ReadAsStringAsync(cancellationToken));
                    return Success(Session("new-access", "refresh-two"));
                case 4:
                    Assert.Equal(HttpMethod.Put, request.Method);
                    Assert.Equal("new-access", request.Headers.Authorization?.Parameter);
                    Assert.Equal("csrf-token", request.Headers.GetValues("X-BoardOil-CSRF").Single());
                    requestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                    return Success(new TestResult("updated"));
                default:
                    throw new InvalidOperationException($"Unexpected HTTP call {call}.");
            }
        });
        var sessionStore = new StubSessionStore();
        await using var transport = CreateTransport(handler, sessionStore);
        await transport.LoginAsync("alice", "secret", TestContext.Current.CancellationToken);

        var result = await transport.SendAsync<TestResult>(
            () => new HttpRequestMessage(HttpMethod.Put, "api/cards/42")
            {
                Content = JsonContent.Create(new { title = "Replayed safely" })
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("updated", result.Value);
        Assert.Equal(2, sessionStore.SaveCount);
        Assert.Equal(requestBodies[0], requestBodies[1]);
        Assert.Contains("Replayed safely", requestBodies[1]);
    }

    [Fact]
    public async Task SendAsync_DeletesPersistedSessionWhenRefreshIsRejected()
    {
        var handler = new StubHttpMessageHandler((request, call, _) => Task.FromResult(call switch
        {
            0 => Success(Session("old-access", "invalid-refresh")),
            1 => Failure(HttpStatusCode.Unauthorized, "expired"),
            2 => Failure(HttpStatusCode.Unauthorized, "refresh rejected"),
            _ => throw new InvalidOperationException($"Unexpected HTTP call {call}.")
        }));
        var sessionStore = new StubSessionStore();
        await using var transport = CreateTransport(handler, sessionStore);
        await transport.LoginAsync("alice", "secret", TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<BoardOilRequestException>(
            () => transport.GetAsync<TestResult>(
                "api/boards",
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal("refresh rejected", exception.Message);
        Assert.Equal(1, sessionStore.DeleteCount);
    }

    [Fact]
    public async Task SendAsync_PreservesStructuredValidationErrors()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["title"] = ["A title is required."],
            ["tags"] = ["Unknown tag."]
        };
        var handler = new StubHttpMessageHandler((_, _, _) => Task.FromResult(
            Failure(HttpStatusCode.UnprocessableEntity, "Validation failed.", errors)));
        await using var transport = CreateTransport(handler);
        transport.UseApiToken("api-token");

        var exception = await Assert.ThrowsAsync<BoardOilRequestException>(
            () => transport.SendAsync<TestResult>(
                () => new HttpRequestMessage(HttpMethod.Put, "api/cards/42")
                {
                    Content = JsonContent.Create(new { title = string.Empty })
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Equal("Validation failed.", exception.Message);
        Assert.Equal(["A title is required."], exception.ValidationErrors["title"]);
        Assert.Equal(["Unknown tag."], exception.ValidationErrors["tags"]);
    }

    private static AuthenticatedBoardOilTransport CreateTransport(
        HttpMessageHandler handler,
        IBoardOilSessionStore? sessionStore = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = Server };
        return new AuthenticatedBoardOilTransport(httpClient, sessionStore);
    }

    private static MachineAuthSession Session(string accessToken, string refreshToken) =>
        new(
            accessToken,
            DateTime.UtcNow.AddMinutes(5),
            refreshToken,
            DateTime.UtcNow.AddDays(1),
            new AuthUser(1, "alice", "Alice", "Owner"),
            "Bearer");

    private static HttpResponseMessage Success<T>(T value) =>
        JsonResponse(
            HttpStatusCode.OK,
            new ApiEnvelope<T>(true, value, 200, null));

    private static HttpResponseMessage Failure(
        HttpStatusCode statusCode,
        string message,
        IReadOnlyDictionary<string, string[]>? validationErrors = null) =>
        JsonResponse(
            statusCode,
            new ApiEnvelope<object>(false, null, (int)statusCode, message, validationErrors));

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode statusCode, T value) =>
        new(statusCode)
        {
            Content = JsonContent.Create(value, options: JsonOptions)
        };

    private sealed record TestResult(string Value);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var call = CallCount++;
            return respond(request, call, cancellationToken);
        }
    }

    private sealed class StubSessionStore : IBoardOilSessionStore
    {
        public int DeleteCount { get; private set; }

        public int SaveCount { get; private set; }

        public Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredSession?>(null);

        public Task SaveAsync(
            MachineAuthSession session,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync()
        {
            DeleteCount++;
            return Task.CompletedTask;
        }
    }
}
