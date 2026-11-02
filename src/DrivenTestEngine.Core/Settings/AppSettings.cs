namespace DrivenTestEngine.Core.Settings;

/// <summary>Everything the overlay remembers between launches.</summary>
public sealed class AppSettings
{
    public string ServerUrl { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;

    /// <summary>DPAPI-protected signing secret (current Windows user only). Never stored in clear text.</summary>
    public string? ProtectedSecret { get; set; }

    public string? ExpectedRobloxUserId { get; set; }
    public string ClientId { get; set; } = string.Empty;

    public bool KeepOnTop { get; set; } = true;
    public bool AutoConnect { get; set; } = false;
    public bool StartCollapsed { get; set; } = false;
    public bool AutoScrollLogs { get; set; } = true;
    public bool ReduceMotion { get; set; } = false;

    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public double OverlayWidth { get; set; } = 432;
    public double OverlayHeight { get; set; } = 660;

    public string LastPage { get; set; } = "tests";

    public void Sanitize()
    {
        OverlayWidth = Math.Clamp(OverlayWidth, 360, 900);
        OverlayHeight = Math.Clamp(OverlayHeight, 420, 1600);
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            ClientId = Guid.NewGuid().ToString("N");
        }
    }
}
