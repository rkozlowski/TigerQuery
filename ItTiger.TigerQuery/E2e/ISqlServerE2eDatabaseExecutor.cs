namespace ItTiger.TigerQuery.E2e;

internal interface ISqlServerE2eDatabaseExecutor
{
    Task ExecuteAsync(
        string connectionString,
        string script,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> QueryNamesAsync(
        string connectionString,
        string script,
        CancellationToken cancellationToken);
}
