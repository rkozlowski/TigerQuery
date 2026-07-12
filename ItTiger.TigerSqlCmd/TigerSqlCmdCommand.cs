using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerQuery.Engine;
using ItTiger.TigerSqlCmd.Logging;
using Microsoft.Extensions.Logging;


namespace ItTiger.TigerSqlCmd;

/// <summary>
/// The advanced <c>run</c> command: full sqlcmd-style script/query execution with
/// variables, mode, verbosity and logging. Automation-friendly — it never prompts for
/// the script/query, though it may prompt for a missing connection when interactive.
/// </summary>
public sealed class TigerSqlCmdCommand : TigerCliAsyncCommandHandler<TigerSqlCmdSettings, TigerSqlCmdExitCode>
{
    public override async Task<TigerSqlCmdExitCode> ExecuteAsync(TigerSqlCmdSettings settings)
    {
        ILogger? logger = null;
        var verbosity = settings.Verbosity;
        if (!string.IsNullOrWhiteSpace(settings.LogFile))
        {
            var loggerFactory = NLogSetup.CreateLoggerFactory(settings.LogFile, settings.LogLevel);
            logger = loggerFactory.CreateLogger("TigerSqlCmd");
            logger.LogInformation("Starting tiger-sqlcmd with mode: {Mode}", settings.Mode);
        }

        var variables = settings.Variables.ToDictionary();
        if (verbosity >= Verbosity.Normal)
        {
            TigerConsole.MarkupLine(settings.E("[Muted]Mode:[/] {0}", settings.Mode));
            if (verbosity == Verbosity.VeryVerbose)
            {
                // The saved connection name, never the resolved connection string (which may carry a password).
                TigerConsole.MarkupLine(settings.E("[Muted]Using connection:[/] [Value]{0}[/]", settings.Connection));
            }

            if (verbosity >= Verbosity.Verbose)
            {
                if (settings.FilePath != null)
                    TigerConsole.MarkupLine(settings.E("[Muted]Executing file:[/] [Path]{0}[/]", settings.FilePath));
                else
                    TigerConsole.MarkupLine(settings.E("[Muted]Executing query:[/] [Value]{0}[/]", settings.Query ?? ""));

                if (variables.Any())
                {
                    TigerConsole.MarkupLine(settings.T("[Muted]Variables:[/]"));
                    foreach (var (key, value) in variables)
                        TigerConsole.MarkupLine($"  [Key]{CliMarkupParser.Escape(key)}[/] = [Value]{CliMarkupParser.Escape(value)}[/]");
                }
            }
        }

        // Resolve the saved connection profile to a connection string before building engine
        // options. The store is reused (no persistence logic duplicated here); the engine only
        // ever sees a plain connection string.
        if (!TigerSqlCmdApp.TryResolveConnection(settings.Connection, logger, out var connectionString, out var failureExitCode))
            return failureExitCode;

        var renderer = new TigerSqlCmdRenderer(verbosity, settings);
        var options = new TigerQueryEngineOptions
        {
            ConnectionString = connectionString,
            Mode = settings.Mode,
            Variables = variables,
            Logger = logger,
            OnMessage = renderer.WriteMessage,
            OnBatchStart = renderer.WriteBatchStart,
            OnBatchEnd = renderer.WriteBatchEnd,
            OnResultSet = renderer.WriteResultSet
        };

        var engine = new TigerQueryEngine(options);
        return await TigerSqlCmdEngineRunner.RunAsync(logger, token =>
            !string.IsNullOrWhiteSpace(settings.FilePath)
                ? engine.RunFromFileAsync(settings.FilePath, cancellationToken: token)
                : engine.RunFromStringAsync(settings.Query ?? "", token));
    }
}
