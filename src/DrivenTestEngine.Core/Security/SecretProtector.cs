using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DrivenTestEngine.Core.Security;

/// <summary>
/// Wraps Windows DPAPI (current-user scope) so the signing secret can be persisted without ever
/// touching the disk in clear text. On non-Windows hosts DPAPI is unavailable and the caller must
/// keep the secret in memory only.
/// </summary>
public static class SecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;
    private const string Description = "Driven Test Engine signing secret";

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    public static bool IsAvailable => OperatingSystem.IsWindows();

    /// <summary>Protects the secret and returns a base64 blob, or null when DPAPI is unavailable.</summary>
    public static string? Protect(string plaintext)
    {
        if (!IsAvailable || string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var inputPtr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, inputPtr, bytes.Length);
            var input = new DataBlob { Size = bytes.Length, Data = inputPtr };

            if (!CryptProtectData(ref input, Description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            {
                return null;
            }

            try
            {
                return Convert.ToBase64String(ReadBlob(output));
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                {
                    LocalFree(output.Data);
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(inputPtr);
        }
    }

    /// <summary>Unprotects a base64 DPAPI blob. Returns null when the blob is unreadable.</summary>
    public static string? Unprotect(string? protectedBase64)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(protectedBase64))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(protectedBase64);
        }
        catch (FormatException)
        {
            return null;
        }

        var inputPtr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, inputPtr, bytes.Length);
            var input = new DataBlob { Size = bytes.Length, Data = inputPtr };

            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            {
                return null;
            }

            try
            {
                return Encoding.UTF8.GetString(ReadBlob(output));
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                {
                    LocalFree(output.Data);
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(inputPtr);
        }
    }

    private static byte[] ReadBlob(DataBlob blob)
    {
        if (blob.Size <= 0 || blob.Data == IntPtr.Zero)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[blob.Size];
        Marshal.Copy(blob.Data, buffer, 0, blob.Size);
        return buffer;
    }
}
