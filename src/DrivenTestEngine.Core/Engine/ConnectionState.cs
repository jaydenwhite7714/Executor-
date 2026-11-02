using DrivenTestEngine.Core.Models;
using DrivenTestEngine.Core.Transport;

namespace DrivenTestEngine.Core.Engine;

public enum ConnectionState
{
    Disconnected = 0,
    Connecting,
    Connected,
    Disconnecting,
    Faulted,
}

/// <summary>Immutable view of the link to the testing service, safe to hand to the UI thread.</summary>
public sealed class ConnectionSnapshot
{
    public ConnectionState State { get; init; } = ConnectionState.Disconnected;
    public string Message { get; init; } = "Not connected";
    public ServerSession? Session { get; init; }
    public Uri? ServerUrl { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public TransportFailure? Failure { get; init; }

    public bool IsConnected => State == ConnectionState.Connected && Session is not null;

    public string StateText => State switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.Connecting => "Connecting",
        ConnectionState.Disconnecting => "Disconnecting",
        ConnectionState.Faulted => "Faulted",
        _ => "Offline",
    };

    public string SessionText => Session is null
        ? "no session"
        : "session " + (Session.TestSession.SessionId.Length > 8
            ? Session.TestSession.SessionId[..8]
            : Session.TestSession.SessionId);
}
