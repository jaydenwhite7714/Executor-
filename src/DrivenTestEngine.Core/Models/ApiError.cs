namespace DrivenTestEngine.Core.Models;

/// <summary>Error envelope returned by the testing service for every non-2xx response.</summary>
public sealed class ApiErrorEnvelope
{
    public ApiError? Error { get; set; }
}

public sealed class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Hint { get; set; }
    public Dictionary<string, string>? Details { get; set; }
}

public sealed class HealthResponse
{
    public string Status { get; set; } = string.Empty;
    public string ServerVersion { get; set; } = string.Empty;
    public DateTimeOffset? Time { get; set; }
    public string? Game { get; set; }
    public string? ProtocolVersion { get; set; }
}
