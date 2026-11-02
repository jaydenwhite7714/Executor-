namespace DrivenTestEngine.Core.Transport;

public enum TransportFailureKind
{
    Unknown = 0,
    NotConfigured,
    Network,
    Timeout,
    Unauthorized,
    Forbidden,
    Rejected,
    ServerError,
    Protocol,
    Cancelled,
}

/// <summary>Why a call to the testing service did not produce a usable response.</summary>
public sealed class TransportFailure
{
    public TransportFailureKind Kind { get; init; } = TransportFailureKind.Unknown;
    public string Message { get; init; } = "The request failed.";
    public string? ServerCode { get; init; }
    public int? HttpStatus { get; init; }
    public string? Hint { get; init; }

    public string DisplayMessage => string.IsNullOrWhiteSpace(Hint)
        ? Message
        : Message + " " + Hint;

    public static TransportFailure Create(
        TransportFailureKind kind,
        string message,
        string? hint = null,
        string? serverCode = null,
        int? httpStatus = null) => new()
        {
            Kind = kind,
            Message = message,
            Hint = hint,
            ServerCode = serverCode,
            HttpStatus = httpStatus,
        };

    public static TransportFailure NotConfigured(string message = "No testing service is configured yet.") =>
        Create(TransportFailureKind.NotConfigured, message, "Open Connection and enter your server URL, key id and signing secret.");

    public static TransportFailure Network(string message, Exception? inner = null) =>
        Create(TransportFailureKind.Network, message, inner?.Message);

    public static TransportFailure Timeout(string message) =>
        Create(TransportFailureKind.Timeout, message, "The testing service did not answer in time.");

    public override string ToString() => $"{Kind}: {Message}";
}

public sealed class TransportResult<T>
{
    private TransportResult(T? value, TransportFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }
    public TransportFailure? Failure { get; }
    public bool IsSuccess => Failure is null;

    public static TransportResult<T> Success(T value) => new(value, null);
    public static TransportResult<T> Failed(TransportFailure failure) => new(default, failure);

    public static implicit operator bool(TransportResult<T> result) => result.IsSuccess;
}
