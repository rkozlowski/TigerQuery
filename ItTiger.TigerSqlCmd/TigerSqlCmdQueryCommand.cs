using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.Engine;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// The basic default command: a friendly, interactive query runner for humans. Prompts
/// for the saved connection and the SQL query when missing (in interactive mode), resolves
/// the connection through the shared resolver, and executes the query with default engine
/// settings. Advanced behavior (files, variables, mode, logging) lives in <c>run</c>.
/// </summary>
public sealed class TigerSqlCmdQueryCommand : TigerCliAsyncCommandHandler<TigerSqlCmdQuerySettings, TigerSqlCmdExitCode>
{
    public override async Task<TigerSqlCmdExitCode> ExecuteAsync(TigerSqlCmdQuerySettings settings)
    {
        // The engine only ever receives a plain connection string; saved profiles stay behind
        // the resolver, and the resolved string is never printed.
        if (!TigerSqlCmdApp.TryResolveConnection(settings.Connection, logger: null, out var connectionString, out var failureExitCode))
            return failureExitCode;

        var renderer = new TigerSqlCmdRenderer(Verbosity.Normal, settings);
        var options = new TigerQueryEngineOptions
        {
            ConnectionString = connectionString,
            OnMessage = renderer.WriteMessage,
            OnBatchStart = renderer.WriteBatchStart,
            OnBatchEnd = renderer.WriteBatchEnd,
            OnResultSet = renderer.WriteResultSet
        };

        var engine = new TigerQueryEngine(options);
        return await TigerSqlCmdEngineRunner.RunAsync(logger: null, token =>
            engine.RunFromStringAsync(settings.Query, token));
    }
}
