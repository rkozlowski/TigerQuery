using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;

namespace ItTiger.TigerQuery.Tests.Engine;

/// <summary>
/// Locks the rule that produced the <c>tiger-sqlcmd run</c> exit-0 regression: the
/// terminal <see cref="ExecutionResult.ResultCode"/> reports whether any batch attempt
/// failed, independently of whether the effective <c>:ON ERROR</c> policy let the run
/// carry on. Reaching the end of a script is not success, and neither is a later batch
/// succeeding after an earlier one failed.
/// </summary>
/// <remarks>
/// The default policy is continue-on-error, which is why these tests deliberately use
/// scripts with no <c>:ON ERROR</c> directive at all — that is the shape
/// <c>tiger-sqlcmd run</c> executes unless a script asks for something else.
/// </remarks>
public sealed class BatchFailureResultCodeTests
{
    /// <summary>A severity SQL Server delivers as a message rather than an exception.</summary>
    private const byte UserErrorSeverity = 16;

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task EveryBatchSucceedingUnderTheDefaultPolicyReportsSuccess(
        TigerQueryExecutionMode executionMode)
    {
        using var host = new OutputTestHost();

        var result = await host.RunAsync(
            "SELECT 1;\r\nGO\r\nSELECT 2;\r\nGO\r\nSELECT 3;\r\nGO\r\n",
            executionMode);

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(0, result.FailedBatches);
        Assert.Equal(3, result.ExecutedBatches);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task AMessageOnlyServerErrorFailsTheRunEvenThoughLaterBatchesRun(
        TigerQueryExecutionMode executionMode)
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            if (emission.Has("FAIL"))
                emission.ServerMessage("Deliberate severity 16.", UserErrorSeverity);
        };

        var result = await host.RunAsync(
            "SELECT 1;\r\nGO\r\nFAIL\r\nGO\r\nSELECT 3;\r\nGO\r\n",
            executionMode);

        // The continue policy is preserved: all three batches were submitted.
        Assert.Equal(3, host.ExecutionCount);
        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(2, result.ExecutedBatches);
    }

    [Fact]
    public async Task ALaterSuccessfulBatchNeverRestoresSuccess()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            if (emission.BatchNumber == 1)
                emission.ServerMessage("First batch failed.", UserErrorSeverity);
        };

        var result = await host.RunAsync(
            "FAIL\r\nGO\r\nSELECT 2;\r\nGO\r\nSELECT 3;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(2, result.ExecutedBatches);

        // The last attempt succeeded, so no exception survives — the count and the code
        // are what carry the failure to the caller.
        Assert.Null(result.Exception);
        Assert.Equal("end:3:1:True", host.Events.Last(item => item.StartsWith("end:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// An explicit <c>:ON ERROR IGNORE</c> keeps its documented meaning — later batches
    /// run — but it is not a request to report success.
    /// </summary>
    [Fact]
    public async Task AnExplicitIgnoreDirectiveContinuesWithoutClearingTheFailure()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            if (emission.Has("FAIL"))
                emission.ServerMessage("Ignored but still failed.", UserErrorSeverity);
        };

        var result = await host.RunAsync(
            ":ON ERROR IGNORE\r\nFAIL\r\nGO\r\nSELECT 2;\r\nGO\r\n");

        Assert.Equal(2, host.ExecutionCount);
        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
    }

    /// <summary>
    /// The parser mode decides which directives a script may use; it never changes how a
    /// failed batch is accounted for.
    /// </summary>
    [Theory]
    [InlineData(SqlCmdMode.SqlCmd)]
    [InlineData(SqlCmdMode.SqlCmdEx)]
    [InlineData(SqlCmdMode.Normal)]
    public async Task EveryParserModeReportsTheSameFailedResult(SqlCmdMode mode)
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            if (emission.Has("FAIL"))
                emission.ServerMessage("Deliberate severity 16.", UserErrorSeverity);
        };

        var result = await host.RunAsync(
            "FAIL\r\nGO\r\nSELECT 2;\r\nGO\r\n",
            mode: mode);

        Assert.Equal(2, host.ExecutionCount);
        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(1, result.ExecutedBatches);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(16, false)]
    public async Task TheSeverityThresholdIsUnchangedByTheNewResultRule(
        byte severity,
        bool expectedSuccess)
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ServerMessage("Boundary.", severity);

        var result = await host.RunAsync("MAYBE\r\nGO\r\n");

        Assert.Equal(
            expectedSuccess ? ExecutionResultCode.Success : ExecutionResultCode.BatchFailed,
            result.ResultCode);
        Assert.Equal(expectedSuccess ? 0 : 1, result.FailedBatches);
    }

    /// <summary>
    /// Routing the result sets to a file that is written perfectly is not evidence that
    /// the SQL succeeded, and it must not overwrite the failed outcome.
    /// </summary>
    [Fact]
    public async Task SuccessfulResultRoutingDoesNotMaskASqlFailure()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["Id"], [emission.BatchNumber]);
            if (emission.BatchNumber == 1)
                emission.ServerMessage("Deliberate severity 16.", UserErrorSeverity);
        };

        var result = await host.RunAsync(
            ":Out report.csv\r\nFAIL\r\nGO\r\nSELECT 2;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal("Id\r\n1\r\n2\r\n", host.ReadText("report.csv"));
    }

    /// <summary>
    /// An output failure is its own terminal classification and stops the run whatever the
    /// error policy is, so it stays nonzero and is not downgraded by the new rule.
    /// </summary>
    [Fact]
    public async Task AnOutputFailureKeepsItsOwnNonZeroClassification()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            emission.BatchNumber == 1 ? ["Id"] : ["Other"],
            ["value"]);

        var result = await host.RunAsync(
            ":Out report.csv\r\nSELECT 1;\r\nGO\r\nSELECT 2;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        Assert.IsType<OutputRoutingException>(result.Exception);
    }

    /// <summary>
    /// When a batch has already failed, a subsequent routing failure is secondary: the
    /// SQL failure stays the reported cause and the run is still nonzero either way.
    /// </summary>
    [Fact]
    public async Task ASqlFailureFollowedByARoutingFailureStaysNonZero()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            if (emission.BatchNumber == 1)
                emission.ServerMessage("Deliberate severity 16.", UserErrorSeverity);
            emission.ResultSet(
                emission.BatchNumber == 1 ? ["Id"] : ["Other"],
                ["value"]);
        };

        var result = await host.RunAsync(
            ":Out report.csv\r\nFAIL\r\nGO\r\nSELECT 2;\r\nGO\r\n");

        Assert.NotEqual(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
    }
}
