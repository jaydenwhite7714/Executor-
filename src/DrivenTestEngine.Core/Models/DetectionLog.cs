using System.Text.Json.Serialization;
using DrivenTestEngine.Core.Protocol;

namespace DrivenTestEngine.Core.Models;

[JsonConverter(typeof(LenientEnumConverter<DetectionLevel>))]
public enum DetectionLevel
{
    Unknown = 0,
    Trace,
    Info,
    Warning,
    Error,
    Critical,
}

[JsonConverter(typeof(LenientEnumConverter<DetectionCategory>))]
public enum DetectionCategory
{
    Unknown = 0,
    Client,
    Session,
    Test,
    AntiCheat,
    Server,
}

/// <summary>Entry as delivered by the testing service.</summary>
public sealed class ServerLogEntry
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public DetectionLevel Level { get; set; } = DetectionLevel.Info;
    public DetectionCategory Category { get; set; } = DetectionCategory.Server;
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public Dictionary<string, string>? Data { get; set; }
}

public sealed class ServerLogBatch
{
    public List<ServerLogEntry> Entries { get; set; } = new();
    public string? NextCursor { get; set; }
}

/// <summary>Entry shown in the live detection log. Locally produced entries are marked as such.</summary>
public sealed class DetectionLogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public DetectionLevel Level { get; init; } = DetectionLevel.Info;
    public DetectionCategory Category { get; init; } = DetectionCategory.Client;
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public bool IsLocal { get; init; } = true;

    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    public string CategoryText => Category.ToString().ToUpperInvariant();
    public string LevelText => Level.ToString().ToUpperInvariant();
    public string SourceText => IsLocal ? "CLIENT" : "SERVER";
}
