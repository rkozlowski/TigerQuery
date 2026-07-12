using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
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
    public static async Task<TigerSqlCmdExitCode> RunAsync(
        ILogger? logger,
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
        catch (SqlException ex)
        {
            // A SqlException escaping the engine means the connection could not be
            // opened; batch-level SQL failures never propagate this far.
            logger?.LogError(ex, "Failed to open the SQL Server connection.");
            TigerConsole.MarkupErrorLine($"[Error]{CliMarkupParser.Escape(ex.Message)}[/]");
            return TigerSqlCmdExitCode.ConnectionFailed;
        }
    }
}
