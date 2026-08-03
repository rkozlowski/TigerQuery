using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.E2e;

/// <summary>Configures one safe SQL Server E2E database lifecycle.</summary>
public sealed class SqlServerE2eDatabaseLifecycleOptions
{
    /// <summary>The default prefix used for generated E2E database names.</summary>
    public const string DefaultDatabasePrefix = "_TQ_E2E_";

    /// <summary>Gets the prefix used for the unique database name.</summary>
    /// <remarks>
    /// Hosts may replace the default. The prefix is also checked immediately before
    /// cleanup, but a match is only a defensive guard and never establishes ownership.
    /// </remarks>
    public string DatabasePrefix { get; init; } = DefaultDatabasePrefix;

    /// <summary>Gets the readers used to resolve external connection values at operation time.</summary>
    /// <remarks>
    /// Resolved values are used only to build an in-memory effective connection string.
    /// They are never copied back into the profile or connection store.
    /// </remarks>
    public SqlServerExternalValueResolutionOptions? ExternalValues { get; init; }
}
