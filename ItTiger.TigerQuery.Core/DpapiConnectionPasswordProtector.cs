using System.Security.Cryptography;
using ItTiger.TigerQuery.Core.Encryption;

namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Protects non-empty plain-text profile passwords with current-user Windows DPAPI.
/// </summary>
/// <remarks>
/// DPAPI ciphertext is tied to the Windows user account that saved it and is not portable
/// to another user or platform; it follows that account to another computer only through a
/// roaming user profile. Decryption failure leaves
/// <see cref="SqlServerConnectionProfile.PlainPassword"/> unchanged rather than throwing, and
/// <see cref="SqlServerConnectionResolver"/> then reports the stored password as unreadable
/// instead of attempting a login without it.
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
    /// <exception cref="CryptographicException">
    /// Windows DPAPI rejected the operation, for example because the account has no loaded
    /// user profile. The message names DPAPI's own reason and never contains the password.
    /// </exception>
    public void ProtectForSave(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrEmpty(profile.PlainPassword))
            return;

        string encryptedPassword;
        try
        {
            encryptedPassword = DpapiHelper.Encrypt(profile.PlainPassword);
        }
        catch (CryptographicException ex)
        {
            // Saving must fail loudly rather than drop the password, and the reason DPAPI
            // gave is kept: it is what tells a service account without a loaded profile
            // apart from any other failure.
            throw new CryptographicException(
                "The password could not be protected with Windows DPAPI for the current user: "
                + ex.Message,
                ex);
        }

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
