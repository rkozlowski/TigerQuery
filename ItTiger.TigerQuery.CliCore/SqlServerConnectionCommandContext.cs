using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// The store and policy the mounted connection commands share. The store is reached
/// through an accessor rather than captured, because a run-selected store is not known
/// when <see cref="SqlServerConnectionCommands.Configure"/> builds the command factories.
/// </summary>
internal sealed class SqlServerConnectionCommandContext(
    Func<SqlServerConnectionStore> storeAccessor,
    SqlServerConnectionValidationPolicy validationPolicy,
    string? defaultE2eBootstrapConnectionName = null)
{
    /// <summary>Gets the store for the current run.</summary>
    /// <remarks>
    /// The accessor returns the same instance for the whole run, so the handlers, the two
    /// providers, and the edit loader share one file, one lock, and one mutation gate.
    /// </remarks>
    public SqlServerConnectionStore Store => storeAccessor();

    public SqlServerConnectionValidationPolicy ValidationPolicy { get; } = validationPolicy;

    /// <summary>Gets the host's optional bootstrap-name convention.</summary>
    public string? DefaultE2eBootstrapConnectionName { get; } =
        defaultE2eBootstrapConnectionName;
}
