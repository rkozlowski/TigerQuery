using ItTiger.TigerQuery.Core.Encryption;

namespace ItTiger.TigerQuery.Core;

public sealed class DpapiConnectionPasswordProtector : IConnectionPasswordProtector
{
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
