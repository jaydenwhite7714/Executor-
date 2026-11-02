using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DrivenTestEngine.App.Infrastructure;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.Core.Models;

namespace DrivenTestEngine.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "DrivenTestEngine.SingleInstance.6F0B4C21";

    private Mutex? _singleInstance;
    private AppServices? _services;
    private OverlayHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        UiDispatcher.Initialize(Dispatcher);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        if (!AcquireSingleInstance())
        {
            MessageBox.Show(
                "Driven Test Engine is already running. Look for the overlay or its edge tab.",
                "Driven Test Engine",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _services = AppServices.Create();
            MotionSettings.Enabled = !_services.Settings.Current.ReduceMotion;
            _host = new OverlayHost(_services);
            _host.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Driven Test Engine could not start.\n\n" + ex.Message,
                "Driven Test Engine",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private bool AcquireSingleInstance()
    {
        try
        {
            _singleInstance = new Mutex(true, SingleInstanceMutexName, out var createdNew);
            return createdNew;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.Log.Error(
            DetectionCategory.Client,
            "[client] Unhandled UI exception: " + e.Exception.GetType().Name,
            e.Exception.Message);

        MessageBox.Show(
            "An unexpected error was caught so the overlay can stay open.\n\n" + e.Exception.Message,
            "Driven Test Engine",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.Dispose();
            if (_services is not null)
            {
                _services.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            }
        }
        catch (Exception)
        {
            // Shutdown must always complete.
        }
        finally
        {
            _singleInstance?.Dispose();
        }

        base.OnExit(e);
    }
}
