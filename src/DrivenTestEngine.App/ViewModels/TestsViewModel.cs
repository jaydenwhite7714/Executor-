using System.Collections.ObjectModel;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.Core.Engine;
using DrivenTestEngine.Core.Mvvm;
using DrivenTestEngine.Core.Tests;

namespace DrivenTestEngine.App.ViewModels;

/// <summary>The four server-run tests. Tiles stay disabled until a verified session exists.</summary>
public sealed class TestsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private bool _isConnected;
    private string _gateMessage = "Connect to your testing service to unlock server-run tests.";
    private string _gateTitle = "No verified session";
    private string _summaryText = "0 of 4 tests available";

    public TestsViewModel(AppServices services)
    {
        _services = services;

        foreach (var definition in TestCatalog.All)
        {
            Cards.Add(new TestCardViewModel(services, definition));
        }

        GoToConnectionCommand = new RelayCommand(() => ConnectionRequested?.Invoke(this, EventArgs.Empty));

        services.Engine.StateChanged += (_, snapshot) => UiDispatcher.Post(() => ApplySnapshot(snapshot));
        ApplySnapshot(services.Engine.Snapshot);
    }

    public ObservableCollection<TestCardViewModel> Cards { get; } = new();

    public RelayCommand GoToConnectionCommand { get; }

    public event EventHandler? ConnectionRequested;

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public string GateTitle
    {
        get => _gateTitle;
        private set => SetProperty(ref _gateTitle, value);
    }

    public string GateMessage
    {
        get => _gateMessage;
        private set => SetProperty(ref _gateMessage, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public bool IsGated => !IsConnected;

    private void ApplySnapshot(ConnectionSnapshot snapshot)
    {
        var connected = snapshot.IsConnected;
        IsConnected = connected;
        OnPropertyChanged(nameof(IsGated));

        var session = snapshot.Session;
        var available = 0;

        foreach (var card in Cards)
        {
            card.IsConnected = connected;
            card.IsAvailable = session?.AllowsTest(card.Id) ?? false;
            if (card.IsAvailable)
            {
                available++;
            }
        }

        if (connected && session is not null)
        {
            var settings = _services.Settings.Current;
            var localPreview = Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var uri)
                && uri.IsLoopback && uri.Port == 8791
                && (settings.KeyId.StartsWith("dte-local-", StringComparison.Ordinal)
                    || settings.KeyId == "dte-dev-key");
            if (localPreview)
            {
                GateTitle = "Authenticated API · local preview";
                GateMessage = "Signed communication with the local reference service is working. Roblox identity is not verified; no game tests are enabled.";
            }
            else
            {
                GateTitle = "Authenticated service session";
                GateMessage = "Service authenticated for " + session.Account.DisplayName +
                              " · place " + session.TestSession.PlaceId + ". Only tests explicitly enabled by the server can run.";
            }
        }
        else if (snapshot.State == ConnectionState.Faulted)
        {
            GateTitle = "Connection faulted";
            GateMessage = snapshot.Failure?.DisplayMessage ?? snapshot.Message;
        }
        else
        {
            GateTitle = "No verified session";
            GateMessage = "Connect to your testing service to unlock server-run tests.";
        }

        SummaryText = available + " of " + Cards.Count + " tests available";
    }
}
