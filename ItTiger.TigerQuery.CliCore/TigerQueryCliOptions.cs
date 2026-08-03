using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// The TigerQuery configuration a host application supplies once and every part of a run
/// reads from: the application default connection-store file, and the store selected for
/// the current run once <see cref="TigerQueryCliContribution"/> has applied
/// <c>--tq-connection-store-file</c>.
/// </summary>
/// <remarks>
/// <para>
/// A host creates one instance, hands it to a <see cref="TigerQueryCliContribution"/>, and
/// passes that same instance to
/// <see cref="SqlServerConnectionCommandOptions.TigerQuery"/> and to its own command
/// factories and services. Two instances, or one instance registered and a different one
/// given to the commands, means the run resolves a store path that nothing reads — so
/// share exactly one.
/// </para>
/// <para>
/// The host-supplied members are set once at composition time. The run-selected members
/// (<see cref="ExplicitConnectionStoreFile"/>, <see cref="ResolvedStorePath"/>, and
/// <see cref="Store"/>) are filled in by the contribution callback, which TigerCli invokes
/// once per command run before command settings are bound. They are null, and
/// <see cref="Store"/> throws, until then — including throughout help rendering, which
/// never invokes callbacks.
/// </para>
/// <para>
/// This type is not thread-safe, and one app instance is not intended to run twice
/// concurrently. Sequential runs of the same app are supported: each run's callback
/// replaces the previous run's resolution and store rather than adding to it.
/// </para>
/// </remarks>
public sealed class TigerQueryCliOptions
{
    private SqlServerConnectionStore? store;

    /// <summary>Gets the application's default connection-store file path. Required.</summary>
    /// <remarks>
    /// TigerQuery defines no universal default, so the host must name one — typically from
    /// <see cref="SqlServerConnectionStoreOptions.Shared"/> or
    /// <see cref="SqlServerConnectionStoreOptions.AppSpecific"/>. It applies when neither
    /// <c>--tq-connection-store-file</c> nor
    /// <see cref="SqlServerConnectionStoreEnvironment.ConnectionStoreFile"/> supplies a
    /// value, and it is the one part of the resolution that is known at build time, so
    /// help text may mention it.
    /// </remarks>
    public required string DefaultConnectionStoreFile { get; init; }

    /// <summary>
    /// Gets the connection name the host treats as its default E2E bootstrap profile, or
    /// null when the host configures none.
    /// </summary>
    /// <remarks>
    /// Host configuration only: it names a convention and is used by
    /// <c>connections add-e2e-bootstrap</c> when that command receives no
    /// <c>--name</c>. It confers no authorization; a profile becomes usable for E2E work
    /// through explicit TigerQuery metadata, never through its name.
    /// </remarks>
    public string? DefaultE2eBootstrapConnectionName { get; init; }

    /// <summary>Gets the password strategy for the run's store, or null for the platform default.</summary>
    /// <remarks>
    /// Invoked once, when <see cref="Store"/> is first constructed. Protector selection
    /// stays a host concern and is independent of which file the run selected; a host that
    /// needs <see cref="NonPersistingConnectionPasswordProtector"/> on a build agent keeps
    /// it whichever way the path was chosen.
    /// </remarks>
    public Func<IConnectionPasswordProtector>? PasswordProtectorFactory { get; init; }

    /// <summary>Gets the environment lookup, or null to read the process environment.</summary>
    /// <remarks>
    /// Passed straight to
    /// <see cref="SqlServerConnectionStorePathOptions.EnvironmentReader"/>. Injecting it
    /// lets a test cover environment precedence without mutating process-global state.
    /// </remarks>
    public Func<string, string?>? EnvironmentReader { get; init; }

    /// <summary>
    /// Gets the value supplied to <c>--tq-connection-store-file</c> for the current run,
    /// or null when the option was absent or the callback has not run.
    /// </summary>
    /// <remarks>
    /// The raw value as typed. <see cref="ResolvedStorePath"/> carries the normalized
    /// absolute path actually selected, which is what diagnostics should show.
    /// </remarks>
    public string? ExplicitConnectionStoreFile { get; private set; }

    /// <summary>
    /// Gets the store-path resolution for the current run, or null before the contribution
    /// callback has run.
    /// </summary>
    /// <remarks>
    /// A failed resolution ends the run with a validation error before any command binds,
    /// so a successful command handler always observes a successful resolution.
    /// <see cref="SqlServerConnectionStorePathResolution.Source"/> reports whether the CLI
    /// option, the environment variable, or <see cref="DefaultConnectionStoreFile"/> won.
    /// </remarks>
    public SqlServerConnectionStorePathResolution? ResolvedStorePath { get; private set; }

    /// <summary>Gets the one connection store for the current run.</summary>
    /// <remarks>
    /// Constructed on first access from <see cref="ResolvedStorePath"/> and returned
    /// unchanged afterwards, so every command, provider, and host service that reads this
    /// property shares one instance — and therefore one file, one lock, and one in-process
    /// mutation gate.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The contribution callback has not run, which means the host did not register the
    /// contribution or shared a different options instance with it; or the callback
    /// resolved a failure, which normally ends the run before any command can ask.
    /// </exception>
    public SqlServerConnectionStore Store => store ??= CreateStore();

    /// <summary>
    /// Records the outcome of one contribution callback, discarding any store built for a
    /// previous run of the same app instance.
    /// </summary>
    internal void ApplyResolution(
        string? explicitConnectionStoreFile,
        SqlServerConnectionStorePathResolution resolution)
    {
        ExplicitConnectionStoreFile = explicitConnectionStoreFile;
        ResolvedStorePath = resolution;
        store = null;
    }

    private SqlServerConnectionStore CreateStore()
    {
        if (ResolvedStorePath is null)
            throw new InvalidOperationException(
                "The TigerQuery connection store was requested before the "
                + $"{TigerQueryCliContribution.ConnectionStoreFileOption} contribution ran. Register "
                + $"{nameof(TigerQueryCliContribution)} with TigerCliAppBuilder.AddContribution(...) and give "
                + $"it the same {nameof(TigerQueryCliOptions)} instance the commands and services use.");

        if (!ResolvedStorePath.IsSuccess)
            throw new InvalidOperationException(
                $"The TigerQuery connection-store path was not resolved: {ResolvedStorePath.ErrorMessage}");

        var storeOptions = new SqlServerConnectionStoreOptions
        {
            FilePath = ResolvedStorePath.FilePath!
        };

        return PasswordProtectorFactory is null
            ? new SqlServerConnectionStore(storeOptions)
            : new SqlServerConnectionStore(storeOptions, PasswordProtectorFactory());
    }
}
