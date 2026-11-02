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
    private bool _disposed;

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

        if (_services.Settings.Current.AutoConnect && !_shell.Connection.IsConnected)
        {
            _shell.Connection.ConnectCommand.Execute(null);
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
