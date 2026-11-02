using System.Text.Json.Serialization;

namespace DrivenTestEngine.Core.Models;

/// <summary>Roblox account the testing service authenticated the session against.</summary>
public sealed class AccountInfo
{
    public string RobloxUserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Set by the server when the account is on the developer allow-list.</summary>
    public bool IsDeveloper { get; set; }
}

/// <summary>The private test session the testing service resolved for this handshake.</summary>
public sealed class TestSessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string PlaceId { get; set; } = string.Empty;
    public string UniverseId { get; set; } = string.Empty;
    public string? ServerInstanceId { get; set; }

    /// <summary>True only when the server positively identified a private, developer-owned test session.</summary>
    public bool IsPrivateTestSession { get; set; }
}

/// <summary>Result of a successful handshake. Nothing is cached until this object validates.</summary>
public sealed class ServerSession
{
    public string SessionToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string ServerVersion { get; set; } = string.Empty;
    public AccountInfo Account { get; set; } = new();
    public TestSessionInfo TestSession { get; set; } = new();
    public List<string> Capabilities { get; set; } = new();
    public List<string> AllowedTests { get; set; } = new();

    [JsonIgnore]
    public bool HasToken => !string.IsNullOrWhiteSpace(SessionToken);

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public bool SupportsCapability(string capability) =>
        Capabilities.Exists(c => string.Equals(c, capability, StringComparison.OrdinalIgnoreCase));

    public bool AllowsTest(string testId) =>
        AllowedTests.Count == 0 || AllowedTests.Exists(t => string.Equals(t, testId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Client-side mirror of the rules the server must also enforce: a developer account,
    /// a private test session, a live token and (when configured) the expected account id.
    /// </summary>
    public SessionAuthorization Authorize(DateTimeOffset now, string? expectedRobloxUserId)
    {
        var problems = new List<string>();

        if (!HasToken)
        {
            problems.Add("The server did not return a session token.");
        }

        if (IsExpired(now))
        {
            problems.Add("The server session token is already expired.");
        }

        if (!Account.IsDeveloper)
        {
            problems.Add("This Roblox account is not on the server's developer allow-list.");
        }

        if (!TestSession.IsPrivateTestSession)
        {
            problems.Add("The server did not confirm a private test session. Public sessions are refused.");
        }

        if (!string.IsNullOrWhiteSpace(expectedRobloxUserId)
            && !string.Equals(expectedRobloxUserId.Trim(), Account.RobloxUserId, StringComparison.Ordinal))
        {
            problems.Add($"Connected as Roblox user {Account.RobloxUserId}, expected {expectedRobloxUserId.Trim()}.");
        }

        return new SessionAuthorization(problems.Count == 0, problems);
    }
}

public sealed record SessionAuthorization(bool IsAuthorized, IReadOnlyList<string> Problems);
