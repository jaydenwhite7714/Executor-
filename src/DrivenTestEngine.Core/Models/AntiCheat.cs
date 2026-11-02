using System.Text.Json.Serialization;
using DrivenTestEngine.Core.Protocol;

namespace DrivenTestEngine.Core.Models;

[JsonConverter(typeof(LenientEnumConverter<AntiCheatSeverity>))]
public enum AntiCheatSeverity
{
    Unknown = 0,
    Info,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>A detection raised by the game's own server-side anti-cheat during a test session.</summary>
public sealed class AntiCheatSignal
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = "uncategorised";
    public AntiCheatSeverity Severity { get; set; } = AntiCheatSeverity.Info;
    public string Description { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public bool Acknowledged { get; set; }
}

public sealed class AntiCheatStatus
{
    public bool MonitoringEnabled { get; set; }
    public DateTimeOffset? LastScanAt { get; set; }
    public List<AntiCheatSignal> Signals { get; set; } = new();
}

public sealed class AntiCheatMonitorRequest
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Locally computed integrity check. These are real client-side facts (signature failures,
/// clock skew, refused authorisations) and are never mixed with server-reported detections.
/// </summary>
public sealed class IntegrityCheck
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string Detail { get; init; } = string.Empty;
}
