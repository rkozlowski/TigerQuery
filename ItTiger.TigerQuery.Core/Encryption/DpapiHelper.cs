using System;
using System.Security.Cryptography;
using System.Text;

namespace ItTiger.TigerQuery.Core.Encryption;

/// <summary>
/// Provides current-user Windows DPAPI encryption helpers using UTF-8 text and
/// Base64-encoded protected values.
/// </summary>
public static class DpapiHelper
{
    /// <summary>Encrypts text for the current Windows user.</summary>
    /// <param name="plain">The plain text to protect.</param>
    /// <returns>A Base64-encoded DPAPI value.</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// The current platform is not Windows.
    /// </exception>
    public static string Encrypt(string plain)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI encryption is only supported on Windows.");

        var bytes = Encoding.UTF8.GetBytes(plain);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    /// <summary>Attempts to decrypt a Base64-encoded current-user DPAPI value.</summary>
    /// <param name="encrypted">The value produced by <see cref="Encrypt"/>.</param>
    /// <returns>
    /// The decrypted UTF-8 text, or an empty string when decoding or unprotection fails.
    /// </returns>
    /// <exception cref="PlatformNotSupportedException">
    /// The current platform is not Windows.
    /// </exception>
    public static string Decrypt(string encrypted)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI encryption is only supported on Windows.");

        try
        {
            var protectedBytes = Convert.FromBase64String(encrypted);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Tries to encrypt text for the current Windows user.</summary>
    /// <param name="plain">The plain text to protect.</param>
    /// <param name="encrypted">
    /// Receives the Base64-encoded protected value on success, or an empty string on failure.
    /// </param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    /// <remarks>This method returns false rather than throwing for platform and encryption failures.</remarks>
    public static bool TryEncrypt(string plain, out string encrypted)
    {
        encrypted = string.Empty;

        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            encrypted = Encrypt(plain);
            return true;
        }
        catch
        {
            encrypted = string.Empty;
            return false;
        }
    }

    /// <summary>Tries to decrypt a Base64-encoded current-user DPAPI value.</summary>
    /// <param name="encrypted">The protected value to decrypt.</param>
    /// <param name="plain">
    /// Receives non-empty decrypted text on success, or an empty string on failure.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a non-empty value is decrypted; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>This method returns false rather than throwing for platform and decryption failures.</remarks>
    public static bool TryDecrypt(string encrypted, out string plain)
    {
        plain = string.Empty;

        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var decrypted = Decrypt(encrypted);
            if (string.IsNullOrEmpty(decrypted))
                return false;

            plain = decrypted;
            return true;
        }
        catch
        {
            plain = string.Empty;
            return false;
        }
    }
}
