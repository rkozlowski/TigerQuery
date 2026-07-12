namespace ItTiger.TigerQuery.Core;

public sealed class NonPersistingConnectionPasswordProtector : IConnectionPasswordProtector
{
    public void ProtectForSave(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.PlainPassword = null;
    }

    public void UnprotectAfterLoad(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.PlainPassword = null;
    }
}
