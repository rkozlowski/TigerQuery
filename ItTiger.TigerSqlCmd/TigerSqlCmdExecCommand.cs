using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerQuery.CliCore;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// Settings for <c>exec</c>. The child executable and its arguments are not settings: they
/// arrive through the <c>--</c> passthrough that <see cref="TigerSqlCmdChildCommandLine"/>
/// removes before TigerCli parses anything.
/// </summary>
internal sealed class TigerSqlCmdExecSettings : TigerCliSettings
{
    // Same contract as every other TigerSqlCmd command: a saved connection name, prompted
    // from the saved connections when interactive, failing in non-interactive mode.
    [TigerCliOption("-c|--connection",
        ValueName = "name",
        Required = true,
        Description = "Name of a saved SQL Server connection (managed via the 'connection' command).",
        DescriptionResourceKey = "Opt_Connection_Description",
        Provider = "connections",
        Promptable = TigerCliPromptable.Normal)]
    public string Connection { get; set; } = default!;

    [TigerCliOption("--connection-string-env",
        ValueName = "variable-name",
        Description = "Set this environment variable to the resolved connection string for the "
            + "child process only. Preferred over argument substitution when the child can read it.")]
    public string? ConnectionStringEnvironmentVariable { get; set; }
}

/// <summary>
/// The <c>exec</c> command: resolves a saved connection and hands the resulting connection
/// string to a directly started child process, by argument substitution, by a child-only
/// environment variable, or both.
/// </summary>
/// <param name="connections">
/// The run-shared TigerQuery state the app composed, from which this command reads the
/// store the run selected.
/// </param>
/// <param name="childCommandLine">
/// The tokens after <c>--</c>, or null when the run supplied no separator.
/// </param>
/// <remarks>
/// The handler returns a raw <see cref="int"/> rather than
/// <see cref="TigerSqlCmdExitCode"/> because TigerCli passes an integer handler result
/// through to the process unmapped, which is what lets the child's own exit code survive.
/// </remarks>
internal sealed class TigerSqlCmdExecCommand(
    TigerQueryCliOptions connections,
    IReadOnlyList<string>? childCommandLine)
    : TigerCliAsyncCommandHandler<TigerSqlCmdExecSettings>
{
    public override async Task<int> ExecuteAsync(TigerSqlCmdExecSettings settings)
    {
        // Handoff configuration is validated first and on its own: an invalid command line
        // must never reach the connection store, let alone resolve an external secret.
        if (!TigerSqlCmdExecPlan.TryCreate(
                childCommandLine,
                settings.ConnectionStringEnvironmentVariable,
                out var plan,
                out var error))
        {
            TigerConsole.MarkupErrorLine($"[Error]{CliMarkupParser.Escape(error!)}[/]");
            return (int)TigerSqlCmdExitCode.InvalidArguments;
        }

        // No logger: exec has no log-file option, and the resolved connection string must not
        // reach a log even if one existed.
        if (!TigerSqlCmdApp.TryResolveConnection(
                connections, settings.Connection, logger: null, out var connectionString, out var failureExitCode))
        {
            return (int)failureExitCode;
        }

        return await TigerSqlCmdChildProcess.RunAsync(plan!, connectionString).ConfigureAwait(false);
    }
}
