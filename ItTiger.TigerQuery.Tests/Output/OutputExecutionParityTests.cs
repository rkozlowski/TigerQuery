using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;

namespace ItTiger.TigerQuery.Tests.Output;

/// <summary>
/// Runs the same scripted route changes through streaming and prepared execution and
/// compares files, bytes, events, and failure classification.
/// </summary>
public sealed class OutputExecutionParityTests
{
    private const string RoutedScript =
        ":Out first.csv\r\n"
        + ":Error errors.log\r\n"
        + "SELECT 1;\r\nGO\r\n"
        + ":Out second.csv\r\n"
        + "SELECT 2;\r\nGO 2\r\n"
        + ":Out first.csv\r\n"
        + "SELECT 3;\r\nGO\r\n";

    private static void EmitRows(OutputTestHost.Emission emission)
    {
        emission.ResultSet(
            ["Id", "Source"],
            [emission.BatchNumber, $"b{emission.BatchNumber}e{emission.ExecutionIndex}"]);
        emission.ServerMessage("printed", 0);
        emission.ServerMessage("diagnosed", 16);
    }

    [Fact]
    public async Task BothModesProduceTheSameFilesAndBytes()
    {
        using var streaming = new OutputTestHost();
        using var prepared = new OutputTestHost();
        streaming.Emit = EmitRows;
        prepared.Emit = EmitRows;

        var routing = new OutputRoutingOptions
        {
            OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages
        };

        var streamingResult = await streaming.RunAsync(
            RoutedScript,
            TigerQueryExecutionMode.Streaming,
            routing);
        var preparedResult = await prepared.RunAsync(
            RoutedScript,
            TigerQueryExecutionMode.Prepared,
            OutputTestHost.CloneWithBaseDirectory(routing, prepared.Directory));

        Assert.Equal(streamingResult.ResultCode, preparedResult.ResultCode);
        Assert.Equal(streamingResult.ExecutedBatches, preparedResult.ExecutedBatches);
        Assert.Equal(streamingResult.FailedBatches, preparedResult.FailedBatches);

        var files = streaming.ProducedFiles();
        Assert.Equal(files, prepared.ProducedFiles());
        Assert.NotEmpty(files);

        foreach (var name in files)
        {
            Assert.Equal(streaming.ReadBytes(name), prepared.ReadBytes(name));
        }
    }

    [Fact]
    public async Task BothModesProduceTheSameEventOrderApartFromPlanReadiness()
    {
        using var streaming = new OutputTestHost();
        using var prepared = new OutputTestHost();
        streaming.Emit = EmitRows;
        prepared.Emit = EmitRows;

        await streaming.RunAsync(RoutedScript, TigerQueryExecutionMode.Streaming);
        await prepared.RunAsync(RoutedScript, TigerQueryExecutionMode.Prepared);

        Assert.Equal("plan:3:4", prepared.Events[0]);
        Assert.Equal(streaming.Events, prepared.Events.Skip(1));
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task RouteTransitionsLandOnTheSameBatchesInBothModes(TigerQueryExecutionMode mode)
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            ["Id"],
            [$"b{emission.BatchNumber}e{emission.ExecutionIndex}"]);

        await host.RunAsync(RoutedScript, mode);

        Assert.Equal("Id\r\nb1e1\r\nb3e1\r\n", host.ReadText("first.csv"));
        Assert.Equal("Id\r\nb2e1\r\nb2e2\r\n", host.ReadText("second.csv"));
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task AnOutputFailureFailsTheBatchAndStopsTheRun(TigerQueryExecutionMode mode)
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            emission.BatchNumber == 1 ? ["Id"] : ["Other"],
            [emission.BatchNumber]);

        var result = await host.RunAsync(
            ":Out report.csv\r\n"
            + "SELECT 1;\r\nGO\r\n"
            + "SELECT 2;\r\nGO\r\n"
            + "SELECT 3;\r\nGO\r\n",
            mode);

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        var failure = Assert.IsType<OutputRoutingException>(result.Exception);
        Assert.Equal(host.PathOf("report.csv"), failure.Path);

        Assert.Equal(1, result.ExecutedBatches);
        Assert.Equal(1, result.FailedBatches);
        Assert.Contains("end:2:1:False", host.Events);
        Assert.DoesNotContain("start:3:1", host.Events);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task OnErrorIgnoreDoesNotSurviveAnOutputFailure(TigerQueryExecutionMode mode)
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            emission.BatchNumber == 1 ? ["Id"] : ["Other"],
            [emission.BatchNumber]);

        var result = await host.RunAsync(
            ":ON ERROR IGNORE\r\n"
            + ":Out report.csv\r\n"
            + "SELECT 1;\r\nGO\r\n"
            + "SELECT 2;\r\nGO\r\n"
            + "SELECT 3;\r\nGO\r\n",
            mode);

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        Assert.DoesNotContain("start:3:1", host.Events);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ContinueOnErrorForUnhandledExceptionsDoesNotSurviveAnOutputFailure(
        TigerQueryExecutionMode mode)
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            emission.BatchNumber == 1 ? ["Id"] : ["Other"],
            [emission.BatchNumber]);

        var options = host.BuildOptions(
            mode,
            configure: engineOptions => { });
        var relaxed = new TigerQueryEngineOptions
        {
            Mode = options.Mode,
            ExecutionMode = mode,
            ContinueOnError = true,
            ContinueOnErrorForUnhandledExceptions = true,
            OutputRouting = options.OutputRouting,
            OnResultSet = options.OnResultSet,
            OnMessage = options.OnMessage,
            OnBatchStart = options.OnBatchStart,
            OnBatchEnd = options.OnBatchEnd,
            OnExecutionPlanReady = options.OnExecutionPlanReady
        };

        var result = await host.RunAsync(
            ":Out report.csv\r\n"
            + "SELECT 1;\r\nGO\r\n"
            + "SELECT 2;\r\nGO\r\n"
            + "SELECT 3;\r\nGO\r\n",
            relaxed);

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        Assert.DoesNotContain("start:3:1", host.Events);
    }

    [Fact]
    public async Task StreamingLeavesEarlierFilesBehindALateParserError()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [emission.BatchNumber]);

        var engine = host.CreateEngine(host.BuildOptions());

        await Assert.ThrowsAsync<TigerQueryException>(
            () => engine.RunFromStringAsync(
                ":Out report.csv\r\nSELECT 1;\r\nGO\r\n:setvar\r\n",
                TestContext.Current.CancellationToken));

        Assert.Equal(["report.csv"], host.ProducedFiles());
        Assert.Equal("Id\r\n1\r\n", host.ReadText("report.csv"));
    }

    [Fact]
    public async Task PreparedCreatesNoFileWhenParsingFailsLate()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [emission.BatchNumber]);

        var engine = host.CreateEngine(host.BuildOptions(TigerQueryExecutionMode.Prepared));

        await Assert.ThrowsAsync<TigerQueryException>(
            () => engine.RunFromStringAsync(
                ":Out report.csv\r\nSELECT 1;\r\nGO\r\n:setvar\r\n",
                TestContext.Current.CancellationToken));

        Assert.Empty(host.ProducedFiles());
        Assert.Equal(0, host.OpenCount);
        Assert.Equal(0, host.ExecutionCount);
    }

    [Fact]
    public async Task PreparedDetectsAChannelCollisionBeforeOpeningTheConnection()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [emission.BatchNumber]);

        var engine = host.CreateEngine(host.BuildOptions(TigerQueryExecutionMode.Prepared));

        var failure = await Assert.ThrowsAsync<OutputRoutingException>(
            () => engine.RunFromStringAsync(
                ":Out shared.log\r\nSELECT 1;\r\nGO\r\n:Error shared.log\r\nSELECT 2;\r\nGO\r\n",
                TestContext.Current.CancellationToken));

        Assert.Equal(host.PathOf("shared.log"), failure.Path);
        Assert.Empty(host.ProducedFiles());
        Assert.Equal(0, host.OpenCount);
        Assert.Equal(0, host.ExecutionCount);
        Assert.DoesNotContain(host.Events, entry => entry.StartsWith("plan:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StreamingReportsTheSameCollisionAsAnOutputFailureAfterEarlierWork()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [emission.BatchNumber]);

        var result = await host.RunAsync(
            ":Out shared.log\r\nSELECT 1;\r\nGO\r\n:Error shared.log\r\nSELECT 2;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        var failure = Assert.IsType<OutputRoutingException>(result.Exception);
        Assert.Equal(host.PathOf("shared.log"), failure.Path);

        // The established mode difference: streaming already wrote batch 1.
        Assert.Equal("Id\r\n1\r\n", host.ReadText("shared.log"));
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task CancellationFlushesAndClosesEveryDestination(TigerQueryExecutionMode mode)
    {
        using var cancellation = new CancellationTokenSource();
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["Id"], [emission.BatchNumber]);
            if (emission.BatchNumber == 2)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        };

        var engine = host.CreateEngine(host.BuildOptions(mode));
        var result = await engine.RunFromStringAsync(
            ":Out report.csv\r\nSELECT 1;\r\nGO\r\nSELECT 2;\r\nGO\r\nSELECT 3;\r\nGO\r\n",
            cancellation.Token);

        Assert.Equal(ExecutionResultCode.UserCancelled, result.ResultCode);

        // Content written before cancellation is durable and the handle is released.
        Assert.Equal("Id\r\n1\r\n2\r\n", host.ReadText("report.csv"));
        using var reopened = new FileStream(
            host.PathOf("report.csv"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        Assert.True(reopened.Length > 0);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ADiagnosticIsWrittenOnceToTheErrorFile(TigerQueryExecutionMode mode)
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ServerMessage("Deliberate severity 16.", 16);

        var routing = new OutputRoutingOptions { InitialErrorPath = "errors.log" };
        await host.RunAsync("SELECT 1;\r\nGO\r\n", mode, routing);

        Assert.Equal("Deliberate severity 16.\r\n", host.ReadText("errors.log"));
    }

    [Fact]
    public async Task AnOutputFailureReachesTheMessageCallbackButNoErrorFile()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            emission.BatchNumber == 1 ? ["Id"] : ["Other"],
            [emission.BatchNumber]);

        var routing = new OutputRoutingOptions { InitialErrorPath = "errors.log" };
        var result = await host.RunAsync(
            ":Out report.csv\r\nSELECT 1;\r\nGO\r\nSELECT 2;\r\nGO\r\n",
            routing: routing);

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        Assert.Contains(
            host.MessageCallbacks,
            entry => entry.Message.Severity == Events.SqlCmdMessage.SeverityFatalException);
        Assert.False(host.Exists("errors.log"));
    }
}
