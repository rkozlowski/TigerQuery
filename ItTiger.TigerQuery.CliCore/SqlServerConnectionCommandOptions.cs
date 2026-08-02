using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Supplies application-owned dependencies when mounting the reusable SQL Server
/// connection command group.
/// </summary>
/// <remarks>
/// Store selection has two forms and the host picks exactly one.
/// <see cref="TigerQuery"/> defers the choice to the run, so
/// <c>--tq-connection-store-file</c> and the TigerQuery environment variable can decide
/// it; <see cref="Store"/> pins one store at composition time for a host with no
/// contribution. <see cref="SqlServerConnectionCommands.Configure"/> rejects both and
/// neither.
/// </remarks>
public sealed class SqlServerConnectionCommandOptions
{
    /// <summary>Gets or sets one fixed connection-profile store for every run.</summary>
    /// <remarks>
    /// The simple form, for a host that selects its store itself and offers no store-path
    /// option. Mutually exclusive with <see cref="TigerQuery"/>: a host that registers
    /// <see cref="TigerQueryCliContribution"/> sets that property instead, because a store
    /// constructed here would ignore whatever the run selected.
    /// </remarks>
    public SqlServerConnectionStore? Store { get; set; }

    /// <summary>
    /// Gets or sets the contribution-owned state the commands read their store from at
    /// execution time.
    /// </summary>
    /// <remarks>
    /// This must be the same <see cref="TigerQueryCliOptions"/> instance given to the
    /// <see cref="TigerQueryCliContribution"/> the host registered, and to the host's own
    /// commands and services. The commands do not touch it until they run, which is after
    /// the contribution callback has selected the run's store. Mutually exclusive with
    /// <see cref="Store"/>.
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
