using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;

namespace ItTiger.TigerQuery.Tests.Engine;

public sealed class TigerQueryExecutionModeTests
{
    [Fact]
    public void DefaultExecutionModeIsStreaming()
    {
        var options = new TigerQueryEngineOptions();

        Assert.Equal(TigerQueryExecutionMode.Streaming, options.ExecutionMode);
    }

    [Fact]
    public async Task StreamingPreservesCallbackOrderAndDoesNotRaisePlanReady()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var options = new TigerQueryEngineOptions
        {
            OnExecutionPlanReady = _ => events.Add("plan"),
            OnBatchStart = batch => events.Add($"start:{batch.BatchNumber}:{batch.ExecutionIndex}"),
            OnBatchEnd = batch => events.Add($"end:{batch.BatchNumber}:{batch.ExecutionIndex}")
        };
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            "SELECT 1\r\nGO\r\nSELECT 2\r\nGO\r\n",
            TestContext.Current.CancellationToken);
        events.Add("complete");

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(
            [
                "open",
                "start:1:1",
                "execute:1:1",
                "end:1:1",
                "start:2:1",
                "execute:2:1",
                "end:2:1",
                "complete"
            ],
            events);
    }

    [Fact]
    public async Task StreamingHandsEarlierBatchToExecutionBeforeLaterParserError()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var planReadyCount = 0;
        var options = new TigerQueryEngineOptions
        {
            OnExecutionPlanReady = _ => planReadyCount++
        };
        var engine = probe.CreateEngine(options);

        await Assert.ThrowsAsync<TigerQueryException>(
            () => engine.RunFromStringAsync(
                "PRINT 'first';\r\nGO\r\n:setvar\r\n",
                TestContext.Current.CancellationToken));

        Assert.Equal(["open", "execute:1:1"], events);
        Assert.Equal(0, planReadyCount);
    }

    [Fact]
    public async Task PreparedEmptyScriptRaisesZeroCountsThenOpensConnection()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var callbackCount = 0;
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = plan =>
            {
                callbackCount++;
                events.Add($"plan:{plan.LogicalBatchCount}:{plan.TotalExecutionCount}");
            }
        };
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            "",
            TestContext.Current.CancellationToken);
        events.Add("complete");

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(1, callbackCount);
        Assert.Equal(["plan:0:0", "open", "complete"], events);
    }

    [Fact]
    public async Task PreparedSuccessRaisesPlanReadyBeforeBatchCallbacks()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = plan =>
                events.Add($"plan:{plan.LogicalBatchCount}:{plan.TotalExecutionCount}"),
            OnBatchStart = batch => events.Add($"start:{batch.BatchNumber}:{batch.ExecutionIndex}"),
            OnBatchEnd = batch => events.Add($"end:{batch.BatchNumber}:{batch.ExecutionIndex}")
        };
        var engine = probe.CreateEngine(options);

        await engine.RunFromStringAsync(
            "SELECT 1\r\nGO 2\r\n",
            TestContext.Current.CancellationToken);
        events.Add("complete");

        Assert.Equal(
            [
                "plan:1:2",
                "open",
                "start:1:1",
                "execute:1:1",
                "end:1:1",
                "start:1:2",
                "execute:1:2",
                "end:1:2",
                "complete"
            ],
            events);
    }

    [Theory]
    [InlineData("PRINT 'first';\r\nGO\r\n:setvar\r\n")]
    [InlineData("PRINT 'first';\r\nGO\r\nPRINT 'held';\r\nGO invalid\r\n")]
    public async Task PreparedParserFailureDoesNotOpenOrExecute(string script)
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var planReadyCount = 0;
        var options = new TigerQueryEngineOptions
        {
            ConnectionString = "not a valid connection string",
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = _ => planReadyCount++
        };
        var engine = probe.CreateEngine(options);

        await Assert.ThrowsAsync<TigerQueryException>(
            () => engine.RunFromStringAsync(
                script,
                TestContext.Current.CancellationToken));

        Assert.Empty(events);
        Assert.Equal(0, probe.OpenCount);
        Assert.Equal(0, probe.ExecutionCount);
        Assert.Equal(0, planReadyCount);
    }

    [Fact]
    public async Task PreparedParserFailurePrecedesInvalidConnectionHandling()
    {
        var options = new TigerQueryEngineOptions
        {
            ConnectionString = "not a valid connection string",
            ExecutionMode = TigerQueryExecutionMode.Prepared
        };
        var engine = new TigerQueryEngine(options);

        await Assert.ThrowsAsync<TigerQueryException>(
            () => engine.RunFromStringAsync(
                ":setvar\r\n",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidPreparedPlanRaisesPlanReadyBeforeInvalidConnectionFailure()
    {
        var events = new List<string>();
        var options = new TigerQueryEngineOptions
        {
            ConnectionString = "not a valid connection string",
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = plan =>
                events.Add($"plan:{plan.LogicalBatchCount}:{plan.TotalExecutionCount}"),
            OnBatchStart = _ => events.Add("batch")
        };
        var engine = new TigerQueryEngine(options);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => engine.RunFromStringAsync(
                "SELECT 1\r\nGO\r\n",
                TestContext.Current.CancellationToken));

        Assert.Equal(["plan:1:1"], events);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task AlternatingOnErrorPoliciesExecuteIdentically(
        TigerQueryExecutionMode executionMode)
    {
        var events = new List<string>();
        var probe = new EngineProbe(
            events,
            execute: (_, batch, batchNumber, executionIndex, _) =>
            {
                events.Add($"execute:{batchNumber}:{executionIndex}");
                if (batch.Text.Contains("FAIL", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Expected test failure.");
                }

                return Task.CompletedTask;
            });
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = executionMode,
            ContinueOnErrorForUnhandledExceptions = true,
            OnBatchStart = batch => events.Add($"start:{batch.BatchNumber}"),
            OnBatchEnd = batch => events.Add($"end:{batch.BatchNumber}:{batch.Success}")
        };
        var engine = probe.CreateEngine(options);
        var script = """
            FAIL_IGNORE
            :ON ERROR IGNORE
            GO
            FAIL_EXIT
            :ON ERROR EXIT
            GO
            SHOULD_NOT_RUN
            :ON ERROR IGNORE
            GO
            """;

        var result = await engine.RunFromStringAsync(
            script,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.UnhandledException, result.ResultCode);
        Assert.Equal(2, result.FailedBatches);
        Assert.DoesNotContain(events, item => item == "start:3");
        Assert.Equal(2, probe.ExecutionCount);
    }

    [Fact]
    public async Task CancellationDuringPreparationRaisesNoCallbacksAndDoesNotOpen()
    {
        using var cancellation = new CancellationTokenSource();
        using var reader = new CancelAfterReadTextReader(
            "SELECT 1;\r\nGO\r\nSELECT 2;\r\nGO\r\n",
            cancellation,
            cancelAfterReadCount: 1);
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = _ => events.Add("plan"),
            OnBatchStart = _ => events.Add("batch")
        };
        var engine = probe.CreateEngine(options);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunAsync(reader, cancellation.Token));

        Assert.Empty(events);
        Assert.Equal(0, probe.OpenCount);
        Assert.Equal(0, probe.ExecutionCount);
    }

    [Fact]
    public async Task CancellationImmediatelyBeforePlanReadyRaisesNoCallback()
    {
        using var cancellation = new CancellationTokenSource();
        using var reader = new CancelAfterReadTextReader(
            "",
            cancellation,
            cancelAfterReadCount: 1);
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = _ => events.Add("plan")
        };
        var engine = probe.CreateEngine(options);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunAsync(reader, cancellation.Token));

        Assert.Empty(events);
        Assert.Equal(0, probe.OpenCount);
    }

    [Fact]
    public async Task CancellationFromPlanReadyPreventsConnectionAndBatchCallbacks()
    {
        using var cancellation = new CancellationTokenSource();
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var callbackCount = 0;
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = _ =>
            {
                callbackCount++;
                events.Add("plan");
                cancellation.Cancel();
            },
            OnBatchStart = _ => events.Add("batch")
        };
        var engine = probe.CreateEngine(options);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunFromStringAsync(
                "SELECT 1\r\nGO\r\n",
                cancellation.Token));

        Assert.Equal(1, callbackCount);
        Assert.Equal(["plan"], events);
        Assert.Equal(0, probe.OpenCount);
        Assert.Equal(0, probe.ExecutionCount);
    }

    [Fact]
    public void RepeatIterationAtIntMaxValueDoesNotWrap()
    {
        Assert.True(TigerQueryEngine.TryGetExecutionIndex(
            int.MaxValue,
            int.MaxValue - 1,
            out var finalExecutionIndex));
        Assert.Equal(int.MaxValue, finalExecutionIndex);

        Assert.False(TigerQueryEngine.TryGetExecutionIndex(
            int.MaxValue,
            int.MaxValue,
            out var afterFinalExecutionIndex));
        Assert.Equal(0, afterFinalExecutionIndex);
    }

    private sealed class CancelAfterReadTextReader : TextReader
    {
        private readonly StringReader _reader;
        private readonly CancellationTokenSource _cancellation;
        private readonly int _cancelAfterReadCount;
        private int _readCount;

        public CancelAfterReadTextReader(
            string text,
            CancellationTokenSource cancellation,
            int cancelAfterReadCount)
        {
            _reader = new StringReader(text);
            _cancellation = cancellation;
            _cancelAfterReadCount = cancelAfterReadCount;
        }

        public override int Peek()
        {
            return _reader.Peek();
        }

        public override int Read()
        {
            var result = _reader.Read();
            _readCount++;
            if (_readCount >= _cancelAfterReadCount)
            {
                _cancellation.Cancel();
            }

            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reader.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
