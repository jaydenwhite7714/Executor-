using System.Text.Json;
using DrivenTestEngine.Core.Security;

namespace DrivenTestEngine.Core.Settings;

/// <summary>Loads/saves <see cref="AppSettings"/> and owns the DPAPI-protected signing secret.</summary>
public sealed class SettingsService
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public SettingsService(string filePath)
    {
        _filePath = filePath;
    }

    public AppSettings Current { get; private set; } = new();

    public string FilePath => _filePath;

    public string? SecretWarning { get; private set; }

    public void Load()
    {
        AppSettings settings = new();
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, _json) ?? new AppSettings();
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        settings.Sanitize();
        lock (_gate)
        {
            Current = settings;
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AppSettings snapshot;
            lock (_gate)
            {
                snapshot = Current;
            }

            var json = JsonSerializer.Serialize(snapshot, _json);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Reads the signing secret, decrypting it with DPAPI when a protected blob exists.</summary>
    public string? ReadSecret()
    {
        string? protectedBlob;
        lock (_gate)
        {
            protectedBlob = Current.ProtectedSecret;
        }

        if (string.IsNullOrWhiteSpace(protectedBlob))
        {
            return null;
        }

        var secret = SecretProtector.Unprotect(protectedBlob);
        if (secret is null)
        {
            SecretWarning = "The saved signing secret could not be decrypted on this Windows account. Enter it again.";
        }

        return secret;
    }

    /// <summary>Stores the signing secret. Returns false when it could not be persisted securely.</summary>
    public bool WriteSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            lock (_gate)
            {
                Current.ProtectedSecret = null;
            }

            SecretWarning = null;
            Save();
            return true;
        }

        var blob = SecretProtector.Protect(secret);
        if (blob is null)
        {
            SecretWarning = SecretProtector.IsAvailable
                ? "Windows DPAPI refused to protect the secret, so it was not saved."
                : "DPAPI is unavailable, so the secret is kept in memory for this session only.";
            return false;
        }

        lock (_gate)
        {
            Current.ProtectedSecret = blob;
        }

        SecretWarning = null;
        Save();
        return true;
    }

    public void EnsureClientId()
    {
        var generated = false;
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(Current.ClientId))
            {
                Current.ClientId = Guid.NewGuid().ToString("N");
                generated = true;
            }
        }

        if (generated)
        {
            // Keep the id stable across launches so the server can recognise this client.
            Save();
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        lock (_gate)
        {
            mutate(Current);
            Current.Sanitize();
        }

        Save();
    }
}
