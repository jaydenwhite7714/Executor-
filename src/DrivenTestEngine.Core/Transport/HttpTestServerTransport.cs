using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Protocol;
using DrivenTestEngine.Core.Security;

namespace DrivenTestEngine.Core.Transport;

/// <summary>
/// HTTPS transport for the Driven Football testing service. Every request is HMAC-SHA256 signed
/// (see <see cref="RequestSigner"/>) and session-bound after the handshake. Failure is surfaced,
/// never papered over.
/// </summary>
public sealed class HttpTestServerTransport : ITestServerTransport
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _requestTimeout;
    private readonly ClientCredentials _credentials;
    private readonly string _clientId;
    private readonly Uri _baseAddress;

    public HttpTestServerTransport(
        Uri baseAddress,
        ClientCredentials credentials,
        string clientVersion,
        string clientId,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(credentials);

        if (baseAddress.Scheme != Uri.UriSchemeHttps && baseAddress.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("The server URL must use https.", nameof(baseAddress));
        }

        var normalized = baseAddress.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseAddress
            : new Uri(baseAddress.AbsoluteUri + "/");

        _baseAddress = normalized;
        _credentials = credentials.Clone();
        _clientId = clientId;
        ClientVersion = clientVersion;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(25);

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AllowAutoRedirect = false,
        };

        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttpClient = true;
    }

    public Uri? BaseAddress => _baseAddress;

    public string ClientVersion { get; }

    public Task<TransportResult<HealthResponse>> CheckHealthAsync(CancellationToken cancellationToken) =>
        SendAsync<HealthResponse>(HttpMethod.Get, DteProtocol.Routes.Health, null, null, cancellationToken);

    public Task<TransportResult<ServerSession>> HandshakeAsync(CancellationToken cancellationToken)
    {
        var body = new HandshakeRequest
        {
            ClientId = _clientId,
            ClientVersion = ClientVersion,
            Protocol = DteProtocol.Version,
        };

        return SendAsync<ServerSession>(HttpMethod.Post, DteProtocol.Routes.Handshake, body, null, cancellationToken);
    }

    public Task<TransportResult<ServerSession>> RefreshSessionAsync(string sessionToken, CancellationToken cancellationToken) =>
        SendAsync<ServerSession>(HttpMethod.Post, DteProtocol.Routes.Refresh, new { }, sessionToken, cancellationToken);

    public async Task<TransportResult<bool>> CloseSessionAsync(string sessionToken, CancellationToken cancellationToken)
    {
        var result = await SendRawAsync(HttpMethod.Post, DteProtocol.Routes.CloseSession, new { }, sessionToken, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? TransportResult<bool>.Success(true)
            : TransportResult<bool>.Failed(result.Failure!);
    }

    public Task<TransportResult<StartTestResponse>> StartTestAsync(
        string sessionToken,
        StartTestRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<StartTestResponse>(HttpMethod.Post, DteProtocol.Routes.RunTest, request, sessionToken, cancellationToken);

    public Task<TransportResult<TestRunStatus>> GetRunStatusAsync(
        string sessionToken,
        string runId,
        CancellationToken cancellationToken)
    {
        var route = string.Format(
            CultureInfo.InvariantCulture,
            DteProtocol.Routes.RunStatus,
            Uri.EscapeDataString(runId));
        return SendAsync<TestRunStatus>(HttpMethod.Get, route, null, sessionToken, cancellationToken);
    }

    public Task<TransportResult<ServerLogBatch>> GetLogsAsync(
        string sessionToken,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(limit, 1, 500);
        var route = DteProtocol.Routes.Logs + "?limit=" + clamped.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            route += "&since=" + Uri.EscapeDataString(cursor);
        }

        return SendAsync<ServerLogBatch>(HttpMethod.Get, route, null, sessionToken, cancellationToken);
    }

    public Task<TransportResult<AntiCheatStatus>> GetAntiCheatStatusAsync(string sessionToken, CancellationToken cancellationToken) =>
        SendAsync<AntiCheatStatus>(HttpMethod.Get, DteProtocol.Routes.AntiCheatStatus, null, sessionToken, cancellationToken);

    public Task<TransportResult<AntiCheatStatus>> SetAntiCheatMonitoringAsync(
        string sessionToken,
        bool enabled,
        CancellationToken cancellationToken) =>
        SendAsync<AntiCheatStatus>(
            HttpMethod.Post,
            DteProtocol.Routes.AntiCheatMonitor,
            new AntiCheatMonitorRequest { Enabled = enabled },
            sessionToken,
            cancellationToken);

    private async Task<TransportResult<TResponse>> SendAsync<TResponse>(
        HttpMethod method,
        string route,
        object? body,
        string? sessionToken,
        CancellationToken cancellationToken)
    {
        var raw = await SendRawAsync(method, route, body, sessionToken, cancellationToken).ConfigureAwait(false);
        if (!raw.IsSuccess)
        {
            return TransportResult<TResponse>.Failed(raw.Failure!);
        }

        try
        {
            var value = JsonSerializer.Deserialize<TResponse>(raw.Value!, DteJson.Options);
            if (value is null)
            {
                return TransportResult<TResponse>.Failed(TransportFailure.Create(
                    TransportFailureKind.Protocol,
                    "The testing service returned an unreadable response.",
                    "The JSON body did not match protocol " + DteProtocol.Version + "."));
            }

            return TransportResult<TResponse>.Success(value);
        }
        catch (JsonException ex)
        {
            return TransportResult<TResponse>.Failed(TransportFailure.Create(
                TransportFailureKind.Protocol,
                "The testing service returned malformed JSON.",
                ex.Message));
        }
    }

    private async Task<TransportResult<string>> SendRawAsync(
        HttpMethod method,
        string route,
        object? body,
        string? sessionToken,
        CancellationToken cancellationToken)
    {
        if (!_credentials.IsComplete)
        {
            return TransportResult<string>.Failed(TransportFailure.NotConfigured(
                "The key id or signing secret is missing."));
        }

        byte[] payload;
        try
        {
            payload = body is null
                ? Array.Empty<byte>()
                : JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), DteJson.Options);
        }
        catch (NotSupportedException ex)
        {
            return TransportResult<string>.Failed(TransportFailure.Create(
                TransportFailureKind.Protocol,
                "The request body could not be serialised.",
                ex.Message));
        }

        Uri uri;
        try
        {
            uri = new Uri(_baseAddress, route.TrimStart('/'));
        }
        catch (UriFormatException ex)
        {
            return TransportResult<string>.Failed(TransportFailure.Create(
                TransportFailureKind.NotConfigured,
                "The server URL could not be combined with the request route.",
                ex.Message));
        }

        var timestamp = RequestSigner.TimestampNow();
        var nonce = RequestSigner.NewNonce();
        var signature = RequestSigner.ComputeSignature(
            _credentials.SharedSecret,
            method.Method,
            uri.PathAndQuery,
            timestamp,
            nonce,
            payload);

        using var request = new HttpRequestMessage(method, uri);
        if (payload.Length > 0)
        {
            var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Content = content;
        }

        request.Headers.TryAddWithoutValidation(DteProtocol.Headers.KeyId, _credentials.KeyId);
        request.Headers.TryAddWithoutValidation(DteProtocol.Headers.Timestamp, timestamp);
        request.Headers.TryAddWithoutValidation(DteProtocol.Headers.Nonce, nonce);
        request.Headers.TryAddWithoutValidation(DteProtocol.Headers.Signature, signature);
        request.Headers.TryAddWithoutValidation(DteProtocol.Headers.ProtocolVersion, DteProtocol.Version);
        request.Headers.TryAddWithoutValidation(DteProtocol.Headers.ClientVersion, ClientVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_requestTimeout);

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            var text = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return TransportResult<string>.Failed(MapFailure(response.StatusCode, text));
            }

            return TransportResult<string>.Success(text);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TransportResult<string>.Failed(TransportFailure.Timeout(
                $"The testing service did not answer within {_requestTimeout.TotalSeconds:0} seconds."));
        }
        catch (OperationCanceledException)
        {
            return TransportResult<string>.Failed(TransportFailure.Create(
                TransportFailureKind.Cancelled,
                "The request was cancelled."));
        }
        catch (HttpRequestException ex)
        {
            return TransportResult<string>.Failed(TransportFailure.Network(
                "Could not reach the testing service.",
                ex));
        }
    }

    private static TransportFailure MapFailure(HttpStatusCode status, string body)
    {
        ApiError? error = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                error = JsonSerializer.Deserialize<ApiErrorEnvelope>(body, DteJson.Options)?.Error;
            }
            catch (JsonException)
            {
                error = null;
            }
        }

        var message = string.IsNullOrWhiteSpace(error?.Message)
            ? DefaultMessage(status)
            : error!.Message;

        var kind = (int)status switch
        {
            401 => TransportFailureKind.Unauthorized,
            403 => TransportFailureKind.Forbidden,
            400 or 409 or 422 or 429 => TransportFailureKind.Rejected,
            >= 500 => TransportFailureKind.ServerError,
            _ => TransportFailureKind.Protocol,
        };

        return TransportFailure.Create(kind, message, error?.Hint, error?.Code, (int)status);
    }

    private static string DefaultMessage(HttpStatusCode status) => (int)status switch
    {
        401 => "The testing service rejected the signed request (HTTP 401).",
        403 => "The testing service refused this account or session (HTTP 403).",
        404 => "The testing service has no route for this call (HTTP 404).",
        429 => "The testing service is rate limiting this client (HTTP 429).",
        >= 500 => "The testing service reported an internal error (HTTP " + (int)status + ").",
        _ => "The testing service returned HTTP " + (int)status + ".",
    };

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
