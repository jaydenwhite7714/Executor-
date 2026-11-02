using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Mvvm;

namespace DrivenTestEngine.App.ViewModels;

/// <summary>Live detection log: local engine events and server-reported detections, clearly separated.</summary>
public sealed class LogsViewModel : ObservableObject
{
    private const int MaxBuffered = 2000;
    private const int MaxVisible = 700;

    private readonly AppServices _services;
    private readonly List<DetectionLogEntry> _buffer = new();
    private readonly object _gate = new();

    private DetectionLevel _minimumLevel = DetectionLevel.Unknown;
    private string _search = string.Empty;
    private bool _autoScroll;
    private string _countText = "0 entries";

    public LogsViewModel(AppServices services)
    {
        _services = services;
        _autoScroll = services.Settings.Current.AutoScrollLogs;

        Filters.Add(new FilterOption("all", "All"));
        Filters.Add(new FilterOption("info", "Info"));
        Filters.Add(new FilterOption("warn", "Warnings"));
        Filters.Add(new FilterOption("error", "Errors"));
        Filters[0].IsSelected = true;

        SelectFilterCommand = new RelayCommand(SelectFilter);
        ClearCommand = new RelayCommand(Clear);
        CopyCommand = new RelayCommand(Copy);

        services.Log.EntryAdded += (_, entry) => UiDispatcher.Post(() => OnEntryAdded(entry));

        lock (_gate)
        {
            _buffer.AddRange(services.Log.Snapshot());
        }

        Rebuild();
    }

    public ObservableCollection<DetectionLogEntry> Visible { get; } = new();

    public ObservableCollection<FilterOption> Filters { get; } = new();

    public RelayCommand SelectFilterCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand CopyCommand { get; }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
            {
                Rebuild();
            }
        }
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set
        {
            if (SetProperty(ref _autoScroll, value))
            {
                _services.Settings.Update(s => s.AutoScrollLogs = value);
            }
        }
    }

    public string CountText
    {
        get => _countText;
        private set => SetProperty(ref _countText, value);
    }

    public bool HasEntries => Visible.Count > 0;

    private void SelectFilter(object? parameter)
    {
        if (parameter is not FilterOption option)
        {
            return;
        }

        foreach (var filter in Filters)
        {
            filter.IsSelected = ReferenceEquals(filter, option);
        }

        _minimumLevel = option.Key switch
        {
            "warn" => DetectionLevel.Warning,
            "error" => DetectionLevel.Error,
            "info" => DetectionLevel.Info,
            _ => DetectionLevel.Unknown,
        };

        Rebuild();
    }

    private void OnEntryAdded(DetectionLogEntry entry)
    {
        lock (_gate)
        {
            _buffer.Add(entry);
            if (_buffer.Count > MaxBuffered)
            {
                _buffer.RemoveRange(0, _buffer.Count - MaxBuffered);
            }
        }

        if (!Matches(entry))
        {
            return;
        }

        Visible.Add(entry);
        while (Visible.Count > MaxVisible)
        {
            Visible.RemoveAt(0);
        }

        UpdateCount();
    }

    private void Rebuild()
    {
        List<DetectionLogEntry> snapshot;
        lock (_gate)
        {
            snapshot = new List<DetectionLogEntry>(_buffer);
        }

        var filtered = snapshot.Where(Matches).ToList();
        if (filtered.Count > MaxVisible)
        {
            filtered = filtered.Skip(filtered.Count - MaxVisible).ToList();
        }

        Visible.Clear();
        foreach (var entry in filtered)
        {
            Visible.Add(entry);
        }

        UpdateCount();
    }

    private bool Matches(DetectionLogEntry entry)
    {
        if (_minimumLevel != DetectionLevel.Unknown && entry.Level < _minimumLevel)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_search))
        {
            return true;
        }

        var needle = _search.Trim();
        return entry.Message.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || (entry.Detail?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
               || entry.Category.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase)
               || entry.Level.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCount()
    {
        CountText = Visible.Count + (Visible.Count == 1 ? " entry" : " entries");
        OnPropertyChanged(nameof(HasEntries));
    }

    private void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }

        Visible.Clear();
        _services.Log.Clear();
        UpdateCount();
    }

    private void Copy()
    {
        var builder = new StringBuilder();
        foreach (var entry in Visible)
        {
            builder.Append(entry.Timestamp.ToString("HH:mm:ss.fff"))
                .Append("  ")
                .Append(entry.LevelText)
                .Append("  ")
                .Append(entry.CategoryText)
                .Append(entry.IsLocal ? "  [client]  " : "  [server]  ")
                .Append(entry.Message);
            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                builder.Append("  |  ").Append(entry.Detail);
            }

            builder.AppendLine();
        }

        try
        {
            Clipboard.SetText(builder.Length == 0 ? "Driven Test Engine — log is empty." : builder.ToString());
            CountText = "Copied " + Visible.Count + " entries";
        }
        catch (Exception)
        {
            CountText = "Clipboard unavailable";
        }
    }
}
