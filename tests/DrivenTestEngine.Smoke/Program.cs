using System.Net;
using System.Net.Sockets;
using DrivenTestEngine.App.Services;
using DrivenTestEngine.App.ViewModels;
using DrivenTestEngine.Core.Security;
using DrivenTestEngine.Core.Tests;
using DrivenTestEngine.Core.Transport;
using DrivenTestEngine.Core.Settings;
using DrivenTestEngine.Core.Util;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// Simulate an upgraded installation from the old public demo profile, including a stale
// expected Roblox user id that cannot be verified by a local reference server.
AppPaths.EnsureCreated();
var oldSettings = new SettingsService(AppPaths.SettingsFile);
oldSettings.Load();
oldSettings.Update(s =>
{
    s.ServerUrl = "http://127.0.0.1:8791";
    s.KeyId = "dte-dev-key";
    s.ExpectedRobloxUserId = "42424242";
    s.AutoConnect = false;
});
Require(oldSettings.WriteSecret("dev-secret-change-me-0123456789abcdef"), "Could not seed legacy profile for migration test.");

await using var appServices = AppServices.Create();
var profile = appServices.Settings.Current;
var secret = appServices.Settings.ReadSecret();
Require(profile.ServerUrl == "http://127.0.0.1:8791", "First-run profile did not select loopback service.");
Require(profile.KeyId.StartsWith("dte-local-", StringComparison.Ordinal), "First-run key id was not generated.");
Require(profile.AutoConnect, "First-run auto-connect was not enabled.");
Require(profile.ExpectedRobloxUserId is null, "Local preview retained an unverifiable expected Roblox UserId.");
Require(profile.KeyId != "dte-dev-key", "Legacy public demo key was not rotated.");
Require(!string.IsNullOrWhiteSpace(secret) && secret != "dev-secret-change-me-0123456789abcdef",  "DPAPI-protected first-run secret could not be read back.");
Console.WriteLine("PASS first-run key/secret provisioning and DPAPI read-back");
using var service = new LocalReferenceService();

// Prove that a port collision produces actionable feedback, not a confusing handshake error.
using (var blocker = new TcpListener(IPAddress.Loopback, 8791))
{
    blocker.Start();
    var conflict = service.StartIfNeeded(profile, secret);
    Require(conflict?.Contains("already listening", StringComparison.OrdinalIgnoreCase) == true,
        "Port conflict was not reported clearly.");
    Console.WriteLine("PASS local service reports port 8791 conflict clearly");
}

var startError = service.StartIfNeeded(profile, secret);
Require(startError is null, startError ?? "Unexpected local service launch result.");
Console.WriteLine("PASS bundled reference service process launched");

using var transport = new HttpTestServerTransport(
    new Uri(profile.ServerUrl), new ClientCredentials(profile.KeyId, secret!), "smoke-test", profile.ClientId);

TransportResult<DrivenTestEngine.Core.Models.HealthResponse>? health = null;
for (var attempt = 0; attempt < 50; attempt++)
{
    health = await transport.CheckHealthAsync(CancellationToken.None);
    if (health.IsSuccess) break;
    await Task.Delay(100);
}
Require(health?.IsSuccess == true, "Bundled service health check failed: " + health?.Failure?.DisplayMessage);
Console.WriteLine("PASS signed health request succeeded");

var handshake = await transport.HandshakeAsync(CancellationToken.None);
Require(handshake.IsSuccess, "Signed handshake failed: " + handshake.Failure?.DisplayMessage);
Require(!string.IsNullOrWhiteSpace(handshake.Value!.SessionToken), "Handshake returned no token.");
Require(handshake.Value.AllowedTests.Count == 0, "Local preview must not advertise unimplemented Roblox tests.");
Require(string.IsNullOrWhiteSpace(handshake.Value.Account.RobloxUserId), "Local reference must not claim a placeholder Roblox UserId.");
Console.WriteLine("PASS signed handshake returned a token, no placeholder Roblox ID, and no enabled game tests");

await transport.CloseSessionAsync(handshake.Value.SessionToken, CancellationToken.None);
Console.WriteLine("PASS session closed cleanly");

var engineConnection = await appServices.Engine.ConnectAsync();
Require(engineConnection.IsConnected, "EngineClient authenticated connection failed: " + engineConnection.Failure?.DisplayMessage);
Console.WriteLine("PASS application EngineClient authenticated the auto-provisioned local profile");
await appServices.Engine.DisconnectAsync();

foreach (var id in new[] { TestCatalog.Speed, TestCatalog.Jump })
{
    var card = new TestCardViewModel(appServices, TestCatalog.Find(id)!);
    card.TogglePreviewCommand.Execute(null);
    Require(card.PreviewEnabled && card.PreviewToggleText == "ON", $"{id} preview toggle did not turn on.");
    card.PreviewValue = 135;
    Require(card.PreviewValue == 100, $"{id} preview slider did not clamp to its UI range.");
    Require(card.PreviewStateText.Contains("UI only", StringComparison.Ordinal), $"{id} preview was not labeled UI-only.");
    Require(card.MetricText == "—", $"{id} local preview invented a measurement.");
    Console.WriteLine($"PASS {id} preview toggle/slider responds and emits no result");
}

foreach (var id in new[] { TestCatalog.AutoCatch, TestCatalog.DimeAccuracy })
{
    var card = new TestCardViewModel(appServices, TestCatalog.Find(id)!);
    Require(card.IsUnsupportedIntegration && !card.HasLocalPreview,
        $"{id} was not clearly left unavailable pending game integration.");
    Console.WriteLine($"PASS {id} remains unavailable pending server integration");
}
