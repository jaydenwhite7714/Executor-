namespace DrivenTestEngine.Core.Models;

/// <summary>Signed body sent when opening a session with the testing service.</summary>
public sealed class HandshakeRequest
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = string.Empty;
    public string Protocol { get; set; } = "1.0";
    public string Platform { get; set; } = "windows";
    public string RequestedScope { get; set; } = "tests:run logs:read anticheat:read anticheat:write";
}
