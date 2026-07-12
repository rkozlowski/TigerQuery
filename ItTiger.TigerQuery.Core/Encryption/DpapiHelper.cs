using System;
using System.Security.Cryptography;
using System.Text;

namespace ItTiger.TigerQuery.Core.Encryption;

public static class DpapiHelper
{
    public static string Encrypt(string plain)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI encryption is only supported on Windows.");

        var bytes = Encoding.UTF8.GetBytes(plain);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

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
