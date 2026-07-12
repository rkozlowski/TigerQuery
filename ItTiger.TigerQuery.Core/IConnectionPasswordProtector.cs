namespace ItTiger.TigerQuery.Core;

public interface IConnectionPasswordProtector
{
    void ProtectForSave(SqlServerConnectionProfile profile);
    void UnprotectAfterLoad(SqlServerConnectionProfile profile);
}
