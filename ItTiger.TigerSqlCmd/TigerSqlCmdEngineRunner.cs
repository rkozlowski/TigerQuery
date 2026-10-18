using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerQuery;
using ItTiger.TigerQuery.Engine;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// Shared engine-execution wrapper for the basic and advanced query commands: runs the
/// engine under a Ctrl+C cancellation scope and maps the failure modes that escape the
/// engine (it handles SQL errors inside batches itself) onto the exit-code contract.
/// </summary>
internal static class TigerSqlCmdEngineRunner
{
    /// <param name="logger">The run's optional log-file logger.</param>
    /// <param name="connectionName">
    /// The saved connection name, never the resolved connection string, used to say which
    /// connection could not be opened.
    /// </param>
    /// <param name="run">Starts the engine under the supplied cancellation token.</param>
    public static async Task<TigerSqlCmdExitCode> RunAsync(
        ILogger? logger,
        string connectionName,
        Func<CancellationToken, Task<ExecutionResult>> run)
    {
        using var cancellation = new ConsoleCancellationScope();
        try
        {
            var result = await run(cancellation.Token);
            return result.ResultCode.ToExitCode();
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C while the connection was still being opened (cancellation during
            // batch execution is already mapped by the engine to UserCancelled).
            logger?.LogWarning("Execution cancelled by user.");
            return TigerSqlCmdExitCode.Cancelled;
        }
        catch (OutputRoutingException ex)
        {
            // Initial-route and prepared-plan failures can occur before the engine
            // reaches its ExecutionResult path. They use the same dedicated process
            // code as output failures returned by the execution coordinator.
            logger?.LogError(ex, "Script output failed for {Path}.", ex.Path);
            TigerConsole.MarkupErrorLine($"[Error]{CliMarkupParser.Escape(ex.Message)}[/]");
            return TigerSqlCmdExitCode.OutputFailed;
        }
        catch (SqlException ex)
        {
            // A SqlException escaping the engine means the connection could not be
            // opened; batch-level SQL failures never propagate this far. The provider's
            // message and error number are what tell a rejected login, an unreachable
            // server, a timeout, an inaccessible database, and an untrusted certificate
            // apart, so both are kept; neither carries the connection string.
            logger?.LogError(
                ex,
                "Could not open the SQL Server connection for saved connection {Connection}: "
                    + "SQL error {Number}, state {State}, class {Class}.",
                connectionName,
                ex.Number,
                ex.State,
                ex.Class);
            TigerConsole.MarkupErrorLine(
                $"[Error]{CliMarkupParser.Escape(DescribeConnectionFailure(connectionName, ex))}[/]");
            return TigerSqlCmdExitCode.ConnectionFailed;
        }
    }

    internal static string DescribeConnectionFailure(string connectionName, SqlException exception) =>
        $"Could not connect to SQL Server using saved connection '{connectionName}': "
        + $"{exception.Message} (SQL error {exception.Number}, state {exception.State}, "
        + $"class {exception.Class})";
}
