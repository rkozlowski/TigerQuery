using ItTiger.TigerQuery.Core.Encryption;

namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Protects non-empty plain-text profile passwords with current-user Windows DPAPI.
/// </summary>
/// <remarks>
/// DPAPI ciphertext is tied to the Windows user and is not portable to another
/// user or platform. Decryption failure leaves <see cref="SqlServerConnectionProfile.PlainPassword"/>
/// unchanged rather than throwing.
/// </remarks>
public sealed class DpapiConnectionPasswordProtector : IConnectionPasswordProtector
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">
    /// A non-empty plain password is supplied on a platform without Windows DPAPI.
    /// </exception>
    public void ProtectForSave(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrEmpty(profile.PlainPassword))
            return;

        if (!DpapiHelper.TryEncrypt(profile.PlainPassword, out var encryptedPassword))
            throw new PlatformNotSupportedException("DPAPI encryption is only supported on Windows.");

        profile.EncryptedPassword = encryptedPassword;
        profile.PasswordEncryption = PasswordEncryptionType.DPAPI;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    public void UnprotectAfterLoad(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.PasswordEncryption != PasswordEncryptionType.DPAPI ||
            string.IsNullOrEmpty(profile.EncryptedPassword))
        {
            return;
        }

        if (DpapiHelper.TryDecrypt(profile.EncryptedPassword, out var plainPassword))
            profile.PlainPassword = plainPassword;
    }
}
