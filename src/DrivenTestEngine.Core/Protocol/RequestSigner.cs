using System.Security.Cryptography;
using System.Text;

namespace DrivenTestEngine.Core.Protocol;

/// <summary>
/// HMAC-SHA256 request signing used by every call to the testing service.
/// Canonical string (all parts UTF-8, '\n' separated):
///   METHOD \n PATH_AND_QUERY \n TIMESTAMP \n NONCE \n SHA256_HEX_LOWERCASE(body)
/// The signature is the lower-case hex HMAC-SHA256 of that string, keyed with the shared secret.
/// </summary>
public static class RequestSigner
{
    public static string ComputeSignature(
        string sharedSecret,
        string method,
        string pathAndQuery,
        string timestamp,
        string nonce,
        ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrEmpty(sharedSecret);

        var bodyHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var canonical = string.Concat(
            (method ?? string.Empty).ToUpperInvariant(), "\n",
            pathAndQuery ?? string.Empty, "\n",
            timestamp, "\n",
            nonce, "\n",
            bodyHash);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(signature).ToLowerInvariant();
    }

    public static string ComputeSignature(
        string sharedSecret,
        string method,
        string pathAndQuery,
        string timestamp,
        string nonce,
        string body) =>
        ComputeSignature(sharedSecret, method, pathAndQuery, timestamp, nonce, Encoding.UTF8.GetBytes(body ?? string.Empty));

    public static bool FixedTimeEquals(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Cryptographically random 128-bit nonce encoded as lower-case hex.</summary>
    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Unix timestamp (seconds) as required by the protocol.</summary>
    public static string TimestampNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
}
