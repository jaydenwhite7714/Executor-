using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Security;

namespace DrivenTestEngine.Core.Transport;

/// <summary>
/// The authenticated link to the Driven Football server-side testing system.
/// Every implementation must be a thin transport: no caching of results, no local fallbacks,
/// no simulated data. If the service cannot be reached the call fails.
/// </summary>
public interface ITestServerTransport : IDisposable
{
    Uri? BaseAddress { get; }

    string ClientVersion { get; }

    Task<TransportResult<HealthResponse>> CheckHealthAsync(CancellationToken cancellationToken);

    Task<TransportResult<ServerSession>> HandshakeAsync(CancellationToken cancellationToken);

    Task<TransportResult<ServerSession>> RefreshSessionAsync(string sessionToken, CancellationToken cancellationToken);

    Task<TransportResult<bool>> CloseSessionAsync(string sessionToken, CancellationToken cancellationToken);

    Task<TransportResult<StartTestResponse>> StartTestAsync(
        string sessionToken,
        StartTestRequest request,
        CancellationToken cancellationToken);

    Task<TransportResult<TestRunStatus>> GetRunStatusAsync(
        string sessionToken,
        string runId,
        CancellationToken cancellationToken);

    Task<TransportResult<ServerLogBatch>> GetLogsAsync(
        string sessionToken,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);

    Task<TransportResult<AntiCheatStatus>> GetAntiCheatStatusAsync(
        string sessionToken,
        CancellationToken cancellationToken);

    Task<TransportResult<AntiCheatStatus>> SetAntiCheatMonitoringAsync(
        string sessionToken,
        bool enabled,
        CancellationToken cancellationToken);
}
