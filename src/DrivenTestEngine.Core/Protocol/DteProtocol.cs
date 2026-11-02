namespace DrivenTestEngine.Core.Protocol;

/// <summary>
/// Constants that describe the Driven Test Engine wire protocol (see docs/PROTOCOL.md).
/// The desktop client and the game's server-side testing service must agree on these values.
/// </summary>
public static class DteProtocol
{
    /// <summary>Protocol generation implemented by this client.</summary>
    public const string Version = "1.0";

    /// <summary>Signature scheme advertised in the handshake.</summary>
    public const string SignatureScheme = "DTE-HMAC-SHA256";

    /// <summary>Maximum accepted difference between the client clock and the server clock.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static class Headers
    {
        public const string KeyId = "X-DTE-Key-Id";
        public const string Timestamp = "X-DTE-Timestamp";
        public const string Nonce = "X-DTE-Nonce";
        public const string Signature = "X-DTE-Signature";
        public const string ProtocolVersion = "X-DTE-Protocol";
        public const string ClientVersion = "X-DTE-Client";
    }

    public static class Routes
    {
        public const string Health = "/v1/health";
        public const string Handshake = "/v1/session/handshake";
        public const string Refresh = "/v1/session/refresh";
        public const string CloseSession = "/v1/session/close";
        public const string RunTest = "/v1/tests/run";
        public const string RunStatus = "/v1/tests/runs/{0}";
        public const string Logs = "/v1/logs";
        public const string AntiCheatStatus = "/v1/anticheat/status";
        public const string AntiCheatMonitor = "/v1/anticheat/monitor";
    }

    public static class ErrorCodes
    {
        public const string Unauthorized = "unauthorized";
        public const string Forbidden = "forbidden";
        public const string DeveloperOnly = "developer_only";
        public const string PrivateSessionRequired = "private_session_required";
        public const string UnknownTest = "unknown_test";
        public const string ValidationFailed = "validation_failed";
        public const string ReplayDetected = "replay_detected";
        public const string ClockSkew = "clock_skew";
        public const string NotImplemented = "not_implemented";
        public const string RateLimited = "rate_limited";
        public const string Internal = "internal_error";
    }
}
