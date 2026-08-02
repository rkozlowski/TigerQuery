namespace ItTiger.TigerQuery.Core;

/// <summary>Identifies which configuration source selected a connection-store file path.</summary>
/// <remarks>
/// Reported on success so diagnostics can prove which store an operation used, and on
/// failure to name the source that supplied the unusable value. The declaration order is
/// the precedence order.
/// </remarks>
public enum SqlServerConnectionStorePathSource
{
    /// <summary>A path supplied directly by the caller, such as a command-line option.</summary>
    Explicit = 0,

    /// <summary>The TigerQuery store-path environment variable.</summary>
    EnvironmentVariable = 1,

    /// <summary>The host application's own default store location.</summary>
    ApplicationDefault = 2
}
