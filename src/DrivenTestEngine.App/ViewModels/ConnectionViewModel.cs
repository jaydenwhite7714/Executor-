using System.Collections.ObjectModel;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.Core.Engine;
using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Mvvm;
using DrivenTestEngine.Core.Security;
using DrivenTestEngine.Core.Transport;

namespace DrivenTestEngine.App.ViewModels;

/// <summary>Connection + overlay preferences. Nothing here pretends a link exists before the handshake passes.</summary>
public sealed class ConnectionViewModel : ObservableObject
{
    private readonly AppServices _services;

    private string _serverUrl = string.Empty;
    private string _keyId = string.Empty;
    private string _secret = string.Empty;
    private string _expectedUserId = string.Empty;
    private ConnectionState _state = ConnectionState.Disconnected;
    private string _statusText = "Offline";
    private string _statusDetail = "No testing service configured.";
    private string? _errorText;
    private string _probeText = string.Empty;
    private bool _probeSucceeded;
    private bool _isBusy;
    private ServerSession? _session;
    private bool _keepOnTop;
    private bool _autoConnect;
    private bool _autoScrollLogs;
    private bool _reduceMotion;
    private string _secretHint = string.Empty;

    public ConnectionViewModel(AppServices services)
    {
        _services = services;

        var settings = services.Settings.Current;
        _serverUrl = settings.ServerUrl ?? string.Empty;
        _keyId = settings.KeyId ?? string.Empty;
        _expectedUserId = settings.ExpectedRobloxUserId ?? string.Empty;
        _keepOnTop = settings.KeepOnTop;
        _autoConnect = settings.AutoConnect;
        _autoScrollLogs = settings.AutoScrollLogs;
        _reduceMotion = settings.ReduceMotion;

        var storedSecret = services.Settings.ReadSecret();
        if (!string.IsNullOrEmpty(storedSecret))
        {
            _secret = storedSecret;
            _secretHint = "Loaded from the Windows credential store (DPAPI).";
        }
        else if (services.Settings.SecretWarning is not null)
        {
            _secretHint = services.Settings.SecretWarning;
        }

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && !IsConnected);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected || State == ConnectionState.Connecting);
        TestConnectionCommand = new AsyncRelayCommand(ProbeAsync, () => !IsBusy);

        services.Engine.StateChanged += (_, snapshot) => UiDispatcher.Post(() => ApplySnapshot(snapshot));
        ApplySnapshot(services.Engine.Snapshot);
    }

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand TestConnectionCommand { get; }

    public ObservableCollection<string> Capabilities { get; } = new();

    public string ServerUrl
    {
        get => _serverUrl;
        set
        {
            if (SetProperty(ref _serverUrl, value))
            {
                OnPropertyChanged(nameof(IsLocalReferenceProfile), nameof(AccountText), nameof(DeveloperBadgeText), nameof(StateText));
                _services.Settings.Update(s => s.ServerUrl = value.Trim());
                if (!IsConnected)
                {
                    _services.ReconfigureEngine();
                }
            }
        }
    }

    public string KeyId
    {
        get => _keyId;
        set
        {
            if (SetProperty(ref _keyId, value))
            {
                OnPropertyChanged(nameof(IsLocalReferenceProfile), nameof(AccountText), nameof(DeveloperBadgeText), nameof(StateText));
                _services.Settings.Update(s => s.KeyId = value.Trim());
                if (!IsConnected)
                {
                    _services.ReconfigureEngine();
                }
            }
        }
    }

    /// <summary>Kept in memory; persisted with DPAPI when connecting or when the overlay closes.</summary>
    public string Secret
    {
        get => _secret;
        set
        {
            if (!SetProperty(ref _secret, value))
            {
                return;
            }

            SecretHint = string.Empty;
            if (!IsConnected)
            {
                _services.ReconfigureEngine();
            }
        }
    }

    public string SecretHint
    {
        get => _secretHint;
        private set => SetProperty(ref _secretHint, value);
    }

    public string ExpectedUserId
    {
        get => _expectedUserId;
        set
        {
            if (SetProperty(ref _expectedUserId, value))
            {
                _services.Settings.Update(s => s.ExpectedRobloxUserId = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
                if (!IsConnected)
                {
                    _services.ReconfigureEngine();
                }
            }
        }
    }

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsConnected), nameof(StateText), nameof(StatusPillText));
                RaiseCommandStates();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public string StatusPillText => State switch
    {
        ConnectionState.Connected => "Online",
        ConnectionState.Connecting => "Linking",
        ConnectionState.Faulted => "Fault",
        ConnectionState.Disconnecting => "Closing",
        _ => "Offline",
    };

    public string StateText => State switch
    {
        ConnectionState.Connected when IsLocalReferenceProfile => "Authenticated API · local preview",
        ConnectionState.Connected => "Authenticated service session",
        ConnectionState.Connecting => "Handshaking with your testing service…",
        ConnectionState.Disconnecting => "Closing session…",
        ConnectionState.Faulted => "Connection faulted",
        _ => "Not connected",
    };

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string ProbeText
    {
        get => _probeText;
        private set
        {
            if (SetProperty(ref _probeText, value))
            {
                OnPropertyChanged(nameof(HasProbe));
            }
        }
    }

    public bool HasProbe => !string.IsNullOrWhiteSpace(ProbeText);

    public bool ProbeSucceeded
    {
        get => _probeSucceeded;
        private set => SetProperty(ref _probeSucceeded, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsConnected => State == ConnectionState.Connected && _session is not null;

    public ServerSession? Session
    {
        get => _session;
        private set
        {
            if (SetProperty(ref _session, value))
            {
                OnPropertyChanged(
                    nameof(IsConnected),
                    nameof(HasSession),
                    nameof(AccountText),
                    nameof(PlaceText),
                    nameof(InstanceText),
                    nameof(SessionIdText),
                    nameof(ExpiryText),
                    nameof(ServerVersionText),
                    nameof(DeveloperBadgeText));
            }
        }
    }

    public bool HasSession => _session is not null;

    public bool IsLocalReferenceProfile => Uri.TryCreate(_serverUrl, UriKind.Absolute, out var uri)
        && uri.IsLoopback && uri.Port == 8791
        && (_keyId.StartsWith("dte-local-", StringComparison.Ordinal) || _keyId == "dte-dev-key");

    public string AccountText => _session is null
        ? "—"
        : IsLocalReferenceProfile
            ? "Local reference · Roblox identity not verified"
            : _session.Account.DisplayName + " · " + _session.Account.RobloxUserId;

    public string PlaceText => string.IsNullOrWhiteSpace(_session?.TestSession.PlaceId) ? "—" : _session!.TestSession.PlaceId;

    public string InstanceText => string.IsNullOrWhiteSpace(_session?.TestSession.ServerInstanceId)
        ? "—"
        : _session!.TestSession.ServerInstanceId!;

    public string SessionIdText => string.IsNullOrWhiteSpace(_session?.TestSession.SessionId) ? "—" : _session!.TestSession.SessionId;

    public string ExpiryText => _session is null
        ? "—"
        : _session.ExpiresAt.ToLocalTime().ToString("HH:mm:ss");

    public string ServerVersionText => string.IsNullOrWhiteSpace(_session?.ServerVersion) ? "—" : _session!.ServerVersion;

    public string DeveloperBadgeText => IsLocalReferenceProfile
        ? "LOCAL DEMO · NOT VERIFIED"
        : _session?.Account.IsDeveloper == true
            ? "DEVELOPER VERIFIED"
            : "NOT VERIFIED";

    public bool KeepOnTop
    {
        get => _keepOnTop;
        set
        {
            if (SetProperty(ref _keepOnTop, value))
            {
                _services.Settings.Update(s => s.KeepOnTop = value);
            }
        }
    }

    public bool AutoConnect
    {
        get => _autoConnect;
        set
        {
            if (SetProperty(ref _autoConnect, value))
            {
                _services.Settings.Update(s => s.AutoConnect = value);
            }
        }
    }

    public bool AutoScrollLogs
    {
        get => _autoScrollLogs;
        set
        {
            if (SetProperty(ref _autoScrollLogs, value))
            {
                _services.Settings.Update(s => s.AutoScrollLogs = value);
            }
        }
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (SetProperty(ref _reduceMotion, value))
            {
                MotionSettings.Enabled = !value;
                _services.Settings.Update(s => s.ReduceMotion = value);
            }
        }
    }

    /// <summary>Writes the in-memory secret through DPAPI and saves the rest of the form.</summary>
    public void ReportLocalServiceError(string message)
    {
        ErrorText = message;
        StatusText = "Local service unavailable";
        StatusDetail = message;
    }

    public void Persist()
    {
        var secret = _secret?.Trim() ?? string.Empty;
        var persisted = _services.Settings.WriteSecret(string.IsNullOrEmpty(secret) ? null : secret);
        if (!persisted && !string.IsNullOrEmpty(secret))
        {
            SecretHint = _services.Settings.SecretWarning ?? "The secret could not be stored securely.";
        }
        else if (!string.IsNullOrEmpty(secret))
        {
            SecretHint = "Stored with Windows DPAPI for this user account.";
        }

        _services.Settings.Update(s =>
        {
            s.ServerUrl = (_serverUrl ?? string.Empty).Trim();
            s.KeyId = (_keyId ?? string.Empty).Trim();
            s.ExpectedRobloxUserId = string.IsNullOrWhiteSpace(_expectedUserId) ? null : _expectedUserId.Trim();
        });
    }

    private async Task ConnectAsync()
    {
        ErrorText = null;
        ProbeText = string.Empty;

        var validation = ValidateForm();
        if (validation is not null)
        {
            ErrorText = validation;
            return;
        }

        Persist();
        IsBusy = true;
        try
        {
            _services.ReconfigureEngine();
            var snapshot = await _services.Engine.ConnectAsync().ConfigureAwait(true);
            ApplySnapshot(snapshot);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectAsync()
    {
        IsBusy = true;
        try
        {
            await _services.Engine.DisconnectAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ProbeAsync()
    {
        ProbeText = string.Empty;
        ProbeSucceeded = false;

        var validation = ValidateForm();
        if (validation is not null)
        {
            ProbeText = validation;
            return;
        }

        Persist();
        IsBusy = true;
        try
        {
            var uri = BuildUri();
            if (uri is null)
            {
                ProbeText = "The server URL is not a valid absolute URL.";
                return;
            }

            using var transport = new HttpTestServerTransport(
                uri,
                new ClientCredentials(_keyId.Trim(), _secret.Trim()),
                _services.VersionText,
                _services.Settings.Current.ClientId);

            var health = await transport.CheckHealthAsync(CancellationToken.None).ConfigureAwait(true);
            if (health.IsSuccess)
            {
                var version = string.IsNullOrWhiteSpace(health.Value!.ServerVersion) ? "unknown" : health.Value.ServerVersion;
                ProbeText = "Service reachable · " + (health.Value.Game ?? "testing service") + " · v" + version;
                ProbeSucceeded = true;
            }
            else
            {
                ProbeText = health.Failure!.DisplayMessage;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? ValidateForm()
    {
        var uri = BuildUri();
        if (uri is null)
        {
            return "Enter the https URL of your testing service (for example https://dte.example.com/).";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !IsLoopback(uri))
        {
            return "The testing service URL must use https. Plain http is only allowed for localhost development.";
        }

        if (string.IsNullOrWhiteSpace(_keyId))
        {
            return "Enter the key id issued by your testing service.";
        }

        if (string.IsNullOrWhiteSpace(_secret))
        {
            return "Enter the signing secret that belongs to that key id.";
        }

        if (_secret.Trim().Length < 32)
        {
            return "The signing secret must be at least 32 characters.";
        }

        return null;
    }

    private Uri? BuildUri()
    {
        var raw = (_serverUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return Uri.TryCreate(raw, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    private void ApplySnapshot(ConnectionSnapshot snapshot)
    {
        IsBusy = snapshot.State == ConnectionState.Connecting;
        State = snapshot.State;
        StatusText = snapshot.IsConnected && IsLocalReferenceProfile
            ? "Authenticated API communication · local preview"
            : snapshot.Message;
        StatusDetail = snapshot.IsConnected && IsLocalReferenceProfile
            ? "Signed health check and handshake accepted. This local demo does not verify Roblox identity or game access."
            : snapshot.Failure?.DisplayMessage ?? snapshot.Message;
        ErrorText = snapshot.State == ConnectionState.Faulted ? snapshot.Failure?.DisplayMessage ?? snapshot.Message : null;
        Session = snapshot.Session;

        Capabilities.Clear();
        if (snapshot.Session is not null)
        {
            foreach (var capability in snapshot.Session.Capabilities)
            {
                Capabilities.Add(capability);
            }
        }

        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        TestConnectionCommand.RaiseCanExecuteChanged();
    }
}
