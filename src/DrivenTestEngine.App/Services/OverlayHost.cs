using System.Windows;
using DrivenTestEngine.App.ViewModels;
using DrivenTestEngine.App.Windows;
using DrivenTestEngine.Core.Models;

namespace DrivenTestEngine.App.Services;

/// <summary>
/// Owns the two windows (overlay + edge tab) and the shell view model, and handles the
/// hide/show transition plus state persistence on exit.
/// </summary>
public sealed class OverlayHost : IDisposable
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly OverlayWindow _overlay;
    private readonly EdgeTabWindow _tab;
    private readonly LocalReferenceService _localService = new();
    private bool _disposed;
    private readonly CancellationTokenSource _autoConnectCts = new();

    public OverlayHost(AppServices services)
    {
        _services = services;
        _shell = new ShellViewModel(services);
        _overlay = new OverlayWindow(_shell, services);
        _tab = new EdgeTabWindow();

        _overlay.CollapseRequested += (_, _) => Collapse();
        _overlay.CloseRequested += (_, _) => Shutdown();
        _overlay.Closed += (_, _) =>
        {
            if (!_disposed)
            {
                Shutdown();
            }
        };

        _tab.ExpandRequested += (_, _) => Expand();
    }

    public ShellViewModel Shell => _shell;

    public void Start()
    {
        _overlay.RestoreGeometry();

        _services.Log.Info(DetectionCategory.Client, "[client] Overlay started · " + _services.BuildStamp);

        if (_services.Settings.Current.StartCollapsed)
        {
            ShowTab();
        }
        else
        {
            _overlay.Show();
        }

        var serviceError = _localService.StartIfNeeded(_services.Settings.Current);
        if (serviceError is not null)
        {
            _services.Log.Error(DetectionCategory.Client, "[client] Local service startup failed", serviceError);
            MessageBox.Show(serviceError, "Driven Test Engine — local service setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (_services.Settings.Current.AutoConnect)
        {
            _shell.Connection.ConnectCommand.Execute(null);
            _ = RetryAutoConnectAsync(_autoConnectCts.Token);
        }
    }

    private async Task RetryAutoConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_shell.Connection.IsConnected)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested || _shell.Connection.IsConnected
                    || !_services.Settings.Current.AutoConnect)
                {
                    return;
                }

                var startError = _localService.StartIfNeeded(_services.Settings.Current);
                if (startError is not null)
                {
                    _services.Log.Warn(DetectionCategory.Client, "[client] Local service retry could not start", startError);
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!_shell.Connection.IsConnected)
                    {
                        _shell.Connection.ConnectCommand.Execute(null);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Normal on application shutdown.
        }
    }

    public void Collapse()
    {
        _overlay.SaveGeometry();
        _overlay.Hide();
        ShowTab();
        _services.Log.Info(DetectionCategory.Client, "[client] Overlay hidden to the edge tab.");
    }

    public void Expand()
    {
        _tab.Hide();
        _overlay.Show();
        _overlay.Activate();
        _overlay.SaveGeometry();
    }

    private void ShowTab()
    {
        _tab.Place(_overlay.Top, _overlay.Height);
        if (!_tab.IsVisible)
        {
            _tab.Show();
        }
    }

    public void Shutdown()
    {
        Persist();
        Application.Current?.Shutdown();
    }

    public void Persist()
    {
        try
        {
            _overlay.SaveGeometry();
            _shell.Connection.Persist();
            _services.Settings.Update(s => s.StartCollapsed = _tab.IsVisible && !_overlay.IsVisible);
        }
        catch (Exception)
        {
            // Never block shutdown on a persistence hiccup.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _autoConnectCts.Cancel();
        _autoConnectCts.Dispose();
        _localService.Dispose();
        Persist();
        CloseTabWindow();
    }

    private void CloseTabWindow()
    {
        try
        {
            _tab.Close();
        }
        catch (InvalidOperationException)
        {
            // Already closed while shutting down.
        }
    }
}
