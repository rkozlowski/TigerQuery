namespace ItTiger.TigerQuery.Core;

/// <summary>
/// The inputs <see cref="SqlServerConnectionStorePathResolver"/> chooses a
/// connection-store file path from: an optional caller override, the TigerQuery
/// environment variable, and the host application's default location.
/// </summary>
/// <remarks>
/// The same options serve a command-line application, a test library, a service, and a
/// container. Only the source of <see cref="ExplicitFilePath"/> differs — a CLI host fills
/// it from a global option, a library caller passes it directly.
/// </remarks>
public sealed class SqlServerConnectionStorePathOptions
{
    /// <summary>Gets the caller-supplied override, or null when none was supplied.</summary>
    /// <remarks>
    /// Null means "not supplied" and lets the environment variable and application
    /// default apply. An empty or whitespace value means "supplied but unusable" and
    /// fails resolution; the two are deliberately not the same.
    /// </remarks>
    public string? ExplicitFilePath { get; init; }

    /// <summary>Gets the host application's default store location. Required.</summary>
    /// <remarks>
    /// TigerQuery has no universal default, so the host must name one — typically from
    /// <see cref="SqlServerConnectionStoreOptions.Shared"/> or
    /// <see cref="SqlServerConnectionStoreOptions.AppSpecific"/>. It is validated like
    /// any other source, so a blank or malformed default fails resolution rather than
    /// producing an unusable path.
    /// </remarks>
    public required string DefaultFilePath { get; init; }

    /// <summary>Gets the environment variable consulted between the override and the default.</summary>
    /// <remarks>
    /// Defaults to <see cref="SqlServerConnectionStoreEnvironment.ConnectionStoreFile"/>.
    /// Overriding it is intended for tests and for a host that must coexist with an
    /// established variable of its own; changing it changes a documented contract.
    /// </remarks>
    public string EnvironmentVariableName { get; init; } =
        SqlServerConnectionStoreEnvironment.ConnectionStoreFile;

    /// <summary>Gets the environment lookup, or null to read the process environment.</summary>
    /// <remarks>
    /// The lookup takes a variable name and returns its value, or null when the variable
    /// is not set. Injecting it lets a test cover environment precedence without mutating
    /// process-global state, which is unsafe alongside parallel tests. The default is
    /// <see cref="Environment.GetEnvironmentVariable(string)"/>.
    /// </remarks>
    public Func<string, string?>? EnvironmentReader { get; init; }
}
