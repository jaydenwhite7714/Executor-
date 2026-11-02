using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.Core.Engine;
using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Mvvm;
using DrivenTestEngine.Core.Tests;
using DrivenTestEngine.Core.Transport;

namespace DrivenTestEngine.App.ViewModels;

/// <summary>One test tile. Every value shown comes from the server's last reply for this tile.</summary>
public sealed class TestCardViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly AsyncRelayCommand _runCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _togglePreviewCommand;

    private CancellationTokenSource? _cancellation;
    private TestRunState _state = TestRunState.Unknown;
    private string _statusText = "Not run";
    private string _metricText = "—";
    private string _resultDetail = string.Empty;
    private string _lastRunText = "never";
    private double _parameterValue;
    private bool _isRunning;
    private bool _isConnected;
    private bool _isAvailable = true;
    private bool _previewEnabled;
    private double _previewValue = 50;

    public TestCardViewModel(AppServices services, TestDefinition definition)
    {
        _services = services;
        Definition = definition;
        _parameterValue = definition.Parameter?.Default ?? 0;

        _runCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
        _runCommand.Failed += (_, ex) =>
        {
            IsRunning = false;
            State = TestRunState.Errored;
            StatusText = "Error";
            ResultDetail = ex.Message;
        };

        _cancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsRunning);
        _togglePreviewCommand = new RelayCommand(() => PreviewEnabled = !PreviewEnabled, () => HasLocalPreview);
    }

    public TestDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Name => Definition.DisplayName;

    public string Subtitle => Definition.Subtitle;

    public string Description => Definition.Description;

    public string Measures => Definition.Measures;

    public string Unit => Definition.Unit;

    public Geometry? IconGeometry => IconLibrary.Get(Definition.IconKey);

    public AsyncRelayCommand RunCommand => _runCommand;

    public RelayCommand CancelCommand => _cancelCommand;

    /// <summary>Speed and jump expose UI-only controls; they never send values to Roblox.</summary>
    public bool HasLocalPreview => Id is TestCatalog.Speed or TestCatalog.Jump;

    public bool PreviewEnabled
    {
        get => _previewEnabled;
        set
        {
            if (SetProperty(ref _previewEnabled, value))
            {
                OnPropertyChanged(nameof(PreviewToggleText), nameof(PreviewStateText));
            }
        }
    }

    public string PreviewToggleText => PreviewEnabled ? "ON" : "OFF";

    public string PreviewStateText => PreviewEnabled ? "Local preview enabled · UI only" : "Local preview off · no game effect";

    public RelayCommand TogglePreviewCommand => _togglePreviewCommand;

    public double PreviewValue
    {
        get => _previewValue;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _previewValue, clamped))
            {
                OnPropertyChanged(nameof(PreviewValueText));
            }
        }
    }

    public string PreviewValueText => ((int)Math.Round(PreviewValue)).ToString(CultureInfo.InvariantCulture) + "% (UI only)";

    public bool IsUnsupportedIntegration => Id is TestCatalog.AutoCatch or TestCatalog.DimeAccuracy;

    public string IntegrationStatus => IsUnsupportedIntegration
        ? "Unavailable · requires Driven Football server integration"
        : string.Empty;

    public bool HasParameter => Definition.Parameter is not null;

    public string ParameterLabel => Definition.Parameter?.Label ?? string.Empty;

    public double ParameterMinimum => Definition.Parameter?.Minimum ?? 0;

    public double ParameterMaximum => Definition.Parameter?.Maximum ?? 1;

    public double ParameterStep => Definition.Parameter?.Step ?? 1;

    /// <summary>Client-side convenience clamp only; the server validates the value again.</summary>
    public double ParameterValue
    {
        get => _parameterValue;
        set
        {
            var clamped = Definition.Parameter?.Clamp(value) ?? value;
            if (SetProperty(ref _parameterValue, clamped))
            {
                OnPropertyChanged(nameof(ParameterText));
            }
        }
    }

    public string ParameterText
    {
        get
        {
            if (Definition.Parameter is null)
            {
                return string.Empty;
            }

            var value = Definition.Parameter.Format(_parameterValue);
            var unit = string.IsNullOrWhiteSpace(Definition.Parameter.Unit) ? string.Empty : " " + Definition.Parameter.Unit;
            return value + unit;
        }
    }

    public TestRunState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(HasResult), nameof(StateText));
            }
        }
    }

    public bool HasResult => State is not (TestRunState.Unknown or TestRunState.Queued or TestRunState.Running);

    public string StateText => State.ToDisplayText().ToUpperInvariant();

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string MetricText
    {
        get => _metricText;
        private set => SetProperty(ref _metricText, value);
    }

    public string ResultDetail
    {
        get => _resultDetail;
        private set
        {
            if (SetProperty(ref _resultDetail, value))
            {
                OnPropertyChanged(nameof(HasDetail));
            }
        }
    }

    public bool HasDetail => !string.IsNullOrWhiteSpace(ResultDetail);

    public string LastRunText
    {
        get => _lastRunText;
        private set => SetProperty(ref _lastRunText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanRun), nameof(RunButtonText));
                _runCommand.RaiseCanExecuteChanged();
                _cancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(CanRun), nameof(AvailabilityHint));
                _runCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(CanRun), nameof(AvailabilityHint));
                _runCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanRun => IsConnected && IsAvailable && !IsRunning;

    public string RunButtonText => IsRunning ? "Running…" : "Run test";

    public string AvailabilityHint => !IsConnected
        ? "Connect to run"
        : !IsAvailable
            ? "Not enabled for this session"
            : string.Empty;

    private async Task RunAsync()
    {
        IsRunning = true;
        ResultDetail = string.Empty;
        MetricText = "—";
        State = TestRunState.Queued;
        StatusText = "Sending request to the testing service…";
        _cancellation = new CancellationTokenSource();

        try
        {
            var parameters = Definition.Parameter is null
                ? null
                : new Dictionary<string, double>(StringComparer.Ordinal) { [Definition.Parameter.Id] = _parameterValue };

            var progress = new Progress<TestRunStatus>(status =>
            {
                State = status.State;
                StatusText = "Server: " + status.State.ToDisplayText().ToLowerInvariant();
            });

            var outcome = await _services.Engine
                .RunTestAsync(Definition.Id, parameters, progress, _cancellation.Token)
                .ConfigureAwait(true);

            if (outcome.Failure is not null)
            {
                if (outcome.Failure.Kind == TransportFailureKind.Cancelled)
                {
                    State = TestRunState.Cancelled;
                    StatusText = "Cancelled";
                    ResultDetail = "Stopped locally. The server may still be finishing the run.";
                }
                else
                {
                    State = TestRunState.Errored;
                    StatusText = "No result";
                    ResultDetail = outcome.Failure.DisplayMessage;
                }

                return;
            }

            ApplyStatus(outcome.Status!);
        }
        finally
        {
            IsRunning = false;
            _cancellation?.Dispose();
            _cancellation = null;
            _cancelCommand.RaiseCanExecuteChanged();
        }
    }

    private void ApplyStatus(TestRunStatus status)
    {
        State = status.State;

        if (status.Metric.HasValue)
        {
            var unit = string.IsNullOrWhiteSpace(status.Unit) ? string.Empty : " " + status.Unit;
            MetricText = status.Metric.Value.ToString("0.##", CultureInfo.CurrentCulture) + unit;
        }
        else
        {
            MetricText = "—";
        }

        StatusText = status.State.ToDisplayText();
        LastRunText = DateTimeOffset.Now.ToString("HH:mm:ss");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(status.Message))
        {
            parts.Add(status.Message!);
        }

        if (status.Checks.Count > 0)
        {
            var passed = status.Checks.Count(c => c.Passed);
            parts.Add($"Server checks {passed}/{status.Checks.Count} passed");
        }

        if (status.Threshold.HasValue)
        {
            parts.Add("Threshold " + status.Threshold.Value.ToString("0.##", CultureInfo.CurrentCulture) + (string.IsNullOrWhiteSpace(status.Unit) ? string.Empty : " " + status.Unit));
        }

        ResultDetail = string.Join(" · ", parts);
    }
}
