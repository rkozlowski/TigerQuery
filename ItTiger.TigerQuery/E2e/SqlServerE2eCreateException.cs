namespace ItTiger.TigerQuery.E2e;

/// <summary>Reports connection persistence failure and the exact database rollback result.</summary>
public sealed class SqlServerE2eCreateException : Exception
{
    /// <summary>Initializes a failed paired create operation.</summary>
    public SqlServerE2eCreateException(
        string databaseName,
        string connectionName,
        bool rollbackSucceeded,
        Exception persistenceFailure,
        Exception? rollbackFailure = null)
        : base(
            rollbackSucceeded
                ? $"Connection '{connectionName}' could not be persisted; database '{databaseName}' was rolled back successfully."
                : $"Connection '{connectionName}' could not be persisted and rollback of database '{databaseName}' also failed. Manual cleanup is required.",
            rollbackFailure is null
                ? persistenceFailure
                : new AggregateException(persistenceFailure, rollbackFailure))
    {
        DatabaseName = databaseName;
        ConnectionName = connectionName;
        RollbackSucceeded = rollbackSucceeded;
        PersistenceFailure = persistenceFailure;
        RollbackFailure = rollbackFailure;
    }

    /// <summary>Gets the exact database created by the failed operation.</summary>
    public string DatabaseName { get; }

    /// <summary>Gets the intended connection name.</summary>
    public string ConnectionName { get; }

    /// <summary>Gets whether exact-database rollback succeeded.</summary>
    public bool RollbackSucceeded { get; }

    /// <summary>Gets the connection persistence failure.</summary>
    public Exception PersistenceFailure { get; }

    /// <summary>Gets the rollback failure, if rollback did not succeed.</summary>
    public Exception? RollbackFailure { get; }
}
