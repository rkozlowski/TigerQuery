using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerCli.Tui.Themes;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd.Resources;
using Microsoft.Extensions.Logging;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// Composes the tiger-sqlcmd <see cref="TigerCliApp"/>. Kept separate from
/// <see cref="Program"/> so the wiring (commands, the saved-connection provider, and the
/// run-selected store) can be exercised by tests against a temporary store location.
/// </summary>
internal static class TigerSqlCmdApp
{
    /// <summary>
    /// The machine-wide SQL Server connection store shared across Tiger tools, so
    /// connections saved here are visible to the other tools and vice versa.
    /// </summary>
    /// <remarks>
    /// This is the application default only. A run may point somewhere else with
    /// <c>--tq-connection-store-file</c> or
    /// <see cref="SqlServerConnectionStoreEnvironment.ConnectionStoreFile"/>; choosing
    /// between the three belongs to TigerQuery, not to this host.
    /// </remarks>
    public static string DefaultConnectionStoreFile =>
        SqlServerConnectionStoreOptions.Shared("ItTiger.net").FilePath;

    /// <summary>The profile name used by <c>connection add-e2e-bootstrap</c> by default.</summary>
    public const string DefaultE2eBootstrapConnectionName = "tiger-sqlcmd-e2e";

    /// <summary>The one command path that consumes a <c>--</c> child command line.</summary>
    public const string ExecCommandName = "exec";

    private const string ExecCommandDescription =
        "Run an external program against a saved connection, passing it the resolved connection "
        + "string. The program is started directly; no shell is involved, so nothing re-parses, "
        + "expands, or globs the child command line.\n"
        + "Everything after [Key]--[/] is the child executable and its arguments. At least one "
        + "handoff is required, and both together are allowed:\n"
        + "[Key]{connection-string}[/] in any child argument is replaced by the resolved "
        + "connection string, also inside a larger argument; all other text is preserved.\n"
        + "[Key]--connection-string-env <variable-name>[/] sets that variable to the resolved "
        + "connection string for the child process only.\n"
        + "Prefer the environment variable when the child supports it: an argument is visible to "
        + "anyone who can list processes on the machine.\n"
        + "[Muted]tiger-sqlcmd exec -c prod --connection-string-env DB_CONNECTION -- my-tool --report[/]\n"
        + "[Muted]tiger-sqlcmd exec -c prod -- my-tool --target={connection-string} --report[/]\n"
        + "The child inherits stdin, stdout, stderr, the working directory, and the rest of the "
        + "environment, and its exit code is returned unchanged. Exit code 21 means the child "
        + "could not be started at all.";

    /// <summary>
    /// Resolves a saved connection name to a connection string via the run's store and the
    /// shared resolver. On failure, prints a clean error (never the connection string) and
    /// returns false with the <see cref="TigerSqlCmdExitCode.ConnectionFailed"/> exit code.
    /// </summary>
    internal static bool TryResolveConnection(
        TigerQueryCliOptions tigerQuery,
        string? connectionName,
        ILogger? logger,
        out string connectionString,
        out TigerSqlCmdExitCode failureExitCode)
    {
        // Reaching the store here is safe: TigerCli applies the contributed option before
        // it binds settings, so a command handler always runs after the store was selected.
        var resolution = SqlServerConnectionResolver.Resolve(tigerQuery.Store, connectionName);
        if (!resolution.IsSuccess)
        {
            logger?.LogError("Connection resolution failed: {Error}", resolution.ErrorMessage);
            TigerConsole.MarkupErrorLine($"[Error]{CliMarkupParser.Escape(resolution.ErrorMessage!)}[/]");
            connectionString = string.Empty;
            failureExitCode = TigerSqlCmdExitCode.ConnectionFailed;
            return false;
        }

        connectionString = resolution.ConnectionString!;
        failureExitCode = TigerSqlCmdExitCode.Ok;
        return true;
    }

    /// <summary>Builds the app over the application-default connection store.</summary>
    public static TigerCliApp Build() => Build(DefaultConnectionStoreFile);

    /// <summary>
    /// Splits a raw process argument list and builds the app for it, returning the arguments
    /// TigerCli should parse.
    /// </summary>
    /// <remarks>
    /// The <c>--</c> child command line has to be removed before TigerCli sees the arguments
    /// and given to the <c>exec</c> factory while the app is composed, so both halves belong
    /// to one step. <see cref="Program"/> and the CLI tests share it, which is what keeps a
    /// test run's argument handling identical to a shipped run's.
    /// </remarks>
    internal static (TigerCliApp App, string[] HostArguments) Compose(
        IReadOnlyList<string> arguments,
        string? defaultConnectionStoreFile = null,
        Func<string, string?>? environmentReader = null)
    {
        var (hostArguments, childCommandLine) = TigerSqlCmdChildCommandLine.Split(arguments);
        var app = Build(
            defaultConnectionStoreFile ?? DefaultConnectionStoreFile,
            environmentReader,
            childCommandLine);
        return (app, hostArguments);
    }

    /// <summary>
    /// Builds the app over a chosen application-default store path, and optionally a
    /// substitute environment lookup.
    /// </summary>
    /// <param name="defaultConnectionStoreFile">
    /// The store used when the run supplies no override. Tests pass a temporary file here
    /// so they never touch the developer's real store.
    /// </param>
    /// <param name="environmentReader">
    /// The lookup behind <see cref="SqlServerConnectionStoreEnvironment.ConnectionStoreFile"/>,
    /// or null to read the process environment as a shipped run does.
    /// </param>
    /// <param name="childCommandLine">
    /// The tokens after <c>--</c> for <c>exec</c>, or null when the run supplied no
    /// separator. Normally produced by <see cref="Compose"/>.
    /// </param>
    /// <remarks>
    /// Injection replaces the *application default* deliberately, so
    /// <c>--tq-connection-store-file</c> and the environment variable still outrank it. A
    /// test that pinned a store above both would prove the opposite of the shipping
    /// precedence.
    /// </remarks>
    internal static TigerCliApp Build(
        string defaultConnectionStoreFile,
        Func<string, string?>? environmentReader = null,
        IReadOnlyList<string>? childCommandLine = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultConnectionStoreFile);

        // One contribution, one options instance: registered below, given to the connection
        // command group, the app-level provider, and both command factories. Two instances
        // would leave this app resolving a store path that nothing reads.
        var tigerQuery = new TigerQueryCliContribution(new TigerQueryCliOptions
        {
            DefaultConnectionStoreFile = defaultConnectionStoreFile,
            DefaultE2eBootstrapConnectionName = DefaultE2eBootstrapConnectionName,
            EnvironmentReader = environmentReader
        });
        var connections = tigerQuery.Options;

        // Set here rather than in Program so test-hosted runs use the production theme.
        TigerConsole.CurrentTheme = new TigerBlueTheme();

        return TigerCliApp.CreateBuilder()
            // Application name, display name, description, version (--version /
            // --version-full), copyright, and the documentation/repository help-footer
            // links all come from project metadata (csproj + Version.props).
            .UseAssemblyMetadata(typeof(TigerSqlCmdApp).Assembly)
            // en-US is the default; --culture pl-PL localizes framework text, app/CliCore
            // metadata, and command output. App strings are chained in front of the reusable
            // connection-command strings so one manager serves both (UseAppResources takes one).
            .SetDefaultCulture("en-US")
            .SetSupportedCultures("en-US", "pl-PL")
            .UseAppResources(SqlServerConnectionCommands.CreateAppResources(TigerSqlCmdStrings.ResourceManager))
            .AddDescription(
                "Run a [Accent]SQL Server[/] query against a saved connection. Use [Accent]run[/] for scripts and advanced sqlcmd options.",
                resourceKey: "App_Description")
            // Contributes --tq-connection-store-file and the TIGERQUERY_CONNECTION_STORE_FILE
            // help entry. This host registers no store-path option or environment variable of
            // its own: the name, precedence, and validation all belong to TigerQuery.
            .AddContribution(tigerQuery)
            // Engine outcomes keep their historical 0–7 values; usage failures map to
            // code 20 (plus Cancelled → 3 to match the engine's meaning).
            .UseExitCodes(TigerSqlCmdExitCode.Ok, TigerSqlCmdExitCode.UnhandledException)
            .ExitCategory(TigerCliExitCategory.Usage, TigerSqlCmdExitCode.InvalidArguments)
            .ExitKind(TigerCliExitKind.Cancelled, TigerSqlCmdExitCode.Cancelled)
            // Reusable connection handlers return portable semantic kinds. Exact-kind
            // mappings preserve their historical 0/2/4/5 process contract; the aliases
            // remain owned and documented by this host. A store-path failure from the
            // contribution arrives as ValidationError and lands on the same code.
            .ExitKind(TigerCliExitKind.ValidationError, TigerSqlCmdExitCode.ConnectionInvalidArguments)
            .ExitKind(TigerCliExitKind.NotFound, TigerSqlCmdExitCode.ConnectionNotFound)
            .ExitKind(TigerCliExitKind.AlreadyExists, TigerSqlCmdExitCode.ConnectionAlreadyExists)
            // App-scoped so it backs -c|--connection on every command form: a missing
            // connection is prompted from the saved connections in interactive mode and
            // fails in non-interactive mode. Reading the store inside the callback keeps it
            // deferred — providers run during prompting, well after the option is applied.
            .ConfigureProviders(providers =>
                providers.Add(
                    "connections",
                    ctx => connections.Store.GetConnectionNamesAsync(ctx.CancellationToken)))
            // Default command: the basic, friendly query runner.
            .SetDefaultCommand(() => new TigerSqlCmdQueryCommand(connections))
            .AddCommandGroup("connection", group =>
            {
                group.SetDescription("Manage saved connections",
                    resourceKey: "Grp_Connections_Description");
                SqlServerConnectionCommands.Configure(group, options =>
                {
                    options.TigerQuery = connections;
                    // Database is optional for tiger-sqlcmd connection commands.
                    options.ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional;
                });
            })
            .AddCommandGroup("e2e", group =>
            {
                group.SetDescription("Manage session-scoped E2E databases and connections.");
                group.SetPromptMode(ItTiger.TigerCli.Enums.TigerCliPromptMode.RequiredOnly);
                group.AddCommand(
                    "create",
                    () => new TigerSqlCmdE2eCreateCommand(connections),
                    "Create an E2E database and its paired owning connection.");
                group.AddCommand(
                    "drop",
                    () => new TigerSqlCmdE2eDropCommand(connections),
                    "Safely drop or detach one session-owned E2E connection.");
                group.AddCommand(
                    "cleanup",
                    () => new TigerSqlCmdE2eCleanupCommand(connections),
                    "Clean all protected E2E connection records for one session.");
            })
            // Advanced command: full script/sqlcmd execution.
            .AddCommand(
                "run",
                () => new TigerSqlCmdCommand(connections),
                "Advanced sqlcmd execution: run a script file or query with variables, output routing, mode, verbosity and logging.",
                descriptionResourceKey: "Cmd_Run_Description")
            // Generic subprocess bridge for tools that need a connection string and cannot
            // read a saved connection name. The child command line was already split off the
            // argument list by Compose; nothing about it reaches the parser.
            .AddCommand(
                ExecCommandName,
                () => new TigerSqlCmdExecCommand(connections, childCommandLine),
                ExecCommandDescription)
            .Build();
    }
}
