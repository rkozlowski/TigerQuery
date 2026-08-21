namespace ItTiger.TigerQuery.E2e;

/// <summary>
/// Reports that a created E2E database could not be provisioned with a requested
/// capability, and what the ownership-safe rollback of the exact paired resources achieved.
/// </summary>
/// <remarks>
/// A create call that raises this has already torn down, or tried to tear down, both the
/// database it created and the connection record it paired with it, through the same
/// ownership-checked teardown a caller would have used. Nothing partially provisioned is
/// ever handed back as usable.
/// </remarks>
public sealed class SqlServerE2eProvisionException : Exception
{
    /// <summary>Initializes a failed provisioning operation and its rollback result.</summary>
    public SqlServerE2eProvisionException(
        string databaseName,
        string connectionName,
        bool rollbackSucceeded,
        Exception provisioningFailure,
        Exception? rollbackFailure = null)
        : base(
            rollbackSucceeded
                ? $"Database '{databaseName}' could not be provisioned; it and connection '{connectionName}' were rolled back successfully."
                : $"Database '{databaseName}' could not be provisioned and rollback of it and connection '{connectionName}' also failed. Manual cleanup is required.",
            rollbackFailure is null
                ? provisioningFailure
                : new AggregateException(provisioningFailure, rollbackFailure))
    {
        DatabaseName = databaseName;
        ConnectionName = connectionName;
        RollbackSucceeded = rollbackSucceeded;
        ProvisioningFailure = provisioningFailure;
        RollbackFailure = rollbackFailure;
    }

    /// <summary>Gets the exact database created by the failed operation.</summary>
    public string DatabaseName { get; }

    /// <summary>Gets the exact paired connection name.</summary>
    public string ConnectionName { get; }

    /// <summary>Gets whether rollback of both exact resources succeeded.</summary>
    public bool RollbackSucceeded { get; }

    /// <summary>Gets the provisioning failure.</summary>
    public Exception ProvisioningFailure { get; }

    /// <summary>Gets the rollback failure, if rollback did not succeed.</summary>
    public Exception? RollbackFailure { get; }
}
