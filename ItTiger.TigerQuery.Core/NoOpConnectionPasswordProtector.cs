namespace ItTiger.TigerQuery.Core;

public sealed class NoOpConnectionPasswordProtector : IConnectionPasswordProtector
{
    public void ProtectForSave(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
    }

    public void UnprotectAfterLoad(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
    }
}
