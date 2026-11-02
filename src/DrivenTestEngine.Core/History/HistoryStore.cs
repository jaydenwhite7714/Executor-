using System.Globalization;
using System.Text;
using System.Text.Json;
using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Tests;

namespace DrivenTestEngine.Core.History;

/// <summary>One completed (or failed) server-side run, exactly as reported by the testing service.</summary>
public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TestId { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public TestRunState State { get; set; } = TestRunState.Unknown;
    public double? Metric { get; set; }
    public string? Unit { get; set; }
    public double? Threshold { get; set; }
    public string? RunId { get; set; }
    public string? Message { get; set; }
    public string? ServerInstanceId { get; set; }
    public string? SessionId { get; set; }
    public bool WasAuthoritative { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public string OutcomeText => State.ToDisplayText();

    [System.Text.Json.Serialization.JsonIgnore]
    public string MetricText => Metric.HasValue
        ? Metric.Value.ToString("0.##", CultureInfo.CurrentCulture) + (string.IsNullOrWhiteSpace(Unit) ? string.Empty : " " + Unit)
        : "—";

    [System.Text.Json.Serialization.JsonIgnore]
    public string WhenText => StartedAt.ToLocalTime().ToString("MMM d, HH:mm:ss", CultureInfo.CurrentCulture);
}

/// <summary>Local run history. Persisted to the user's AppData folder with atomic writes.</summary>
public sealed class HistoryStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly int _capacity;
    private readonly List<HistoryEntry> _entries = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public HistoryStore(string filePath, int capacity = 500)
    {
        _path = filePath;
        _capacity = Math.Clamp(capacity, 20, 5000);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            _entries.Clear();
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(json, _json);
                    if (loaded is not null)
                    {
                        _entries.AddRange(loaded);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Add(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.TestName))
        {
            entry.TestName = TestCatalog.DisplayNameFor(entry.TestId);
        }

        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > _capacity)
            {
                _entries.RemoveRange(_capacity, _entries.Count - _capacity);
            }
        }

        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        }

        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string ToCsv()
    {
        var builder = new StringBuilder();
        builder.AppendLine("startedAt,test,state,metric,unit,threshold,runId,serverInstance,message");
        foreach (var entry in Entries)
        {
            builder.AppendLine(string.Join(',',
                Escape(entry.StartedAt.ToString("O", CultureInfo.InvariantCulture)),
                Escape(entry.TestName),
                Escape(entry.State.ToDisplayText()),
                Escape(entry.Metric?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty),
                Escape(entry.Unit ?? string.Empty),
                Escape(entry.Threshold?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty),
                Escape(entry.RunId ?? string.Empty),
                Escape(entry.ServerInstanceId ?? string.Empty),
                Escape(entry.Message ?? string.Empty)));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<HistoryEntry> snapshot;
            lock (_gate)
            {
                snapshot = new List<HistoryEntry>(_entries);
            }

            var json = JsonSerializer.Serialize(snapshot, _json);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
