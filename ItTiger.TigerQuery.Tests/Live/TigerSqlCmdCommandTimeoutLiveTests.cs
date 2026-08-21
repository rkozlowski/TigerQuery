using ItTiger.TigerQuery.Tests.Cli;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Proves <c>run --command-timeout</c> against a real SQL Server and a real process, the
/// only place the value that reaches
/// <see cref="Microsoft.Data.SqlClient.SqlCommand.CommandTimeout"/> is observable.
/// </summary>
/// <remarks>
/// <para>
/// The reported problem was a single long batch being killed at the provider's 30-second
/// default, forcing callers to split one script into several <c>tiger-sqlcmd</c>
/// invocations. <see cref="ABatchLongerThanTheOldDefaultNeedsTheOptionAndThenSucceeds"/> is
/// the direct regression test for that, and is deliberately the only slow test here: every
/// one of its runs has to out-wait the real 30-second default.
/// </para>
/// <para>
/// Every other test uses a short explicit timeout against a short batch, so the file costs
/// roughly that one long test plus a few seconds. Each run is non-interactive, which is how
/// a script or an agent invokes it.
/// </para>
/// </remarks>
[Collection(LiveTestCollection.Name)]
public sealed class TigerSqlCmdCommandTimeoutLiveTests : IDisposable
{
    /// <summary>Comfortably longer than the provider's 30-second default.</summary>
    private const string LongerThanTheDefault =
        "WAITFOR DELAY '00:00:34'; SELECT 'finished' AS Marker;";

    private readonly LiveTigerSqlCmdHost host = LiveTigerSqlCmdHost.Create();
    private readonly string directory;

    public TigerSqlCmdCommandTimeoutLiveTests() =>
        directory = Path.GetDirectoryName(host.StorePath)!;

    public void Dispose() => host.Dispose();

    // ── The regression: one long batch, one invocation ───────────────

    /// <summary>
    /// A batch that outlasts the 30-second default fails without the option and succeeds
    /// with it, in one invocation — the script never has to be split into phases.
    /// </summary>
    [Fact]
    public async Task ABatchLongerThanTheOldDefaultNeedsTheOptionAndThenSucceeds()
    {
        // Both runs deliberately outlast the harness's ordinary limit, so they get their own.
        var patience = TimeSpan.FromMinutes(2);

        var timedOut = await RunAsync(patience, LongerThanTheDefault, "SqlCmd");
        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, timedOut.ExitCode);
        AssertTimeoutDiagnostics(timedOut);
        Assert.DoesNotContain("finished", timedOut.StdOut, StringComparison.Ordinal);

        // Unlimited: the SqlClient meaning of CommandTimeout = 0.
        var unlimited = await RunAsync(
            patience, LongerThanTheDefault, "SqlCmd", "--command-timeout", "0");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, unlimited.ExitCode);
        Assert.Contains("finished", unlimited.StdOut, StringComparison.Ordinal);

        // And a generous explicit timeout does the same thing, so unlimited is a choice
        // rather than the only way through.
        var generous = await RunAsync(
            patience, LongerThanTheDefault, "SqlCmd", "--command-timeout", "300");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, generous.ExitCode);
        Assert.Contains("finished", generous.StdOut, StringComparison.Ordinal);
    }

    // ── Reaching the SQL command, in both modes and on both paths ────

    /// <summary>
    /// A timeout smaller than the batch stops it. Only a value that reached the provider's
    /// command can do that: the batch is far shorter than the 30-second default.
    /// </summary>
    [Theory]
    [InlineData("SqlCmd")]
    [InlineData("SqlCmdEx")]
    public async Task APositiveTimeoutSmallerThanTheBatchStopsIt(string mode)
    {
        var result = await RunAsync(
            "WAITFOR DELAY '00:00:12'; SELECT 'finished' AS Marker;",
            mode,
            "--command-timeout", "2");

        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, result.ExitCode);
        AssertTimeoutDiagnostics(result);
        Assert.DoesNotContain("finished", result.StdOut, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlCmd")]
    [InlineData("SqlCmdEx")]
    public async Task APositiveTimeoutLargerThanTheBatchLetsItFinish(string mode)
    {
        var result = await RunAsync(
            "WAITFOR DELAY '00:00:03'; SELECT 'finished' AS Marker;",
            mode,
            "--command-timeout", "60");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("finished", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>File execution is the same execution path, and gets the same timeout.</summary>
    [Theory]
    [InlineData("2", (int)TigerSqlCmdExitCode.BatchFailed)]
    [InlineData("60", (int)TigerSqlCmdExitCode.Ok)]
    public async Task TheTimeoutAppliesToSqlFileExecutionToo(string seconds, int expectedExitCode)
    {
        var path = Path.Combine(directory, $"timeout-{Guid.NewGuid():N}.sql");
        await File.WriteAllTextAsync(
            path,
            "WAITFOR DELAY '00:00:08';\r\nGO\r\nSELECT 'finished' AS Marker;\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        var result = await host.RunAsync(
            "run",
            "--connection", LiveTigerSqlCmdHost.BootstrapName,
            "--file", path,
            "--command-timeout", seconds);

        Assert.Equal(expectedExitCode, result.ExitCode);
    }

    /// <summary>
    /// The value bounds each batch, not the run. Two eight-second batches under a
    /// twelve-second timeout total more than the timeout and must still both succeed.
    /// </summary>
    [Fact]
    public async Task TheTimeoutIsPerBatchRatherThanPerRun()
    {
        var result = await RunAsync(
            "WAITFOR DELAY '00:00:08';\r\nGO\r\nWAITFOR DELAY '00:00:08';\r\n"
                + "GO\r\nSELECT 'finished' AS Marker;\r\nGO\r\n",
            "SqlCmd",
            "--command-timeout", "12");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("finished", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// The command timeout is not the connection-open timeout: a two-second command timeout
    /// does not stop a run whose batches are short, however long opening the connection took.
    /// </summary>
    [Fact]
    public async Task ASmallCommandTimeoutDoesNotAffectOpeningTheConnection()
    {
        var result = await RunAsync(
            "SELECT 'finished' AS Marker;", "SqlCmd", "--command-timeout", "2");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("finished", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// Omitting the option leaves the run exactly as it was before the option existed.
    /// </summary>
    [Theory]
    [InlineData("SqlCmd")]
    [InlineData("SqlCmdEx")]
    public async Task OmittingTheOptionKeepsTheExistingDefaultBehavior(string mode)
    {
        var result = await RunAsync(
            "WAITFOR DELAY '00:00:03'; SELECT 'finished' AS Marker;", mode);

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("finished", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// A timed-out batch is an ordinary batch failure, so the corrected 0.8.6 semantics
    /// still hold: a later successful batch does not clear it.
    /// </summary>
    [Fact]
    public async Task ATimedOutBatchStillFailsTheRunEvenWhenALaterBatchSucceeds()
    {
        var result = await RunAsync(
            "WAITFOR DELAY '00:00:12';\r\nGO\r\nSELECT 'later batch ran' AS Marker;\r\nGO\r\n",
            "SqlCmd",
            "--command-timeout", "2");

        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, result.ExitCode);
        Assert.Contains("later batch ran", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// The negative-value rejection reaches the shipped process, not just the settings type.
    /// </summary>
    [Fact]
    public async Task ANegativeTimeoutIsRejectedByTheProcessWithoutRunningSql()
    {
        var result = await RunAsync(
            "SELECT 'finished' AS Marker;", "SqlCmd", "--command-timeout", "-1");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains("--command-timeout", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("finished", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// The failure has to be legible: the exit code carries the contract, and the console
    /// still names the timeout so an operator can tell it from an ordinary SQL error.
    /// </summary>
    private static void AssertTimeoutDiagnostics(TigerSqlCmdProcessResult result)
    {
        var output = result.StdOut + result.StdErr;
        Assert.True(
            output.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || output.Contains("timed out", StringComparison.OrdinalIgnoreCase),
            $"Expected timeout diagnostics on the console. Got:{Environment.NewLine}{output}");
    }

    private Task<TigerSqlCmdProcessResult> RunAsync(
        string query,
        string mode,
        params string[] extraArguments) =>
        RunAsync(TigerSqlCmdProcessRunner.DefaultTimeout, query, mode, extraArguments);

    private Task<TigerSqlCmdProcessResult> RunAsync(
        TimeSpan timeout,
        string query,
        string mode,
        params string[] extraArguments) =>
        host.RunAsync(
            timeout,
            [
                "run",
                "--connection", LiveTigerSqlCmdHost.BootstrapName,
                "--query", query,
                "--mode", mode,
                .. extraArguments
            ]);
}
