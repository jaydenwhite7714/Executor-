using System.Net.Http;
using DrivenTestEngine.Core.History;
using DrivenTestEngine.Core.Logging;
using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Protocol;
using DrivenTestEngine.Core.Security;
using DrivenTestEngine.Core.Tests;
using DrivenTestEngine.Core.Transport;

namespace DrivenTestEngine.Core.Engine;

/// <summary>
/// Orchestrates the authenticated link to the Driven Football server-side testing system.
///
/// Design rules enforced here:
///  * Nothing is reported as connected until a signed handshake returns a token for a developer
///    account inside a private test session.
///  * Every test call is sent to the server; the client never invents metrics or verdicts.
///  * Failures are surfaced as failures, never as fake results.
/// </summary>
public sealed class EngineClient : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RunPollInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromSeconds(120);

    private readonly DetectionLogStore _log;
    private readonly HistoryStore _history;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly object _gate = new();

    private ITestServerTransport? _transport;
    private CancellationTokenSource? _backgroundCts;
    private ServerSession? _session;
    private ConnectionState _state = ConnectionState.Disconnected;
    private string _statusMessage = "Not connected";
    private TransportFailure? _failure;
    private DateTimeOffset? _connectedAt;
    private TimeSpan? _clockSkew;
    private int _signatureFailures;
    private string? _logCursor;
    private bool _disposed;

    public EngineClient(
        DetectionLogStore log,
        HistoryStore history,
        string clientVersion,
        string clientId,
        string? expectedRobloxUserId)
    {
        _log = log;
        _history = history;
        ClientVersion = clientVersion;
        ClientId = clientId;
        ExpectedRobloxUserId = expectedRobloxUserId;
    }

    public string ClientVersion { get; }

    public string ClientId { get; }

    public string? ExpectedRobloxUserId { get; private set; }

    public Uri? ServerUrl { get; private set; }

    public ConnectionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public ServerSession? Session
    {
        get
        {
            lock (_gate)
            {
                return _session;
            }
        }
    }

    public event EventHandler<ConnectionSnapshot>? StateChanged;

    public event EventHandler<TestRunStatus>? RunUpdated;

    public event EventHandler<AntiCheatStatus>? AntiCheatChanged;

    public ConnectionSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new ConnectionSnapshot
                {
                    State = _state,
                    Message = _statusMessage,
                    Session = _session,
                    ServerUrl = ServerUrl,
                    ConnectedAt = _connectedAt,
                    Failure = _failure,
                };
            }
        }
    }

    /// <summary>Replaces the transport configuration. Always drops any existing session.</summary>
    public void Configure(Uri? serverUrl, ClientCredentials credentials, string? expectedRobloxUserId)
    {
        EnsureNotDisposed();
        TeardownTransport();

        ExpectedRobloxUserId = string.IsNullOrWhiteSpace(expectedRobloxUserId) ? null : expectedRobloxUserId.Trim();
        ServerUrl = serverUrl;

        var validation = credentials.Validate();
        if (serverUrl is null)
        {
            SetState(ConnectionState.Disconnected, "No server URL configured.", null);
            return;
        }

        if (validation is not null)
        {
            SetState(ConnectionState.Disconnected, validation, TransportFailure.NotConfigured(validation));
            return;
        }

        try
        {
            _transport = new HttpTestServerTransport(serverUrl, credentials, ClientVersion, ClientId);
        }
        catch (ArgumentException ex)
        {
            SetState(ConnectionState.Disconnected, "The server URL is not valid.", TransportFailure.Create(
                TransportFailureKind.NotConfigured,
                "The server URL is not valid.",
                ex.Message));
            return;
        }

        SetState(ConnectionState.Disconnected, "Ready to connect.", null);
    }

    public async Task<ConnectionSnapshot> ConnectAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transport = _transport;
            if (transport is null)
            {
                var failure = TransportFailure.NotConfigured("Configure the server URL, key id and signing secret first.");
                SetState(ConnectionState.Disconnected, failure.Message, failure);
                _log.Warn(DetectionCategory.Session, "[client] Cannot connect: " + failure.Message);
                return Snapshot;
            }

            TeardownBackground();
            lock (_gate)
            {
                _session = null;
                _connectedAt = null;
                _logCursor = null;
            }

            SetState(ConnectionState.Connecting, "Checking the testing service…", null);
            _log.Info(DetectionCategory.Session, "[client] Health check → " + transport.BaseAddress);

            var health = await transport.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
            if (!health.IsSuccess)
            {
                return FailConnect(transport, health.Failure!, "Health check failed");
            }

            if (health.Value!.Time.HasValue)
            {
                _clockSkew = health.Value.Time.Value - DateTimeOffset.UtcNow;
                if (_clockSkew.Value.Duration() > DteProtocol.MaxClockSkew)
                {
                    _log.Warn(
                        DetectionCategory.Session,
                        "[client] Local clock is " + Math.Round(_clockSkew.Value.TotalSeconds) + "s away from the server clock. Signed requests may be rejected.");
                }
            }

            _log.Info(
                DetectionCategory.Server,
                "[server] " + (string.IsNullOrWhiteSpace(health.Value.Game) ? "Testing service" : health.Value.Game) +
                " online · version " + (string.IsNullOrWhiteSpace(health.Value.ServerVersion) ? "unknown" : health.Value.ServerVersion));

            SetState(ConnectionState.Connecting, "Signing handshake…", null);
            var handshake = await transport.HandshakeAsync(cancellationToken).ConfigureAwait(false);
            if (!handshake.IsSuccess)
            {
                _signatureFailures++;
                return FailConnect(transport, handshake.Failure!, "Handshake rejected");
            }

            var session = handshake.Value!;
            var authorization = session.Authorize(DateTimeOffset.UtcNow, ExpectedRobloxUserId);
            if (!authorization.IsAuthorized)
            {
                lock (_gate)
                {
                    _session = null;
                }

                var message = string.Join(" ", authorization.Problems);
                _log.Error(DetectionCategory.Session, "[client] Handshake refused", message);
                SetState(ConnectionState.Faulted, message, TransportFailure.Create(
                    TransportFailureKind.Forbidden,
                    "The testing service did not authorise this session.",
                    message));
                return Snapshot;
            }

            lock (_gate)
            {
                _session = session;
                _connectedAt = DateTimeOffset.UtcNow;
                _failure = null;
                _logCursor = null;
            }

            _log.Info(
                DetectionCategory.Session,
                "[client] Session authorised for " + session.Account.DisplayName + " (" + session.Account.RobloxUserId + ")",
                "place " + session.TestSession.PlaceId + " · instance " + (session.TestSession.ServerInstanceId ?? "n/a"));

            SetState(
                ConnectionState.Connected,
                "Connected as " + session.Account.DisplayName,
                null);

            StartBackground(transport);

            await PollAntiCheatAsync(transport, session, CancellationToken.None).ConfigureAwait(false);
            await PollLogsAsync(transport, session, CancellationToken.None).ConfigureAwait(false);

            return Snapshot;
        }
        catch (OperationCanceledException)
        {
            SetState(ConnectionState.Disconnected, "Connection cancelled.", null);
            return Snapshot;
        }
        catch (Exception ex)
        {
            var kind = ex is HttpRequestException ? TransportFailureKind.Network : TransportFailureKind.Protocol;
            var diagnostic = ex.GetType().Name + " (0x" + ex.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + "): " + ex.Message;
            _log.Error(DetectionCategory.Client, "[client] Connection attempt threw unexpectedly", diagnostic);
            var failure = TransportFailure.Create(
                kind,
                "The connection attempt failed before a complete handshake response was processed.",
                diagnostic);
            SetState(ConnectionState.Faulted, failure.DisplayMessage, failure);
            return Snapshot;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        ServerSession? session;
        ITestServerTransport? transport;
        lock (_gate)
        {
            session = _session;
            transport = _transport;
        }

        SetState(ConnectionState.Disconnecting, "Closing session…", null);
        TeardownBackground();

        if (transport is not null && session is not null)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var closed = await transport.CloseSessionAsync(session.SessionToken, closeCts.Token).ConfigureAwait(false);
                if (!closed.IsSuccess)
                {
                    _log.Warn(DetectionCategory.Session, "[client] Close-session call failed: " + closed.Failure!.DisplayMessage);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_gate)
        {
            _session = null;
            _connectedAt = null;
            _logCursor = null;
        }

        SetState(ConnectionState.Disconnected, "Disconnected.", null);
        _log.Info(DetectionCategory.Session, "[client] Session closed.");
    }

    public async Task<TestRunOutcome> RunTestAsync(
        string testId,
        IReadOnlyDictionary<string, double>? parameters,
        IProgress<TestRunStatus>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        var definition = TestCatalog.Find(testId);
        if (definition is null)
        {
            return TestRunOutcome.Fail(TransportFailure.Create(
                TransportFailureKind.Rejected,
                "Unknown test '" + testId + "'.",
                "The server only accepts the test ids defined in the protocol."));
        }

        ServerSession? session;
        ITestServerTransport? transport;
        lock (_gate)
        {
            session = _session;
            transport = _transport;
        }

        if (transport is null || session is null || State != ConnectionState.Connected)
        {
            return TestRunOutcome.Fail(TransportFailure.NotConfigured(
                "No verified server session. Connect to the testing service first."));
        }

        if (!session.AllowsTest(testId))
        {
            var reason = "The testing service does not advertise '" + testId + "' for this session.";
            _log.Warn(DetectionCategory.Test, "[client] " + reason);
            return TestRunOutcome.Fail(TransportFailure.Create(
                TransportFailureKind.Forbidden,
                reason,
                "The server decides which tests exist for your session."));
        }

        var sanitized = new Dictionary<string, double>(StringComparer.Ordinal);
        if (parameters is not null)
        {
            foreach (var pair in parameters)
            {
                sanitized[pair.Key] = pair.Value;
            }
        }

        if (definition.Parameter is not null && sanitized.TryGetValue(definition.Parameter.Id, out var raw))
        {
            sanitized[definition.Parameter.Id] = definition.Parameter.Clamp(raw);
        }

        var request = new StartTestRequest
        {
            TestId = testId,
            Parameters = sanitized.Count == 0 ? null : sanitized,
            ClientRunId = Guid.NewGuid().ToString("N"),
        };

        _log.Info(DetectionCategory.Test, "[client] Requesting " + definition.DisplayName + " · run " + request.ClientRunId[..8]);

        var start = await transport.StartTestAsync(session.SessionToken, request, cancellationToken).ConfigureAwait(false);
        if (!start.IsSuccess)
        {
            _log.Error(DetectionCategory.Test, "[client] " + definition.DisplayName + " was refused", start.Failure!.DisplayMessage);
            return TestRunOutcome.Fail(start.Failure!);
        }

        var runId = start.Value!.RunId;
        if (string.IsNullOrWhiteSpace(runId))
        {
            return TestRunOutcome.Fail(TransportFailure.Create(
                TransportFailureKind.Protocol,
                "The testing service accepted the run but returned no run id."));
        }

        _log.Info(DetectionCategory.Server, "[server] Run " + runId + " queued for " + definition.DisplayName);

        TestRunStatus? last = null;
        var deadline = DateTimeOffset.UtcNow + DefaultRunTimeout;
        NetworkTransient retry = new();

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await Task.Delay(RunPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return TestRunOutcome.Fail(TransportFailure.Create(
                    TransportFailureKind.Cancelled,
                    "The run was cancelled before the server reported a result."));
            }

            var status = await transport.GetRunStatusAsync(session.SessionToken, runId, cancellationToken).ConfigureAwait(false);
            if (!status.IsSuccess)
            {
                var failure = status.Failure!;
                if ((failure.Kind is TransportFailureKind.Network or TransportFailureKind.Timeout) && retry.Attempt < 5)
                {
                    retry.Attempt++;
                    _log.Warn(DetectionCategory.Test, "[client] Status poll failed (" + failure.Kind + "), retrying…");
                    continue;
                }

                _log.Error(DetectionCategory.Test, "[client] Could not read run " + runId, failure.DisplayMessage);
                return TestRunOutcome.Fail(failure);
            }

            last = status.Value!;
            progress?.Report(last);
            RaiseRunUpdated(last);

            if (last.State.IsTerminal())
            {
                break;
            }
        }

        if (last is null || !last.State.IsTerminal())
        {
            var failure = TransportFailure.Create(
                TransportFailureKind.Timeout,
                "Run " + runId + " did not finish within " + DefaultRunTimeout.TotalSeconds + " seconds.",
                "The server may still be executing it; check the game's server console.");
            _log.Warn(DetectionCategory.Test, "[client] " + failure.Message);
            return TestRunOutcome.Fail(failure);
        }

        RecordHistory(last, session);
        LogRunResult(definition, last);
        return TestRunOutcome.Ok(last);
    }

    public async Task<TransportResult<AntiCheatStatus>> SetAntiCheatMonitoringAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var (transport, session) = RequireSession();
        if (transport is null || session is null)
        {
            return TransportResult<AntiCheatStatus>.Failed(TransportFailure.NotConfigured(
                "Connect to the testing service before changing anti-cheat monitoring."));
        }

        var result = await transport.SetAntiCheatMonitoringAsync(session.SessionToken, enabled, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            _log.Info(DetectionCategory.AntiCheat, "[client] Anti-cheat monitoring " + (enabled ? "enabled" : "disabled") + " on the game server.");
            RaiseAntiCheat(result.Value!);
        }
        else
        {
            _log.Error(DetectionCategory.AntiCheat, "[client] Anti-cheat toggle refused", result.Failure!.DisplayMessage);
        }

        return result;
    }

    public async Task<TransportResult<AntiCheatStatus>> RefreshAntiCheatAsync(CancellationToken cancellationToken = default)
    {
        var (transport, session) = RequireSession();
        if (transport is null || session is null)
        {
            return TransportResult<AntiCheatStatus>.Failed(TransportFailure.NotConfigured(
                "Connect to the testing service to read anti-cheat status."));
        }

        var result = await transport.GetAntiCheatStatusAsync(session.SessionToken, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            RaiseAntiCheat(result.Value!);
        }

        return result;
    }

    /// <summary>Real, locally verifiable facts about the current link. No simulated entries.</summary>
    public IReadOnlyList<IntegrityCheck> BuildIntegrityChecks()
    {
        ServerSession? session;
        ConnectionState state;
        TimeSpan? skew;
        int signatureFailures;
        lock (_gate)
        {
            session = _session;
            state = _state;
            skew = _clockSkew;
            signatureFailures = _signatureFailures;
        }

        var checks = new List<IntegrityCheck>
        {
            new()
            {
                Id = "transport",
                Name = "Signed transport",
                Passed = _transport is not null,
                Detail = _transport is null
                    ? "No transport configured."
                    : "HMAC-SHA256 signing over " + _transport.BaseAddress,
            },
            new()
            {
                Id = "reachable",
                Name = "Service reachable",
                Passed = state == ConnectionState.Connected,
                Detail = state == ConnectionState.Connected ? "Health check answered." : "No successful health check.",
            },
            new()
            {
                Id = "signature",
                Name = "Signature accepted",
                Passed = signatureFailures == 0 && state == ConnectionState.Connected,
                Detail = signatureFailures == 0
                    ? "No request has been rejected for a bad signature."
                    : signatureFailures + " request(s) rejected. Check the key id and secret.",
            },
            new()
            {
                Id = "developer",
                Name = "Developer account",
                Passed = session?.Account.IsDeveloper == true,
                Detail = session is null
                    ? "No session."
                    : session.Account.DisplayName + " (" + session.Account.RobloxUserId + ")",
            },
            new()
            {
                Id = "private-session",
                Name = "Private test session",
                Passed = session?.TestSession.IsPrivateTestSession == true,
                Detail = session is null
                    ? "No session."
                    : "Place " + session.TestSession.PlaceId + " · instance " + (session.TestSession.ServerInstanceId ?? "n/a"),
            },
            new()
            {
                Id = "token",
                Name = "Session token",
                Passed = session is not null && !session.IsExpired(DateTimeOffset.UtcNow),
                Detail = session is null
                    ? "No token."
                    : "Expires " + session.ExpiresAt.ToLocalTime().ToString("HH:mm:ss"),
            },
            new()
            {
                Id = "clock",
                Name = "Clock skew",
                Passed = skew is null || skew.Value.Duration() <= DteProtocol.MaxClockSkew,
                Detail = skew is null
                    ? "Server time not yet sampled."
                    : Math.Round(skew.Value.TotalSeconds) + "s from the server clock (limit "
                      + DteProtocol.MaxClockSkew.TotalMinutes + " min).",
            },
        };

        return checks;
    }

    private (ITestServerTransport? Transport, ServerSession? Session) RequireSession()
    {
        lock (_gate)
        {
            return (_state == ConnectionState.Connected ? _transport : null,
                    _state == ConnectionState.Connected ? _session : null);
        }
    }

    private void RecordHistory(TestRunStatus status, ServerSession session)
    {
        _history.Add(new HistoryEntry
        {
            TestId = status.TestId,
            TestName = TestCatalog.DisplayNameFor(status.TestId),
            StartedAt = status.StartedAt ?? DateTimeOffset.Now,
            CompletedAt = status.CompletedAt,
            State = status.State,
            Metric = status.Metric,
            Unit = status.Unit,
            Threshold = status.Threshold,
            RunId = status.RunId,
            Message = status.Message,
            ServerInstanceId = status.ServerInstanceId ?? session.TestSession.ServerInstanceId,
            SessionId = session.TestSession.SessionId,
        });
    }

    private void LogRunResult(TestDefinition definition, TestRunStatus status)
    {
        var level = status.State switch
        {
            TestRunState.Passed => DetectionLevel.Info,
            TestRunState.Failed => DetectionLevel.Warning,
            TestRunState.Errored => DetectionLevel.Error,
            TestRunState.Rejected => DetectionLevel.Warning,
            _ => DetectionLevel.Warning,
        };

        var metric = status.Metric.HasValue
            ? " · " + status.Metric.Value.ToString("0.##") + " " + (status.Unit ?? string.Empty)
            : string.Empty;

        _log.Log(
            level,
            DetectionCategory.Server,
            "[server] " + definition.DisplayName + " " + status.State.ToDisplayText().ToLowerInvariant() + metric,
            status.Message);
    }

    private ConnectionSnapshot FailConnect(ITestServerTransport transport, TransportFailure failure, string context)
    {
        if (failure.Kind == TransportFailureKind.Unauthorized)
        {
            _signatureFailures++;
        }

        var message = context + ": " + failure.DisplayMessage;
        _log.Error(DetectionCategory.Session, "[client] " + message, failure.ServerCode);

        if (transport.BaseAddress is not null)
        {
            ServerUrl = transport.BaseAddress;
        }

        SetState(ConnectionState.Faulted, message, failure);
        return Snapshot;
    }

    private void StartBackground(ITestServerTransport transport)
    {
        TeardownBackground();
        var cts = new CancellationTokenSource();
        _backgroundCts = cts;
        _ = Task.Run(() => BackgroundLoopAsync(transport, cts.Token), CancellationToken.None);
    }

    private void TeardownBackground()
    {
        var cts = _backgroundCts;
        _backgroundCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    private async Task BackgroundLoopAsync(ITestServerTransport transport, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                ServerSession? session;
                lock (_gate)
                {
                    session = _session;
                }

                if (session is null || State != ConnectionState.Connected)
                {
                    continue;
                }

                await PollLogsAsync(transport, session, cancellationToken).ConfigureAwait(false);
                await PollAntiCheatAsync(transport, session, cancellationToken).ConfigureAwait(false);
                await EnsureSessionFreshAsync(transport, session, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error(DetectionCategory.Client, "[client] Background monitor stopped unexpectedly.", ex.Message);
        }
    }

    private async Task PollLogsAsync(ITestServerTransport transport, ServerSession session, CancellationToken cancellationToken)
    {
        var cursor = _logCursor;
        var result = await transport.GetLogsAsync(session.SessionToken, cursor, 100, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            HandlePollFailure(result.Failure!, "log");
            return;
        }

        foreach (var entry in result.Value!.Entries)
        {
            _log.Add(new DetectionLogEntry
            {
                Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
                Timestamp = entry.Timestamp == default ? DateTimeOffset.Now : entry.Timestamp,
                Level = entry.Level,
                Category = entry.Category,
                Message = entry.Message,
                Detail = entry.Detail,
                IsLocal = false,
            });
        }

        if (!string.IsNullOrWhiteSpace(result.Value.NextCursor))
        {
            _logCursor = result.Value.NextCursor;
        }
    }

    private async Task PollAntiCheatAsync(ITestServerTransport transport, ServerSession session, CancellationToken cancellationToken)
    {
        var result = await transport.GetAntiCheatStatusAsync(session.SessionToken, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            HandlePollFailure(result.Failure!, "anti-cheat");
            return;
        }

        RaiseAntiCheat(result.Value!);
    }

    private async Task EnsureSessionFreshAsync(ITestServerTransport transport, ServerSession session, CancellationToken cancellationToken)
    {
        if (session.ExpiresAt - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(2))
        {
            return;
        }

        var refreshed = await transport.RefreshSessionAsync(session.SessionToken, cancellationToken).ConfigureAwait(false);
        if (!refreshed.IsSuccess)
        {
            if (refreshed.Failure!.Kind is TransportFailureKind.Unauthorized or TransportFailureKind.Forbidden)
            {
                _log.Error(DetectionCategory.Session, "[client] Session refresh was refused; disconnecting.", refreshed.Failure.DisplayMessage);
                SetState(ConnectionState.Faulted, "Session rejected by the testing service.", refreshed.Failure);
            }

            return;
        }

        var candidate = refreshed.Value!;
        var authorization = candidate.Authorize(DateTimeOffset.UtcNow, ExpectedRobloxUserId);
        if (!authorization.IsAuthorized)
        {
            _log.Error(DetectionCategory.Session, "[client] Refreshed session is not authorised.", string.Join(" ", authorization.Problems));
            SetState(ConnectionState.Faulted, "Refreshed session is not authorised.", TransportFailure.Create(
                TransportFailureKind.Forbidden,
                "Refreshed session is not authorised.",
                string.Join(" ", authorization.Problems)));
            return;
        }

        lock (_gate)
        {
            _session = candidate;
        }

        SetState(ConnectionState.Connected, "Connected as " + candidate.Account.DisplayName, null);
    }

    private void HandlePollFailure(TransportFailure failure, string topic)
    {
        if (failure.Kind == TransportFailureKind.Unauthorized || failure.Kind == TransportFailureKind.Forbidden)
        {
            _log.Error(DetectionCategory.Session, "[client] The testing service refused a " + topic + " poll.", failure.DisplayMessage);
            SetState(ConnectionState.Faulted, "The testing service refused the session token.", failure);
            return;
        }

        _log.Warn(DetectionCategory.Client, "[client] " + topic + " poll failed: " + failure.Kind.ToString().ToLowerInvariant(), failure.Message);
    }

    private void SetState(ConnectionState state, string message, TransportFailure? failure)
    {
        ConnectionSnapshot snapshot;
        lock (_gate)
        {
            _state = state;
            _statusMessage = message;
            if (failure is not null)
            {
                _failure = failure;
            }

            snapshot = new ConnectionSnapshot
            {
                State = _state,
                Message = _statusMessage,
                Session = _session,
                ServerUrl = ServerUrl,
                ConnectedAt = _connectedAt,
                Failure = _failure,
            };
        }

        try
        {
            StateChanged?.Invoke(this, snapshot);
        }
        catch (Exception)
        {
            // A UI subscriber must never break the engine loop.
        }
    }

    private void RaiseRunUpdated(TestRunStatus status)
    {
        try
        {
            RunUpdated?.Invoke(this, status);
        }
        catch (Exception)
        {
        }
    }

    private void RaiseAntiCheat(AntiCheatStatus status)
    {
        try
        {
            AntiCheatChanged?.Invoke(this, status);
        }
        catch (Exception)
        {
        }
    }

    private void TeardownTransport()
    {
        ITestServerTransport? transport;
        lock (_gate)
        {
            transport = _transport;
            _transport = null;
            _session = null;
            _connectedAt = null;
            _logCursor = null;
        }

        transport?.Dispose();
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EngineClient));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TeardownBackground();
        TeardownTransport();
        _connectGate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Small helper that tracks how many transient retries a poll has used.</summary>
    private sealed class NetworkTransient
    {
        public int Attempt { get; set; }
    }
}
