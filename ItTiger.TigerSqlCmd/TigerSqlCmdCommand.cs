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
public sealed class TigerSqlCmdCommand : TigerCliAsyncCommandHandler<TigerSqlCmdSettings>
{
    public override async Task<int> ExecuteAsync(TigerSqlCmdSettings settings)
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
            TigerConsole.MarkupLine(settings.E("[gray]Mode:[/] {0}", settings.Mode));
            if (verbosity == Verbosity.VeryVerbose)
            {
                // The saved connection name, never the resolved connection string (which may carry a password).
                TigerConsole.MarkupLine(settings.E("[gray]Using connection:[/] [blue]{0}[/]", settings.Connection));
            }

            if (verbosity >= Verbosity.Verbose)
            {
                if (settings.FilePath != null)
                    TigerConsole.MarkupLine(settings.E("[gray]Executing file:[/] [green]{0}[/]", settings.FilePath));
                else
                    TigerConsole.MarkupLine(settings.E("[gray]Executing query:[/] [green]{0}[/]", settings.Query ?? ""));

                if (variables.Any())
                {
                    TigerConsole.MarkupLine(settings.T("[gray]Variables:[/]"));
                    foreach (var (key, value) in variables)
                        TigerConsole.MarkupLine($"  [cyan]{CliMarkupParser.Escape(key)}[/] = [yellow]{CliMarkupParser.Escape(value)}[/]");
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
        ExecutionResult result;
        if (!string.IsNullOrWhiteSpace(settings.FilePath))
        {
            result = await engine.RunFromFileAsync(settings.FilePath);
        }
        else
        {
            result = await engine.RunFromStringAsync(settings.Query ?? "");
        }
        return (int)result.ResultCode;
    }
}
