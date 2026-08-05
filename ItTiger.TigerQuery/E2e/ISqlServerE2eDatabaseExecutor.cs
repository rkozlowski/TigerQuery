namespace ItTiger.TigerQuery.E2e;

internal interface ISqlServerE2eDatabaseExecutor
{
    void ClearPool(string connectionString);

    Task ExecuteAsync(
        string connectionString,
        string script,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes one parameterized script as a single batch over a single connection.
    /// </summary>
    /// <param name="connectionString">The effective connection string.</param>
    /// <param name="script">
    /// One batch of T-SQL. It must contain no batch separator: the guarantee this
    /// overload exists for is that every statement in it reaches SQL Server together,
    /// on one connection, with nothing able to run between them.
    /// </param>
    /// <param name="parameters">
    /// Values bound by name, including the leading <c>@</c>. Names that identifiers
    /// cannot express (a database name in <c>ALTER</c>/<c>DROP</c>) still have to be
    /// quoted into the script by the caller.
    /// </param>
    /// <param name="cancellationToken">A token observed during execution.</param>
    Task ExecuteAsync(
        string connectionString,
        string script,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> QueryNamesAsync(
        string connectionString,
        string script,
        CancellationToken cancellationToken);
}
