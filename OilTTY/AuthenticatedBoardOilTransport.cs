using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

internal sealed class AuthenticatedBoardOilTransport : IAsyncDisposable
{
    private const string CsrfHeaderName = "X-BoardOil-CSRF";
    private const int BinaryReadBufferSize = 81920;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _csrfLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly IBoardOilSessionStore? _sessionStore;
    private MachineAuthSession? _session;
    private bool _sessionPersisted;
    private string? _csrfToken;

    public AuthenticatedBoardOilTransport(
        HttpClient httpClient,
        IBoardOilSessionStore? sessionStore)
    {
        _httpClient = httpClient;
        _sessionStore = sessionStore;
    }

    public string IdentityLabel => _session?.User.DisplayName ?? "access token";

    public Uri Server => _httpClient.BaseAddress!;

    public void UseApiToken(string token) => SetBearerToken(token);

    public async Task LoginAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/machine/login",
            new { userName, password },
            JsonOptions,
            cancellationToken);
        var session = await ReadEnvelopeAsync<MachineAuthSession>(response, cancellationToken);
        await AcceptSessionAsync(session, cancellationToken);
    }

    public async Task ResumeAsync(
        StoredSession storedSession,
        CancellationToken cancellationToken = default)
    {
        var session = await RequestRefreshAsync(storedSession.RefreshToken, cancellationToken);
        await AcceptSessionAsync(session, cancellationToken);
    }

    public Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    public async Task<T> SendAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default)
    {
        using var request = requestFactory();
        var requiresCsrf = _session is not null && IsStateChanging(request.Method);
        if (requiresCsrf)
        {
            AddCsrfHeader(request, await GetCsrfTokenAsync(cancellationToken));
        }

        var attemptedToken = _httpClient.DefaultRequestHeaders.Authorization?.Parameter;
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || _session is null)
        {
            return await ReadEnvelopeAsync<T>(response, cancellationToken);
        }

        await RefreshAsync(attemptedToken, cancellationToken);
        using var retriedRequest = requestFactory();
        if (requiresCsrf)
        {
            AddCsrfHeader(retriedRequest, await GetCsrfTokenAsync(cancellationToken));
        }

        using var retriedResponse = await _httpClient.SendAsync(retriedRequest, cancellationToken);
        return await ReadEnvelopeAsync<T>(retriedResponse, cancellationToken);
    }

    public async Task<byte[]> GetBytesAsync(
        string path,
        string expectedMediaType,
        int maximumByteLength,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(expectedMediaType));
        var attemptedToken = _httpClient.DefaultRequestHeaders.Authorization?.Parameter;
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || _session is null)
        {
            return await ReadBytesAsync(
                response,
                expectedMediaType,
                maximumByteLength,
                cancellationToken);
        }

        await RefreshAsync(attemptedToken, cancellationToken);
        using var retriedRequest = new HttpRequestMessage(HttpMethod.Get, path);
        retriedRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(expectedMediaType));
        using var retriedResponse = await _httpClient.SendAsync(
            retriedRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        return await ReadBytesAsync(
            retriedResponse,
            expectedMediaType,
            maximumByteLength,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null && !_sessionPersisted)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var response = await _httpClient.PostAsJsonAsync(
                    "api/auth/machine/logout",
                    new { refreshToken = _session.RefreshToken },
                    JsonOptions,
                    timeout.Token);
            }
            catch (HttpRequestException)
            {
                // Session cleanup is best effort during application shutdown.
            }
            catch (OperationCanceledException)
            {
                // Session cleanup must not delay application shutdown.
            }
        }

        _csrfLock.Dispose();
        _refreshLock.Dispose();
        _httpClient.Dispose();
    }

    private async Task<string> GetCsrfTokenAsync(CancellationToken cancellationToken)
    {
        if (_csrfToken is not null)
        {
            return _csrfToken;
        }

        await _csrfLock.WaitAsync(cancellationToken);
        try
        {
            if (_csrfToken is null)
            {
                var result = await GetAsync<CsrfTokenDto>("api/auth/csrf", cancellationToken);
                if (string.IsNullOrWhiteSpace(result.CsrfToken))
                {
                    throw new InvalidOperationException("BoardOil returned an empty CSRF token.");
                }

                _csrfToken = result.CsrfToken;
            }

            return _csrfToken;
        }
        finally
        {
            _csrfLock.Release();
        }
    }

    private async Task RefreshAsync(
        string? rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is null)
            {
                throw new InvalidOperationException("The BoardOil session cannot be refreshed.");
            }

            if (!string.Equals(_session.AccessToken, rejectedAccessToken, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var session = await RequestRefreshAsync(_session.RefreshToken, cancellationToken);
                await AcceptSessionAsync(session, cancellationToken);
            }
            catch (BoardOilRequestException exception)
                when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                if (_sessionStore is not null)
                {
                    await _sessionStore.DeleteAsync();
                }

                throw;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void SetBearerToken(string token) =>
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static void AddCsrfHeader(HttpRequestMessage request, string csrfToken) =>
        request.Headers.TryAddWithoutValidation(CsrfHeaderName, csrfToken);

    private static bool IsStateChanging(HttpMethod method) =>
        method == HttpMethod.Post
        || method == HttpMethod.Put
        || method == HttpMethod.Patch
        || method == HttpMethod.Delete;

    private async Task<MachineAuthSession> RequestRefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/machine/refresh",
            new { refreshToken },
            JsonOptions,
            cancellationToken);
        return await ReadEnvelopeAsync<MachineAuthSession>(response, cancellationToken);
    }

    private async Task AcceptSessionAsync(
        MachineAuthSession session,
        CancellationToken cancellationToken = default)
    {
        _session = session;
        _sessionPersisted = false;
        SetBearerToken(session.AccessToken);
        if (_sessionStore is null)
        {
            return;
        }

        try
        {
            await _sessionStore.SaveAsync(session, cancellationToken);
            _sessionPersisted = true;
        }
        catch
        {
            await _sessionStore.DeleteAsync();
            throw;
        }
    }

    private static async Task<T> ReadEnvelopeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        ApiEnvelope<T>? envelope;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"BoardOil returned an unexpected response ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        if (!response.IsSuccessStatusCode || envelope?.Success != true || envelope.Data is null)
        {
            var message = envelope?.Message
                          ?? $"BoardOil request failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
            throw new BoardOilRequestException(
                response.StatusCode,
                message,
                envelope?.ValidationErrors);
        }

        return envelope.Data;
    }

    private static async Task<byte[]> ReadBytesAsync(
        HttpResponseMessage response,
        string expectedMediaType,
        int maximumByteLength,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            await ReadEnvelopeAsync<object>(response, cancellationToken);
            throw new InvalidOperationException("BoardOil returned an unsuccessful binary response.");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, expectedMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"BoardOil returned {mediaType ?? "an unknown content type"} instead of {expectedMediaType}.");
        }

        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength > maximumByteLength)
        {
            throw new InvalidOperationException("BoardOil returned an oversized image thumbnail.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[BinaryReadBufferSize];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + count > maximumByteLength)
            {
                throw new InvalidOperationException("BoardOil returned an oversized image thumbnail.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }
}
