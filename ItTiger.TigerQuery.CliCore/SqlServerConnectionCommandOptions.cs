using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Supplies application-owned dependencies when mounting the reusable SQL Server
/// connection command group.
/// </summary>
public sealed class SqlServerConnectionCommandOptions
{
    /// <summary>Gets or sets the required connection-profile store.</summary>
    /// <remarks>
    /// <see cref="SqlServerConnectionCommands.Configure"/> throws if this remains
    /// <see langword="null"/>.
    /// </remarks>
    public SqlServerConnectionStore? Store { get; set; }

    /// <summary>Gets or sets the profile validation policy used by add and edit.</summary>
    /// <remarks>
    /// The default permits server-level profiles without an initial database.
    /// A null value is rejected during command-group configuration.
    /// </remarks>
    public SqlServerConnectionValidationPolicy ValidationPolicy { get; set; } =
        SqlServerConnectionValidationPolicy.DatabaseOptional;
}
