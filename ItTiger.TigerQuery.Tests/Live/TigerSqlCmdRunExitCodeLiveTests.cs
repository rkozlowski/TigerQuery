using ItTiger.TigerQuery.Tests.Cli;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Proves the <c>tiger-sqlcmd run</c> exit-code contract against a real SQL Server and a
/// real process, which is the only place the provider's actual error transport and the
/// process exit code can both be observed.
/// </summary>
/// <remarks>
/// The regression these lock down is a run that printed SQL Server's error text and still
/// exited 0. Every assertion here reads the process exit code; none of them decides
/// success or failure by matching console text.
/// </remarks>
[Collection(LiveTestCollection.Name)]
public sealed class TigerSqlCmdRunExitCodeLiveTests : IDisposable
{
    private readonly LiveTigerSqlCmdHost host = LiveTigerSqlCmdHost.Create();

    public void Dispose() => host.Dispose();

    [Theory]
    [InlineData("SqlCmd")]
    [InlineData("SqlCmdEx")]
    public async Task ASuccessfulBatchReturnsOk(string mode)
    {
        var result = await RunAsync("SELECT 1 AS Ok;", mode);

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
    }

    /// <summary>
    /// The original report: <c>DROP DATABASE</c> failed, SQL Server's message was printed,
    /// and the process still exited 0.
    /// </summary>
    [Theory]
    [InlineData("SqlCmd")]
    [InlineData("SqlCmdEx")]
    public async Task AFailedDropDatabaseReturnsBatchFailedAndKeepsTheErrorText(string mode)
    {
        var absent = "_TQ_E2E_never_created_" + Guid.NewGuid().ToString("N");

        var result = await RunAsync($"DROP DATABASE [{absent}];", mode);

        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, result.ExitCode);
        Assert.Contains(absent, result.StdOut + result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Severity 11-16 errors are delivered by the provider as informational messages
    /// rather than as a thrown exception; the run must still fail.
    /// </summary>
    [Theory]
    [InlineData("RAISERROR('Deliberate severity 16.', 16, 7);")]
    [InlineData("THROW 51000, 'Deliberate throw.', 3;")]
    [InlineData("SELECT * FROM dbo.NoSuchTableAnywhere;")]
    public async Task AMessageOnlyServerErrorReturnsBatchFailed(string sql)
    {
        var result = await RunAsync(sql, "SqlCmd");

        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, result.ExitCode);
    }

    /// <summary>
    /// The default policy keeps running later batches. That is preserved — and it is still
    /// not success.
    /// </summary>
    [Theory]
    [InlineData("SqlCmd")]
    [InlineData("SqlCmdEx")]
    public async Task AnEarlyFailureFollowedByASuccessfulBatchStillReturnsNonZero(string mode)
    {
        var result = await RunAsync(
            """
            RAISERROR('Deliberate severity 16.', 16, 7);
            GO
            SELECT 'later batch ran' AS Marker;
            GO
            """,
            mode);

        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, result.ExitCode);
        Assert.Contains("later batch ran", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExplicitIgnoreDirectiveContinuesAndStillReturnsNonZero()
    {
        var result = await RunAsync(
            """
            :ON ERROR IGNORE
            RAISERROR('Deliberate severity 16.', 16, 7);
            GO
            SELECT 'later batch ran' AS Marker;
            GO
            """,
            "SqlCmd");

        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, result.ExitCode);
        Assert.Contains("later batch ran", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExplicitExitDirectiveStopsAndReturnsNonZero()
    {
        var result = await RunAsync(
            """
            :ON ERROR EXIT
            RAISERROR('Deliberate severity 16.', 16, 7);
            GO
            SELECT 'later batch ran' AS Marker;
            GO
            """,
            "SqlCmd");

        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, result.ExitCode);
        Assert.DoesNotContain("later batch ran", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// A perfectly written result file is not evidence that the SQL succeeded.
    /// </summary>
    [Fact]
    public async Task ResultOutputRoutingDoesNotMaskASqlFailure()
    {
        var outputPath = Path.Combine(Path.GetDirectoryName(host.StorePath)!, "routed.csv");

        var result = await RunAsync(
            """
            SELECT 'routed' AS Marker;
            GO
            RAISERROR('Deliberate severity 16.', 16, 7);
            GO
            """,
            "SqlCmd",
            "--output", outputPath);

        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, result.ExitCode);
        Assert.True(File.Exists(outputPath), "The routed result file should still have been written.");
        Assert.Contains("routed", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    /// <summary>
    /// The console text stays where it always was. It is diagnostics, not the contract.
    /// </summary>
    [Fact]
    public async Task DiagnosticsRemainVisibleWhileTheExitCodeIsNonZero()
    {
        var result = await RunAsync("RAISERROR('Deliberate severity 16.', 16, 7);", "SqlCmd");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "Deliberate severity 16.",
            result.StdOut + result.StdErr,
            StringComparison.Ordinal);
    }

    private Task<TigerSqlCmdProcessResult> RunAsync(
        string query,
        string mode,
        params string[] extraArguments) =>
        host.RunAsync(
        [
            "run",
            "--connection", LiveTigerSqlCmdHost.BootstrapName,
            "--query", query,
            "--mode", mode,
            .. extraArguments
        ]);
}
