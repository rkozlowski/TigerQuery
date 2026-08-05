using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
using System.ComponentModel;
using System.Diagnostics;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// Starts the <c>exec</c> child process directly and reports its exit code.
/// </summary>
/// <remarks>
/// <para>
/// No shell is involved: <see cref="ProcessStartInfo.UseShellExecute"/> stays false and the
/// arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, so the operating
/// system receives exactly the tokens TigerSqlCmd built. Standard input, output, and error
/// are inherited rather than redirected, which keeps the child's console behavior intact
/// and removes any possibility of a pipe-buffer deadlock. The working directory is left
/// unset so the child inherits the caller's.
/// </para>
/// <para>
/// The resolved connection string is written only into the child's argument list and its
/// private environment block. It is never logged, echoed, or included in an error message.
/// </para>
/// </remarks>
internal static class TigerSqlCmdChildProcess
{
    public static async Task<int> RunAsync(TigerSqlCmdExecPlan plan, string connectionString)
    {
        var invocation = plan.Materialize(connectionString);

        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.Executable,
            // Start the executable directly. Shell execution would re-parse the command line
            // and could resolve the "executable" to a document handler instead.
            UseShellExecute = false
        };

        foreach (var argument in invocation.Arguments)
            startInfo.ArgumentList.Add(argument);

        if (invocation.EnvironmentVariableName is not null)
        {
            // ProcessStartInfo.Environment starts as a copy of this process's environment, so
            // the child inherits everything else and the parent process is left untouched.
            startInfo.Environment[invocation.EnvironmentVariableName] = connectionString;
        }

        using var cancellation = new ChildProcessCancellationScope();

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            ReportStartFailure(plan, ex.Message);
            return (int)TigerSqlCmdExitCode.ChildProcessStartFailed;
        }

        if (process is null)
        {
            ReportStartFailure(plan, "The operating system started no new process.");
            return (int)TigerSqlCmdExitCode.ChildProcessStartFailed;
        }

        using (process)
        {
            // Waited without a cancellation token on purpose. The console already delivered
            // Ctrl+C to the child's process group; abandoning the wait would report an exit
            // code for a process that is still running. The scope keeps tiger-sqlcmd alive
            // long enough to observe the real one.
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
    }

    private static void ReportStartFailure(TigerSqlCmdExecPlan plan, string reason)
    {
        TigerConsole.MarkupErrorLine(
            $"[Error]Could not start the child executable '{CliMarkupParser.Escape(plan.Executable)}': "
            + $"{CliMarkupParser.Escape(reason)}[/]");
        TigerConsole.MarkupErrorLine(
            $"[Muted]Child command line:[/] {CliMarkupParser.Escape(plan.DescribeRedacted())}");
    }
}

/// <summary>
/// Keeps tiger-sqlcmd alive across the first Ctrl+C so it can report the child's real exit
/// code, and steps aside for the second.
/// </summary>
/// <remarks>
/// On a console, Ctrl+C reaches every process in the group, so the child is already being
/// asked to stop and TigerSqlCmd never kills it. Suppressing the first press stops the
/// runtime from tearing this process down while the child is still shutting down.
/// Suppressing every press would let a child that ignores Ctrl+C hang the caller, so the
/// second press is passed through to the default handler.
/// </remarks>
internal sealed class ChildProcessCancellationScope : IDisposable
{
    private readonly ConsoleCancelEventHandler _handler;
    private int _requests;

    public ChildProcessCancellationScope()
    {
        _handler = (_, e) => e.Cancel = ShouldSuppress();
        Console.CancelKeyPress += _handler;
    }

    /// <summary>
    /// Records one Ctrl+C and reports whether it should be suppressed. Internal seam for
    /// tests, which cannot raise <see cref="Console.CancelKeyPress"/> directly.
    /// </summary>
    internal bool ShouldSuppress() => Interlocked.Increment(ref _requests) == 1;

    public void Dispose() => Console.CancelKeyPress -= _handler;
}
