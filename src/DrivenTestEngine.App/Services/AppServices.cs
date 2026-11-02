using System.Reflection;
using DrivenTestEngine.Core.Engine;
using DrivenTestEngine.Core.History;
using DrivenTestEngine.Core.Logging;
using DrivenTestEngine.Core.Protocol;
using DrivenTestEngine.Core.Security;
using DrivenTestEngine.Core.Settings;
using DrivenTestEngine.Core.Util;

namespace DrivenTestEngine.App.Services;

/// <summary>Composition root: one instance shared by every view model.</summary>
public sealed class AppServices : IAsyncDisposable
{
    private AppServices(
        SettingsService settings,
        DetectionLogStore log,
        HistoryStore history,
        EngineClient engine)
    {
        Settings = settings;
        Log = log;
        History = history;
        Engine = engine;
    }

    public SettingsService Settings { get; }

    public DetectionLogStore Log { get; }

    public HistoryStore History { get; }

    public EngineClient Engine { get; }

    public string VersionText { get; private init; } = "1.0.0";

    public string BuildStamp => "v" + VersionText + " · protocol " + DteProtocol.Version;

    public static AppServices Create()
    {
        AppPaths.EnsureCreated();

        var settings = new SettingsService(AppPaths.SettingsFile);
        settings.Load();
        SeedLocalReferenceDefaults(settings);
        settings.EnsureClientId();

        var log = new DetectionLogStore(1500, AppPaths.LogFolder);
        var history = new HistoryStore(AppPaths.HistoryFile);
        history.Load();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        var engine = new EngineClient(
            log,
            history,
            version,
            settings.Current.ClientId,
            settings.Current.ExpectedRobloxUserId);

        var services = new AppServices(settings, log, history, engine) { VersionText = version };
        services.ReconfigureEngine();
        return services;
    }

    /// <summary>
    /// First-run convenience for the bundled loopback-only reference service. Never applies to
    /// non-loopback URLs; the sample secret is public and must not be used in a deployed service.
    /// </summary>
    private static void SeedLocalReferenceDefaults(SettingsService settings)
    {
        var current = settings.Current;
        var unsetProfile = string.IsNullOrWhiteSpace(current.ServerUrl)
            && string.IsNullOrWhiteSpace(current.KeyId)
            && string.IsNullOrWhiteSpace(current.ProtectedSecret);
        var explicitLoopbackProfile = Uri.TryCreate(current.ServerUrl, UriKind.Absolute, out var uri)
            && uri.IsLoopback && uri.Port == 8791
            && (string.IsNullOrWhiteSpace(current.KeyId)
                || string.Equals(current.KeyId, "dte-dev-key", StringComparison.Ordinal));
        if (!unsetProfile && !explicitLoopbackProfile)
        {
            return;
        }

        if (unsetProfile || string.IsNullOrWhiteSpace(current.ServerUrl))
        {
            current.ServerUrl = "http://127.0.0.1:8791";
        }

        if (string.IsNullOrWhiteSpace(current.KeyId))
        {
            current.KeyId = "dte-dev-key";
        }

        current.AutoConnect = true;
        if (string.IsNullOrWhiteSpace(current.ProtectedSecret))
        {
            // This is intentionally the documented local demo credential, not a production key.
            settings.WriteSecret("dev-secret-change-me-0123456789abcdef");
        }

        settings.Save();
    }

    public ClientCredentials BuildCredentials()
    {
        var secret = Settings.ReadSecret();
        return new ClientCredentials(Settings.Current.KeyId ?? string.Empty, secret ?? string.Empty);
    }

    public Uri? BuildServerUri()
    {
        var raw = Settings.Current.ServerUrl?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return Uri.TryCreate(raw, UriKind.Absolute, out var uri) ? uri : null;
    }

    public void ReconfigureEngine()
    {
        Engine.Configure(BuildServerUri(), BuildCredentials(), Settings.Current.ExpectedRobloxUserId);
    }

    public async ValueTask DisposeAsync()
    {
        await Engine.DisposeAsync().ConfigureAwait(false);
        Log.Dispose();
    }
}
