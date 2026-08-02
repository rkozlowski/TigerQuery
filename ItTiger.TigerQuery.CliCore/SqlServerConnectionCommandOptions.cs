using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Supplies application-owned dependencies when mounting the reusable SQL Server
/// connection command group.
/// </summary>
/// <remarks>
/// The store is always chosen by the run rather than at composition time, through the
/// <see cref="TigerQueryCliOptions"/> the host also gives to its
/// <see cref="TigerQueryCliContribution"/>. There is deliberately no way to pin one
/// already-constructed store here, because such a store would ignore whatever
/// <c>--tq-connection-store-file</c> or the TigerQuery environment variable selected.
/// </remarks>
public sealed class SqlServerConnectionCommandOptions
{
    /// <summary>
    /// Gets or sets the contribution-owned state the commands read their store from at
    /// execution time. Required.
    /// </summary>
    /// <remarks>
    /// This must be the same <see cref="TigerQueryCliOptions"/> instance given to the
    /// <see cref="TigerQueryCliContribution"/> the host registered, and to the host's own
    /// commands and services. The commands do not touch it until they run, which is after
    /// the contribution callback has selected the run's store.
    /// </remarks>
    public TigerQueryCliOptions? TigerQuery { get; set; }

    /// <summary>Gets or sets the profile validation policy used by add and edit.</summary>
    /// <remarks>
    /// The default permits server-level profiles without an initial database.
    /// A null value is rejected during command-group configuration.
    /// </remarks>
    public SqlServerConnectionValidationPolicy ValidationPolicy { get; set; } =
        SqlServerConnectionValidationPolicy.DatabaseOptional;
}
