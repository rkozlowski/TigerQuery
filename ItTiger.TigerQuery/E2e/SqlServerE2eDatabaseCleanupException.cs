namespace ItTiger.TigerQuery.E2e;

/// <summary>Reports that an owned E2E database could not be dropped.</summary>
public sealed class SqlServerE2eDatabaseCleanupException : Exception
{
    /// <summary>Initializes the failure with the exact database left behind.</summary>
    /// <param name="databaseName">The exact current-lifecycle database name.</param>
    /// <param name="innerException">The SQL execution failure.</param>
    public SqlServerE2eDatabaseCleanupException(string databaseName, Exception innerException)
        : base($"Cleanup failed; E2E database '{databaseName}' was left behind.", innerException)
    {
        DatabaseName = databaseName;
    }

    /// <summary>Gets the exact database left behind.</summary>
    public string DatabaseName { get; }
}
