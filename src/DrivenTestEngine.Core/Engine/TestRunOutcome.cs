using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Transport;

namespace DrivenTestEngine.Core.Engine;

/// <summary>Result of asking the server to run a test, including the authoritative status it reported.</summary>
public sealed class TestRunOutcome
{
    private TestRunOutcome()
    {
    }

    public TestRunStatus? Status { get; private init; }
    public TransportFailure? Failure { get; private init; }

    public bool IsAccepted => Failure is null && Status is not null;

    public static TestRunOutcome Ok(TestRunStatus status) => new() { Status = status };

    public static TestRunOutcome Fail(TransportFailure failure) => new() { Failure = failure };

    public string Summary => Failure is not null
        ? Failure.DisplayMessage
        : Status is null
            ? "No result."
            : Status.State.ToDisplayText() +
              (Status.Metric.HasValue ? " · " + Status.Metric.Value.ToString("0.##") + " " + (Status.Unit ?? string.Empty) : string.Empty);
}
