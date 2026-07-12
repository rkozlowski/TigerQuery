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
/// shared store) can be exercised by tests with an injected store.
/// </summary>
internal static class TigerSqlCmdApp
{
    // The commands are parameterless-constructed by TigerCli, so the configured store is
    // shared through this ambient (set by Build) rather than a constructor.
    internal static SqlServerConnectionStore? ConnectionStore { get; private set; }

    /// <summary>
    /// The machine-wide SQL Server connection store shared across Tiger tools, so
    /// connections saved here are visible to the other tools and vice versa.
    /// </summary>
    public static SqlServerConnectionStore CreateDefaultStore() =>
        new(SqlServerConnectionStoreOptions.Shared("ItTiger.net"));

    /// <summary>
    /// Resolves a saved connection name to a connection string via the shared store and
    /// resolver. On failure, prints a clean error (never the connection string) and returns
    /// false with the <see cref="TigerSqlCmdExitCode.ConnectionFailed"/> exit code.
    /// </summary>
    internal static bool TryResolveConnection(
        string? connectionName,
        ILogger? logger,
        out string connectionString,
        out TigerSqlCmdExitCode failureExitCode)
    {
        var store = ConnectionStore
            ?? throw new InvalidOperationException("tiger-sqlcmd connection store was not configured.");

        var resolution = SqlServerConnectionResolver.Resolve(store, connectionName);
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

    public static TigerCliApp Build(SqlServerConnectionStore connectionStore)
    {
        ArgumentNullException.ThrowIfNull(connectionStore);

        ConnectionStore = connectionStore;

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
            // Engine outcomes keep their historical 0–7 values; framework outcomes map to
            // the documented 20/21 band (plus Cancelled → 3 to match the engine's meaning).
            .UseExitCodes(TigerSqlCmdExitCode.Ok, TigerSqlCmdExitCode.UnhandledException)
            .ExitCategory(TigerCliExitCategory.Usage, TigerSqlCmdExitCode.InvalidArguments)
            .ExitCategory(TigerCliExitCategory.Validation, TigerSqlCmdExitCode.ValidationError)
            .ExitKind(TigerCliExitKind.Cancelled, TigerSqlCmdExitCode.Cancelled)
            // App-scoped so it backs -c|--connection on every command form: a missing
            // connection is prompted from the saved connections in interactive mode and
            // fails in non-interactive mode.
            .ConfigureProviders(providers =>
                providers.Add(
                    "connections",
                    ctx => connectionStore.GetConnectionNamesAsync(ctx.CancellationToken)))
            // Default command: the basic, friendly query runner.
            .SetDefaultCommand<TigerSqlCmdQueryCommand>()
            .AddCommandGroup("connections", group =>
            {
                group.SetDescription("Manage saved connections",
                    resourceKey: "Grp_Connections_Description");
                SqlServerConnectionCommands.Configure(group, options =>
                {
                    options.Store = connectionStore;
                    // Database is optional for tiger-sqlcmd connections.
                    options.ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional;
                });
            })
            // Advanced command: full script/sqlcmd execution.
            .AddCommand<TigerSqlCmdCommand>(
                "run",
                "Advanced sqlcmd execution: run a script file or query with variables, mode, verbosity and logging.",
                descriptionResourceKey: "Cmd_Run_Description")
            .Build();
    }
}
