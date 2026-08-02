namespace ItTiger.TigerQuery.Core;

/// <summary>The environment variables TigerQuery itself defines, documents, and reads.</summary>
/// <remarks>
/// Reading these variables belongs here, not in a CLI framework or a host application.
/// A TigerCli-based host may register the name as help metadata so it appears in
/// <c>--help-env</c>, but the lookup and its precedence are owned by
/// <see cref="SqlServerConnectionStorePathResolver"/>.
/// </remarks>
public static class SqlServerConnectionStoreEnvironment
{
    /// <summary>
    /// The variable that selects the connection-store JSON file when no explicit path
    /// is supplied: <c>TIGERQUERY_CONNECTION_STORE_FILE</c>.
    /// </summary>
    /// <remarks>
    /// Intended for CI/CD, containers, and build agents. A local developer normally
    /// leaves it unset and uses the host application's default store location. When it
    /// is set, it outranks that default and is never silently ignored: a present but
    /// unusable value fails resolution instead of falling back.
    /// </remarks>
    public const string ConnectionStoreFile = "TIGERQUERY_CONNECTION_STORE_FILE";
}
