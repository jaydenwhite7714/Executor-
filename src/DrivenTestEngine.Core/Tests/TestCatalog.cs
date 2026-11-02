namespace DrivenTestEngine.Core.Tests;

/// <summary>One numeric input the operator can tune before a server-side run.</summary>
public sealed class TestParameter
{
    public string Id { get; init; } = "value";
    public string Label { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 10;
    public double Default { get; init; }
    public double Step { get; init; } = 1;
    public int Decimals { get; init; }

    public double Clamp(double value) => Math.Clamp(value, Minimum, Maximum);

    public string Format(double value) =>
        value.ToString("F" + Decimals.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Client-side presentation metadata for a server-side test. The game server owns the actual
/// measurement: this catalogue only tells the UI what to show and which parameter to send.
/// </summary>
public sealed class TestDefinition
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Measures { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string IconKey { get; init; } = string.Empty;
    public TestParameter? Parameter { get; init; }
}

public static class TestCatalog
{
    public const string Speed = "speed";
    public const string Jump = "jump";
    public const string AutoCatch = "auto-catch";
    public const string DimeAccuracy = "dime-accuracy";

    public static IReadOnlyList<TestDefinition> All { get; } = new List<TestDefinition>
    {
        new()
        {
            Id = Speed,
            DisplayName = "Speed Test",
            Subtitle = "Sprint & acceleration",
            Description = "Requests a timed sprint from the game server and records the authoritative velocity curve it reports back.",
            Measures = "Peak studs/second plus acceleration ramp, measured on the server for the test avatar.",
            Unit = "studs/s",
            IconKey = "Speed",
            Parameter = new TestParameter
            {
                Id = "durationSeconds",
                Label = "Sample window",
                Unit = "s",
                Minimum = 1,
                Maximum = 10,
                Default = 4,
                Step = 1,
            },
        },
        new()
        {
            Id = Jump,
            DisplayName = "Jump Test",
            Subtitle = "Apex height & cooldown",
            Description = "Asks the server to sample the test avatar's jump apex, airtime and jump cooldown across several attempts.",
            Measures = "Apex height, airtime and cooldown between jumps, as observed by the server.",
            Unit = "studs",
            IconKey = "Jump",
            Parameter = new TestParameter
            {
                Id = "attempts",
                Label = "Attempts",
                Unit = "x",
                Minimum = 1,
                Maximum = 5,
                Default = 3,
                Step = 1,
            },
        },
        new()
        {
            Id = AutoCatch,
            DisplayName = "Auto Catch Test",
            Subtitle = "Automation detection",
            Description = "The server opens a series of scripted catch windows and checks whether the avatar reacts inside human bounds, flagging automation.",
            Measures = "Reaction latency distribution and automation verdict issued by the game's anti-cheat.",
            Unit = "windows",
            IconKey = "Catch",
            Parameter = new TestParameter
            {
                Id = "windows",
                Label = "Catch windows",
                Unit = "x",
                Minimum = 2,
                Maximum = 10,
                Default = 5,
                Step = 1,
            },
        },
        new()
        {
            Id = DimeAccuracy,
            DisplayName = "Dime Accuracy Test",
            Subtitle = "Precision placement",
            Description = "Runs the dime drill in your private test session and scores placement precision against shrinking target windows.",
            Measures = "Average placement error across targets and the resulting accuracy percentage.",
            Unit = "%",
            IconKey = "Dime",
            Parameter = new TestParameter
            {
                Id = "targets",
                Label = "Targets",
                Unit = "x",
                Minimum = 3,
                Maximum = 15,
                Default = 8,
                Step = 1,
            },
        },
    };

    public static TestDefinition? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public static bool Contains(string? id) => Find(id) is not null;

    public static string DisplayNameFor(string? id) => Find(id)?.DisplayName ?? (string.IsNullOrWhiteSpace(id) ? "Unknown test" : id);
}
