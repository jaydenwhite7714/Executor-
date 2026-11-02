using System.Diagnostics;
using System.Net.Sockets;
using DrivenTestEngine.Core.Settings;

namespace DrivenTestEngine.App.Services;

/// <summary>Starts the bundled, loopback-only reference service when the local profile is selected.</summary>
internal sealed class LocalReferenceService : IDisposable
{
    private Process? _process;

    public string? StartIfNeeded(AppSettings settings)
    {
        if (!IsLocalReferenceProfile(settings))
        {
            return null;
        }

        if (CanConnect())
        {
            return null;
        }

        var entryPoint = Path.Combine(AppContext.BaseDirectory, "server", "src", "server.mjs");
        if (!File.Exists(entryPoint))
        {
            return "The local reference service files are missing. Repair/reinstall Driven Test Engine, or start it manually from the repository with `cd server && npm start`.";
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "node",
                WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "server"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            start.ArgumentList.Add(entryPoint);
            start.Environment["DTE_HOST"] = "127.0.0.1";
            start.Environment["DTE_PORT"] = "8791";
            start.Environment["DTE_KEY_ID"] = "dte-dev-key";
            start.Environment["DTE_SECRET"] = "dev-secret-change-me-0123456789abcdef";
            _process = Process.Start(start);
            return _process is null
                ? "Could not start the local reference service. Install Node.js 18 or newer, then restart DTE."
                : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Node.js 18 or newer is required to auto-start the local reference service. Install the LTS version from https://nodejs.org/, then restart DTE.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return "Could not start the local reference service: " + ex.Message;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
            // It exited between the check and shutdown.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Do not prevent the overlay from shutting down if process teardown is denied.
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    private static bool IsLocalReferenceProfile(AppSettings settings) =>
        Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var uri)
        && uri.IsLoopback
        && uri.Port == 8791
        && string.Equals(settings.KeyId, "dte-dev-key", StringComparison.Ordinal);

    private static bool CanConnect()
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync("127.0.0.1", 8791);
            return task.Wait(TimeSpan.FromMilliseconds(250)) && client.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (AggregateException)
        {
            return false;
        }
    }
}
