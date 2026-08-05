using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Events;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Engine;

/// <summary>
/// Covers the coordinator rule that a server error reported for a batch fails that
/// batch even when the provider's execution returned normally. SQL Server user errors
/// (severity 11-16) reach the engine through
/// <see cref="SqlConnection.InfoMessage"/> because the engine enables
/// <see cref="SqlConnection.FireInfoMessageEventOnUserErrors"/>, which is exactly the
/// transport <c>RAISERROR(..., 16, ...)</c> and <c>THROW</c> use.
/// </summary>
public sealed class BatchErrorAggregationTests
{
    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ServerErrorUnderOnErrorExitStopsTheRunAndFailsTheTriggeringBatch(
        TigerQueryExecutionMode executionMode)
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        probe.OnBatch("FAIL", engine => engine.ReportServerMessage(ServerError("Deliberate failure.")));
        var options = OptionsWith(executionMode, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            """
            :ON ERROR EXIT
            SELECT 'first'
            GO
            FAIL
            GO
            SELECT 'never'
            GO
            """,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(1, result.ExecutedBatches);

        // The failing batch ends once, unsuccessfully, and nothing after it starts.
        Assert.Equal(
            [
                "start:1:1",
                "execute:1:1",
                "end:1:1:True",
                "start:2:1",
                "execute:2:1",
                "message:16:Deliberate failure.",
                "end:2:1:False"
            ],
            events);
        Assert.Equal(2, probe.ExecutionCount);

        var batchError = Assert.IsType<SqlBatchErrorException>(result.Exception);
        Assert.Same(batchError, probe.LastBatchEnd!.Exception);
        var diagnostic = Assert.Single(batchError.Errors);
        Assert.Equal("Deliberate failure.", diagnostic.Text);
        Assert.Equal(16, diagnostic.Severity);
        Assert.Equal(50000, diagnostic.Number);
        Assert.Equal(7, diagnostic.State);
        Assert.Equal(3, diagnostic.LineNumber);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ServerErrorUnderOnErrorIgnoreCountsTheFailureAndContinues(
        TigerQueryExecutionMode executionMode)
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        probe.OnBatch("FAIL", engine => engine.ReportServerMessage(ServerError("Deliberate failure.")));
        var options = OptionsWith(executionMode, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            """
            :ON ERROR IGNORE
            SELECT 'first'
            GO
            FAIL
            GO
            SELECT 'after'
            GO
            """,
            TestContext.Current.CancellationToken);

        // Ignore governs how much of the script runs, not what the run reports: the
        // later batch executes and the run still ends as a failure.
        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(2, result.ExecutedBatches);
        Assert.Equal(
            [
                "start:1:1",
                "execute:1:1",
                "end:1:1:True",
                "start:2:1",
                "execute:2:1",
                "message:16:Deliberate failure.",
                "end:2:1:False",
                "start:3:1",
                "execute:3:1",
                "end:3:1:True"
            ],
            events);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task InformationalMessagesNeverFailABatch(TigerQueryExecutionMode executionMode)
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        probe.OnBatch("NOISY", engine =>
        {
            engine.ReportServerMessage(new SqlCmdMessage { Text = "PRINT output", Severity = 0 });
            engine.ReportServerMessage(new SqlCmdMessage { Text = "Low severity", Severity = 10 });
        });
        var options = OptionsWith(executionMode, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            ":ON ERROR EXIT\r\nNOISY\r\nGO\r\nSELECT 'after'\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(0, result.FailedBatches);
        Assert.Equal(2, result.ExecutedBatches);
        Assert.Contains("end:1:1:True", events);
        Assert.Contains("end:2:1:True", events);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(16, false)]
    public async Task TheErrorThresholdIsSeverityEleven(byte severity, bool expectedSuccess)
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        probe.OnBatch(
            "MAYBE",
            engine => engine.ReportServerMessage(new SqlCmdMessage { Text = "Boundary.", Severity = severity }));
        var options = OptionsWith(TigerQueryExecutionMode.Prepared, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            ":ON ERROR EXIT\r\nMAYBE\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedSuccess, result.ResultCode == ExecutionResultCode.Success);
        Assert.Equal(expectedSuccess ? 0 : 1, result.FailedBatches);
        Assert.Equal(expectedSuccess ? 1 : 0, result.ExecutedBatches);
    }

    [Fact]
    public async Task AFatalServerErrorReportedAsAMessageStopsTheRunEvenUnderIgnore()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        probe.OnBatch(
            "FATAL",
            engine => engine.ReportServerMessage(new SqlCmdMessage { Text = "Fatal.", Severity = 20 }));
        var options = OptionsWith(TigerQueryExecutionMode.Prepared, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            ":ON ERROR IGNORE\r\nFATAL\r\nGO\r\nSELECT 'never'\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.Fatal, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(0, result.ExecutedBatches);
        Assert.DoesNotContain("start:2:1", events);
    }

    [Fact]
    public async Task EveryDiagnosticOfAFailingBatchIsPreservedInOrder()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        probe.OnBatch("MULTI", engine =>
        {
            engine.ReportServerMessage(new SqlCmdMessage { Text = "Progress.", Severity = 0 });
            engine.ReportServerMessage(ServerError("First error."));
            engine.ReportServerMessage(ServerError("Second error."));
        });
        var options = OptionsWith(TigerQueryExecutionMode.Prepared, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            ":ON ERROR EXIT\r\nMULTI\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);

        var batchError = Assert.IsType<SqlBatchErrorException>(result.Exception);
        Assert.Equal(["First error.", "Second error."], batchError.Errors.Select(item => item.Text));
        Assert.Equal(
            ["message:0:Progress.", "message:16:First error.", "message:16:Second error."],
            events.Where(item => item.StartsWith("message:", StringComparison.Ordinal)));
        Assert.Contains("First error.", batchError.Message, StringComparison.Ordinal);
        Assert.Contains("1 further SQL Server error", batchError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingRepeatIterationUnderExitStopsTheRemainingIterations()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        probe.OnBatch("REPEAT", engine =>
        {
            if (probe.ExecutionCount == 2)
                engine.ReportServerMessage(ServerError("Second iteration failed."));
        });
        var options = OptionsWith(TigerQueryExecutionMode.Prepared, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            ":ON ERROR EXIT\r\nREPEAT\r\nGO 4\r\nSELECT 'never'\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(1, result.ExecutedBatches);
        Assert.Equal(2, probe.ExecutionCount);
        Assert.Equal(
            [
                "start:1:1",
                "execute:1:1",
                "end:1:1:True",
                "start:1:2",
                "execute:1:2",
                "message:16:Second iteration failed.",
                "end:1:2:False"
            ],
            events);
    }

    [Fact]
    public async Task AFailingRepeatIterationUnderIgnoreRunsTheRemainingIterations()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        probe.OnBatch("REPEAT", engine =>
        {
            if (probe.ExecutionCount == 2)
                engine.ReportServerMessage(ServerError("Second iteration failed."));
        });
        var options = OptionsWith(TigerQueryExecutionMode.Prepared, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            ":ON ERROR IGNORE\r\nREPEAT\r\nGO 3\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(2, result.ExecutedBatches);
        Assert.Equal(3, probe.ExecutionCount);
    }

    [Fact]
    public async Task DiagnosticsAreAttributedOnlyToTheBatchThatProducedThem()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        probe.OnBatch("FAIL", engine => engine.ReportServerMessage(ServerError("Batch two failed.")));
        var options = OptionsWith(TigerQueryExecutionMode.Prepared, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            ":ON ERROR IGNORE\r\nSELECT 1\r\nGO\r\nFAIL\r\nGO\r\nSELECT 2\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(2, result.ExecutedBatches);
        Assert.Equal(["end:1:1:True", "end:2:1:False", "end:3:1:True"],
            events.Where(item => item.StartsWith("end:", StringComparison.Ordinal)));

        // The final result carries no exception because the last attempt succeeded, but
        // that success does not erase the failure recorded for the middle batch.
        Assert.Null(result.Exception);
        Assert.Equal(ExecutionResultCode.BatchFailed, result.ResultCode);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ASuccessfulScriptIsUnaffected(TigerQueryExecutionMode executionMode)
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var options = OptionsWith(executionMode, events);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            ":ON ERROR EXIT\r\nSELECT 1\r\nGO\r\nSELECT 2\r\nGO 2\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(0, result.FailedBatches);
        Assert.Equal(3, result.ExecutedBatches);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(events, item => item.EndsWith(":False", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MessagesObservedOutsideABatchAreStillPublishedButFailNothing()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var options = OptionsWith(TigerQueryExecutionMode.Prepared, events);
        var engine = probe.CreateEngine(options);

        // Simulates a server message delivered while the connection is being opened.
        engine.ReportServerMessage(ServerError("Login-time notice."));

        var result = await engine.RunFromStringAsync(
            ":ON ERROR EXIT\r\nSELECT 1\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(0, result.FailedBatches);
        Assert.Contains("message:16:Login-time notice.", events);
    }

    [Fact]
    public void ADiagnosticAlreadyDeliveredForAnAttemptIsNotPublishedTwice()
    {
        var diagnostics = new BatchDiagnostics();
        var delivered = ServerError("Deliberate failure.");

        diagnostics.RecordDelivered(delivered);

        // A SqlException carrying the same server error must not republish it.
        Assert.True(diagnostics.WasDelivered(ServerError("Deliberate failure.")));
        Assert.False(diagnostics.WasDelivered(ServerError("A different failure.")));
        Assert.False(diagnostics.WasDelivered(new SqlCmdMessage
        {
            Text = "Deliberate failure.",
            Severity = 17,
            LineNumber = 3
        }));
        Assert.True(diagnostics.HasError);
        Assert.False(diagnostics.HasFatalError);
        Assert.Single(diagnostics.Errors);
    }

    private static SqlCmdMessage ServerError(string text) => new()
    {
        Text = text,
        Severity = 16,
        LineNumber = 3,
        Number = 50000,
        State = 7
    };

    private static TigerQueryEngineOptions OptionsWith(
        TigerQueryExecutionMode executionMode,
        List<string> events) => new()
        {
            ExecutionMode = executionMode,
            OnBatchStart = batch => events.Add($"start:{batch.BatchNumber}:{batch.ExecutionIndex}"),
            OnBatchEnd = batch => events.Add($"end:{batch.BatchNumber}:{batch.ExecutionIndex}:{batch.Success}"),
            OnMessage = (message, _) => events.Add($"message:{message.Severity}:{message.Text}")
        };

    private sealed class EngineProbe
    {
        private readonly List<string> _events;
        private readonly Dictionary<string, Action<TigerQueryEngine>> _batchActions = new(StringComparer.Ordinal);
        private TigerQueryEngine? _engine;

        public EngineProbe(List<string> events)
        {
            _events = events;
        }

        public int ExecutionCount { get; private set; }

        public BatchEnd? LastBatchEnd { get; private set; }

        /// <summary>Runs <paramref name="action"/> whenever a batch contains <paramref name="marker"/>.</summary>
        public void OnBatch(string marker, Action<TigerQueryEngine> action) => _batchActions[marker] = action;

        public TigerQueryEngine CreateEngine(TigerQueryEngineOptions options)
        {
            var wrapped = new TigerQueryEngineOptions
            {
                ConnectionString = options.ConnectionString,
                ExecutionMode = options.ExecutionMode,
                Mode = options.Mode,
                ContinueOnError = options.ContinueOnError,
                OnBatchStart = options.OnBatchStart,
                OnBatchEnd = batch =>
                {
                    LastBatchEnd = batch;
                    options.OnBatchEnd?.Invoke(batch);
                },
                OnMessage = options.OnMessage,
                OnExecutionPlanReady = options.OnExecutionPlanReady
            };

            _engine = new TigerQueryEngine(wrapped, OpenAsync, ExecuteAsync);
            return _engine;
        }

        private Task OpenAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        private Task ExecuteAsync(
            QueryExecutionContext context,
            SqlBatch batch,
            int batchNumber,
            int executionIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutionCount++;
            _events.Add($"execute:{batchNumber}:{executionIndex}");

            foreach (var (marker, action) in _batchActions)
            {
                if (batch.Text.Contains(marker, StringComparison.Ordinal))
                    action(_engine!);
            }

            return Task.CompletedTask;
        }
    }
}
