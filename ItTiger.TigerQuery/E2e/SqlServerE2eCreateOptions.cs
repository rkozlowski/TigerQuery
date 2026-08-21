namespace ItTiger.TigerQuery.E2e;

/// <summary>
/// What one <see cref="SqlServerE2eSessionLifecycle.CreateAsync(System.Guid, SqlServerE2eCreateOptions, System.Threading.CancellationToken)"/>
/// call should produce: the name parts for the paired resources, and the capabilities the
/// new database itself is provisioned with.
/// </summary>
/// <remarks>
/// Capabilities here describe the database being created, not the bootstrap connection.
/// The bootstrap stays a plain authorized route to <c>master</c>: nothing on this type is
/// read from or written to its metadata, and two calls with different options against the
/// same bootstrap are independent.
/// </remarks>
public sealed class SqlServerE2eCreateOptions
{
    /// <summary>Gets the name part for the generated database, or null for the default.</summary>
    public string? DatabaseNamePart { get; init; }

    /// <summary>Gets the name part for the generated connection, or null for the default.</summary>
    public string? ConnectionNamePart { get; init; }

    /// <summary>
    /// Gets whether the new database is provisioned so it can host memory-optimized tables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is <see langword="false"/>, which creates an ordinary disposable
    /// database. When it is <see langword="true"/>, the created database also receives a
    /// <c>MEMORY_OPTIMIZED_DATA</c> filegroup and its data container, and the create call
    /// reports success only once SQL Server's own catalog confirms both.
    /// </para>
    /// <para>
    /// Provisioning is generic in-memory OLTP support and creates no application schema.
    /// Teardown is unchanged: the paired connection still records
    /// <c>ittiger.e2e.database.allow-drop=true</c>, and the ordinary forced owned-database
    /// teardown removes the database and its container with no special caller action.
    /// </para>
    /// </remarks>
    public bool MemoryOptimized { get; init; }
}
