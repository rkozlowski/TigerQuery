using ItTiger.Core;
using ItTiger.TigerQuery.Engine;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// The tiger-sqlcmd process exit-code contract. Values 0–7 mirror the engine's
/// <see cref="ExecutionResultCode"/> one-to-one so script-visible codes are unchanged;
/// Code 20 is reserved for framework-produced usage failures mapped through the app's
/// TigerCli exit-code policy in <see cref="TigerSqlCmdApp"/>.
/// Connection-command outcomes are mapped onto host-owned aliases so their historical
/// script-visible values remain stable without CliCore owning concrete process codes.
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

    [TigerText("Invalid connection arguments", Description = "A connection command rejected well-formed settings that failed domain validation.")]
    ConnectionInvalidArguments = 2,

    [TigerText("Cancelled", Description = "Execution was cancelled by the user.")]
    Cancelled = 3,

    [TigerText("Connection failed", Description = "The SQL Server connection could not be resolved or opened.")]
    ConnectionFailed = 4,

    [TigerText("Connection not found", Description = "The requested saved SQL Server connection does not exist.")]
    ConnectionNotFound = 4,

    [TigerText("Parse error", Description = "The script could not be parsed.")]
    ParseError = 5,

    [TigerText("Connection already exists", Description = "A saved SQL Server connection with the requested name already exists.")]
    ConnectionAlreadyExists = 5,

    [TigerText("Unhandled exception", Description = "An unexpected error ended execution.")]
    UnhandledException = 6,

    [TigerText("Fatal exception", Description = "A fatal engine exception ended execution.")]
    FatalException = 7,

    [TigerText("Invalid arguments", Description = "The command line was invalid or incomplete.")]
    InvalidArguments = 20,

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
