using DrivenTestEngine.Core.Models;

namespace DrivenTestEngine.Core.Logging;

/// <summary>
/// Bounded, thread-safe live log. Local entries are always flagged <see cref="DetectionLogEntry.IsLocal"/>
/// so the UI can distinguish engine-side events from server-reported detections.
/// </summary>
public sealed class DetectionLogStore : IDisposable
{
    private readonly object _gate = new();
    private readonly List<DetectionLogEntry> _entries = new();
    private readonly string? _logDirectory;
    private readonly int _capacity;
    private bool _disposed;

    public DetectionLogStore(int capacity = 1000, string? logDirectory = null)
    {
        _capacity = Math.Clamp(capacity, 100, 20000);
        _logDirectory = logDirectory;
    }

    public event EventHandler<DetectionLogEntry>? EntryAdded;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public IReadOnlyList<DetectionLogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    public void Add(DetectionLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _entries.Add(entry);
            if (_entries.Count > _capacity)
            {
                _entries.RemoveRange(0, _entries.Count - _capacity);
            }

        }

        AppendToDisk(entry);
        EntryAdded?.Invoke(this, entry);
    }

    public void Log(DetectionLevel level, DetectionCategory category, string message, string? detail = null, bool isLocal = true)
    {
        Add(new DetectionLogEntry
        {
            Level = level,
            Category = category,
            Message = message,
            Detail = detail,
            IsLocal = isLocal,
        });
    }

    public void Info(DetectionCategory category, string message, string? detail = null) =>
        Log(DetectionLevel.Info, category, message, detail);

    public void Warn(DetectionCategory category, string message, string? detail = null) =>
        Log(DetectionLevel.Warning, category, message, detail);

    public void Error(DetectionCategory category, string message, string? detail = null) =>
        Log(DetectionLevel.Error, category, message, detail);

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private void AppendToDisk(DetectionLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_logDirectory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(_logDirectory, "dte-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            var line = string.Join('\t',
                entry.Timestamp.ToString("O"),
                entry.LevelText,
                entry.CategoryText,
                entry.IsLocal ? "local" : "server",
                entry.Message.Replace('\t', ' '),
                (entry.Detail ?? string.Empty).Replace('\t', ' '));
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // Logging must never take the engine down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _entries.Clear();
        }
    }
}
