using System.Text.Json.Serialization;
using DrivenTestEngine.Core.Protocol;

namespace DrivenTestEngine.Core.Models;

[JsonConverter(typeof(LenientEnumConverter<TestRunState>))]
public enum TestRunState
{
    Unknown = 0,
    Queued,
    Running,
    Passed,
    Failed,
    Errored,
    Rejected,
    Cancelled,
    Expired,
}

public static class TestRunStateExtensions
{
    public static bool IsTerminal(this TestRunState state) => state is
        TestRunState.Passed or TestRunState.Failed or TestRunState.Errored or
        TestRunState.Rejected or TestRunState.Cancelled or TestRunState.Expired;

    public static bool IsSuccess(this TestRunState state) => state == TestRunState.Passed;

    public static string ToDisplayText(this TestRunState state) => state switch
    {
        TestRunState.Queued => "Queued",
        TestRunState.Running => "Running",
        TestRunState.Passed => "Passed",
        TestRunState.Failed => "Failed",
        TestRunState.Errored => "Error",
        TestRunState.Rejected => "Rejected",
        TestRunState.Cancelled => "Cancelled",
        TestRunState.Expired => "Expired",
        _ => "Unknown",
    };
}

/// <summary>A single server-issued validation check that contributed to a run result.</summary>
public sealed class TestCheck
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Authoritative state of a test run as reported by the game's server.</summary>
public sealed class TestRunStatus
{
    public string RunId { get; set; } = string.Empty;
    public string TestId { get; set; } = string.Empty;
    public TestRunState State { get; set; } = TestRunState.Unknown;

    /// <summary>Measured value, only present once the server completed the run.</summary>
    public double? Metric { get; set; }

    public string? Unit { get; set; }
    public double? Threshold { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<TestCheck> Checks { get; set; } = new();
    public Dictionary<string, string>? Evidence { get; set; }
    public string? ServerInstanceId { get; set; }
}

public sealed class StartTestRequest
{
    public string TestId { get; set; } = string.Empty;
    public Dictionary<string, double>? Parameters { get; set; }
    public string ClientRunId { get; set; } = string.Empty;
}

public sealed class StartTestResponse
{
    public string RunId { get; set; } = string.Empty;
    public TestRunState State { get; set; } = TestRunState.Queued;
}
