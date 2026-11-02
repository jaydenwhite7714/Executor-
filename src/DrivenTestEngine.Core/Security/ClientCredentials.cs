namespace DrivenTestEngine.Core.Security;

/// <summary>Key id + shared signing secret issued by the testing service's operator.</summary>
public sealed class ClientCredentials
{
    public ClientCredentials()
    {
    }

    public ClientCredentials(string keyId, string sharedSecret)
    {
        KeyId = keyId;
        SharedSecret = sharedSecret;
    }

    public string KeyId { get; set; } = string.Empty;
    public string SharedSecret { get; set; } = string.Empty;

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(SharedSecret);

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(KeyId))
        {
            return "A key id is required.";
        }

        if (KeyId.Length > 128)
        {
            return "The key id is too long (max 128 characters).";
        }

        if (string.IsNullOrWhiteSpace(SharedSecret))
        {
            return "A signing secret is required.";
        }

        if (SharedSecret.Length < 32)
        {
            return "The signing secret must be at least 32 characters. Generate one on the testing service.";
        }

        return null;
    }

    public ClientCredentials Clone() => new(KeyId, SharedSecret);
}
