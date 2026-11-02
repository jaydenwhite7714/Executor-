using System.Collections.Generic;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.Core.Engine;
using DrivenTestEngine.Core.Mvvm;

namespace DrivenTestEngine.App.ViewModels;

/// <summary>Root view model for the overlay shell: navigation, status strip and window commands.</summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Dictionary<string, object> _pages;

    private object? _currentPage;
    private ConnectionState _connectionState = ConnectionState.Disconnected;
    private string _statusText = "Not connected";
    private string _sessionText = "no session";
    private bool _isConnected;

    public ShellViewModel(AppServices services)
    {
        _services = services;

        Connection = new ConnectionViewModel(services);
        Tests = new TestsViewModel(services);
        AntiCheat = new AntiCheatViewModel(services);
        Logs = new LogsViewModel(services);
        History = new HistoryViewModel(services);

        _pages = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["tests"] = Tests,
            ["anticheat"] = AntiCheat,
            ["logs"] = Logs,
            ["history"] = History,
            ["connection"] = Connection,
        };

        NavItems = new List<NavItem>
        {
            new("tests", "Tests", "Icon.Tests"),
            new("anticheat", "Monitor", "Icon.Shield"),
            new("logs", "Logs", "Icon.Logs"),
            new("history", "History", "Icon.History"),
            new("connection", "Link", "Icon.Link"),
        };

        NavigateCommand = new RelayCommand(Navigate);
        CollapseCommand = new RelayCommand(() => CollapseRequested?.Invoke(this, EventArgs.Empty));
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        Tests.ConnectionRequested += (_, _) => NavigateTo("connection");

        services.Engine.StateChanged += (_, snapshot) => UiDispatcher.Post(() => ApplySnapshot(snapshot));
        ApplySnapshot(services.Engine.Snapshot);

        var lastPage = services.Settings.Current.LastPage;
        NavigateTo(_pages.ContainsKey(lastPage ?? string.Empty) ? lastPage! : "tests");
    }

    public ConnectionViewModel Connection { get; }

    public TestsViewModel Tests { get; }

    public AntiCheatViewModel AntiCheat { get; }

    public LogsViewModel Logs { get; }

    public HistoryViewModel History { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    public RelayCommand NavigateCommand { get; }

    public RelayCommand CollapseCommand { get; }

    public RelayCommand CloseCommand { get; }

    public event EventHandler? CollapseRequested;

    public event EventHandler? CloseRequested;

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public ConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (SetProperty(ref _connectionState, value))
            {
                OnPropertyChanged(nameof(ConnectionPillText), nameof(IsConnected));
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public string ConnectionPillText => ConnectionState switch
    {
        ConnectionState.Connected => "LIVE",
        ConnectionState.Connecting => "LINKING",
        ConnectionState.Disconnecting => "CLOSING",
        ConnectionState.Faulted => "FAULT",
        _ => "OFFLINE",
    };

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SessionText
    {
        get => _sessionText;
        private set => SetProperty(ref _sessionText, value);
    }

    public string VersionText => _services.BuildStamp;

    public void NavigateTo(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            return;
        }

        CurrentPage = page;

        foreach (var item in NavItems)
        {
            item.IsActive = string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);
        }

        _services.Settings.Update(s => s.LastPage = key.ToLowerInvariant());
    }

    private void Navigate(object? parameter)
    {
        var key = parameter switch
        {
            NavItem item => item.Key,
            string text => text,
            _ => null,
        };

        if (key is not null)
        {
            NavigateTo(key);
        }
    }

    private void ApplySnapshot(ConnectionSnapshot snapshot)
    {
        ConnectionState = snapshot.State;
        IsConnected = snapshot.IsConnected;
        StatusText = snapshot.Message;
        SessionText = snapshot.Session is null
            ? "no session"
            : "place " + snapshot.Session.TestSession.PlaceId + " · " + snapshot.SessionText;
    }
}
