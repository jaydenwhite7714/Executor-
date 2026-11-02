using System.Collections.ObjectModel;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.Core.Engine;
using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Mvvm;

namespace DrivenTestEngine.App.ViewModels;

public sealed class SignalRow
{
    public SignalRow(AntiCheatSignal signal)
    {
        Signal = signal;
    }

    public AntiCheatSignal Signal { get; }

    public AntiCheatSeverity Severity => Signal.Severity;

    public string SeverityText => Signal.Severity.ToString().ToUpperInvariant();

    public string CategoryText => string.IsNullOrWhiteSpace(Signal.Category) ? "GENERAL" : Signal.Category.ToUpperInvariant();

    public string Description => Signal.Description;

    public string TimeText => Signal.DetectedAt == default
        ? "—"
        : Signal.DetectedAt.ToLocalTime().ToString("HH:mm:ss");

    public bool HasSubject => !string.IsNullOrWhiteSpace(Signal.Subject);

    public string SubjectText => Signal.Subject ?? string.Empty;
}

/// <summary>
/// Anti-cheat surface: the game server's own detections plus a real integrity checklist computed
/// from this client's link. The two sources are never blended.
/// </summary>
public sealed class AntiCheatViewModel : ObservableObject
{
    private readonly AppServices _services;

    private bool _isConnected;
    private bool _monitoringEnabled;
    private bool _isBusy;
    private bool _hasLoadedOnce;
    private string _statusText = "Connect to read anti-cheat state from the game server.";
    private string _lastScanText = "never";
    private string _integritySummary = "0 / 0 checks";

    public AntiCheatViewModel(AppServices services)
    {
        _services = services;

        ToggleMonitoringCommand = new AsyncRelayCommand(ToggleMonitoringAsync, () => IsConnected && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsConnected && !IsBusy);

        services.Engine.AntiCheatChanged += (_, status) => UiDispatcher.Post(() => ApplyStatus(status));
        services.Engine.StateChanged += (_, snapshot) => UiDispatcher.Post(() => OnConnectionChanged(snapshot));

        OnConnectionChanged(services.Engine.Snapshot);
    }

    public ObservableCollection<SignalRow> Signals { get; } = new();

    public ObservableCollection<IntegrityCheck> Integrity { get; } = new();

    public AsyncRelayCommand ToggleMonitoringCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(MonitoringLabel));
                ToggleMonitoringCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool MonitoringEnabled
    {
        get => _monitoringEnabled;
        private set
        {
            if (SetProperty(ref _monitoringEnabled, value))
            {
                OnPropertyChanged(nameof(MonitoringLabel));
            }
        }
    }

    public string MonitoringLabel => !IsConnected
        ? "Server monitoring unavailable"
        : MonitoringEnabled
            ? "Server monitoring armed"
            : "Server monitoring disarmed";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ToggleMonitoringCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LastScanText
    {
        get => _lastScanText;
        private set => SetProperty(ref _lastScanText, value);
    }

    public string IntegritySummary
    {
        get => _integritySummary;
        private set => SetProperty(ref _integritySummary, value);
    }

    public bool HasSignals => Signals.Count > 0;

    public string SignalCountText => Signals.Count == 1 ? "1 detection" : Signals.Count + " detections";

    private async Task ToggleMonitoringAsync()
    {
        IsBusy = true;
        try
        {
            var target = !MonitoringEnabled;
            var result = await _services.Engine.SetAntiCheatMonitoringAsync(target).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                ApplyStatus(result.Value!);
                StatusText = target ? "Monitoring armed on the game server." : "Monitoring disarmed on the game server.";
            }
            else
            {
                StatusText = result.Failure!.DisplayMessage;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _services.Engine.RefreshAntiCheatAsync().ConfigureAwait(true);
            StatusText = result.IsSuccess
                ? "Refreshed from the game server."
                : result.Failure!.DisplayMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyStatus(AntiCheatStatus status)
    {
        _hasLoadedOnce = true;
        MonitoringEnabled = status.MonitoringEnabled;
        LastScanText = status.LastScanAt.HasValue
            ? status.LastScanAt.Value.ToLocalTime().ToString("HH:mm:ss")
            : "not reported";

        Signals.Clear();
        foreach (var signal in status.Signals.OrderByDescending(s => s.DetectedAt))
        {
            Signals.Add(new SignalRow(signal));
        }

        OnPropertyChanged(nameof(HasSignals), nameof(SignalCountText));
    }

    private void OnConnectionChanged(ConnectionSnapshot snapshot)
    {
        IsConnected = snapshot.IsConnected;

        Integrity.Clear();
        foreach (var check in _services.Engine.BuildIntegrityChecks())
        {
            Integrity.Add(check);
        }

        var passed = Integrity.Count(c => c.Passed);
        IntegritySummary = passed + " / " + Integrity.Count + " checks";

        if (!snapshot.IsConnected)
        {
            MonitoringEnabled = false;
            _hasLoadedOnce = false;
            if (_hasLoadedOnce)
            {
                StatusText = "Session closed. Server detections are read live and are not cached.";
            }
            else
            {
                StatusText = "Connect to read anti-cheat state from the game server.";
            }
        }
        else if (!_hasLoadedOnce)
        {
            StatusText = "Waiting for the first anti-cheat poll…";
        }
    }
}
