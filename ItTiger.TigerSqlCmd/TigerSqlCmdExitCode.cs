using ItTiger.Core;
using ItTiger.TigerQuery.Engine;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// The tiger-sqlcmd process exit-code contract. Values 0–8 mirror the engine's
/// <see cref="ExecutionResultCode"/> one-to-one so script-visible codes are unchanged;
/// Code 20 is reserved for framework-produced usage failures mapped through the app's
/// TigerCli exit-code policy in <see cref="TigerSqlCmdApp"/>, and code 21 for an
/// <c>exec</c> child that could not be started at all. <c>exec</c> otherwise returns the
/// child's own exit code unchanged, so a child may well return a value this enum also
/// defines; the caller knows which command it ran.
/// Connection-command outcomes are mapped onto host-owned aliases so their historical
/// script-visible values remain stable without CliCore owning concrete process codes.
/// </summary>
[TigerText("tiger-sqlcmd exit codes")]
public enum TigerSqlCmdExitCode
{
    [TigerText("OK", Description = "Execution completed successfully.")]
    Ok = 0,

    [TigerText("Batch failed", Description = "At least one SQL batch failed, whether or not the run continued past it.")]
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

    [TigerText("Output failed", Description = "Script output could not be routed, serialized, or written.")]
    OutputFailed = 8,

    [TigerText("Invalid arguments", Description = "The command line was invalid or incomplete.")]
    InvalidArguments = 20,

    [TigerText("Child process start failed", Description = "The 'exec' child executable could not be started.")]
    ChildProcessStartFailed = 21,

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
        ExecutionResultCode.OutputFailed => TigerSqlCmdExitCode.OutputFailed,
        _ => TigerSqlCmdExitCode.UnhandledException,
    };
}
