using ItTiger.Core;
using ItTiger.TigerQuery.Engine;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// The tiger-sqlcmd process exit-code contract. Values 0–7 mirror the engine's
/// <see cref="ExecutionResultCode"/> one-to-one so script-visible codes are unchanged;
/// 20+ are reserved for framework-produced outcomes (usage and validation failures)
/// mapped through the app's TigerCli exit-code policy in <see cref="TigerSqlCmdApp"/>.
/// The <c>connections</c> command group keeps its own
/// <see cref="ItTiger.TigerQuery.CliCore.SqlServerConnectionExitCode"/> contract.
/// </summary>
[TigerText("tiger-sqlcmd exit codes")]
public enum TigerSqlCmdExitCode
{
    [TigerText("OK", Description = "Execution completed successfully.")]
    Ok = 0,

    [TigerText("Batch failed", Description = "A batch failed and execution stopped.")]
    BatchFailed = 1,

    [TigerText("Fatal SQL error", Description = "A fatal SQL error ended execution.")]
    FatalSqlError = 2,

    [TigerText("Cancelled", Description = "Execution was cancelled by the user.")]
    Cancelled = 3,

    [TigerText("Connection failed", Description = "The SQL Server connection could not be resolved or opened.")]
    ConnectionFailed = 4,

    [TigerText("Parse error", Description = "The script could not be parsed.")]
    ParseError = 5,

    [TigerText("Unhandled exception", Description = "An unexpected error ended execution.")]
    UnhandledException = 6,

    [TigerText("Fatal exception", Description = "A fatal engine exception ended execution.")]
    FatalException = 7,

    [TigerText("Invalid arguments", Description = "The command line was invalid or incomplete.")]
    InvalidArguments = 20,

    [TigerText("Validation error", Description = "The command-line input failed validation.")]
    ValidationError = 21,
}

internal static class TigerSqlCmdExitCodeMapper
{
    /// <summary>
    /// Maps an engine <see cref="ExecutionResultCode"/> onto the process exit-code
    /// contract. The values are numerically identical today; the explicit switch keeps
    /// the mapping intentional (and test-locked) if either enum ever grows.
    /// </summary>
    public static TigerSqlCmdExitCode ToExitCode(this ExecutionResultCode code) => code switch
    {
        ExecutionResultCode.Success => TigerSqlCmdExitCode.Ok,
        ExecutionResultCode.BatchFailed => TigerSqlCmdExitCode.BatchFailed,
        ExecutionResultCode.Fatal => TigerSqlCmdExitCode.FatalSqlError,
        ExecutionResultCode.UserCancelled => TigerSqlCmdExitCode.Cancelled,
        ExecutionResultCode.ConnectionFailed => TigerSqlCmdExitCode.ConnectionFailed,
        ExecutionResultCode.ParseError => TigerSqlCmdExitCode.ParseError,
        ExecutionResultCode.UnhandledException => TigerSqlCmdExitCode.UnhandledException,
        ExecutionResultCode.FatalException => TigerSqlCmdExitCode.FatalException,
        _ => TigerSqlCmdExitCode.UnhandledException,
    };
}
