using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;

namespace ItTiger.TigerQuery.Tests.Engine;

/// <summary>
/// Covers the ordered execution-step model shared by streaming and prepared
/// execution: route directives are retained in order, they do not affect plan
/// counts or batch lifecycle, and both modes consume the same stream.
/// </summary>
public sealed class ExecutionStepModeTests
{
    private const string RoutedScript =
        ":Out first.csv\r\n"
        + "SELECT 1;\r\nGO\r\n"
        + ":Error errors.log\r\n"
        + "SELECT 2;\r\n"
        + ":Out second.csv\r\nGO 2\r\n"
        + ":Out first.csv\r\n"
        + "SELECT 3;\r\nGO\r\n";

    private const string UnroutedScript =
        "SELECT 1;\r\nGO\r\n"
        + "SELECT 2;\r\nGO 2\r\n"
        + "SELECT 3;\r\nGO\r\n";

    [Fact]
    public async Task PreparedPlanRetainsEveryDirectiveInSourceOrder()
    {
        var plan = await TestHelper.PrepareAsync(RoutedScript);

        Assert.Equal(
            [
                "out:first.csv",
                "batch:SELECT 1;:1",
                "error:errors.log",
                "out:second.csv",
                "batch:SELECT 2;:2",
                "out:first.csv",
                "batch:SELECT 3;:1"
            ],
            TestHelper.Describe(plan.Steps));
    }

    [Fact]
    public async Task PreparedPlanStepsMatchTheStreamingStepStream()
    {
        var plan = await TestHelper.PrepareAsync(RoutedScript);
        var streamingSteps = await TestHelper.ParseStepsAsync(RoutedScript);

        Assert.Equal(TestHelper.Describe(streamingSteps), TestHelper.Describe(plan.Steps));
    }

    [Fact]
    public async Task DirectiveStepsDoNotChangePlanCounts()
    {
        var routed = await TestHelper.PrepareAsync(RoutedScript);
        var unrouted = await TestHelper.PrepareAsync(UnroutedScript);

        Assert.Equal(3, routed.LogicalBatchCount);
        Assert.Equal(4L, routed.TotalExecutionCount);
        Assert.Equal(unrouted.LogicalBatchCount, routed.LogicalBatchCount);
        Assert.Equal(unrouted.TotalExecutionCount, routed.TotalExecutionCount);
        Assert.Equal(7, routed.Steps.Count);
    }

    [Fact]
    public async Task PlanBatchesProjectionExcludesRouteSteps()
    {
        var plan = await TestHelper.PrepareAsync(RoutedScript);

        Assert.All(plan.Steps.OfType<ExecuteBatchStep>(), step => Assert.NotNull(step.Execution.Batch));
        Assert.Equal(
            ["SELECT 1;", "SELECT 2;", "SELECT 3;"],
            plan.Batches.Select(batch => batch.Batch.Text.Trim()));
    }

    [Fact]
    public async Task ContinueOnErrorSnapshotsSurviveInterleavedDirectives()
    {
        var script = """
            :ON ERROR EXIT
            :Out first.csv
            SELECT 1;
            GO
            :ON ERROR IGNORE
            SELECT 2;
            :Error errors.log
            GO
            :Out second.csv
            :ON ERROR EXIT
            SELECT 3;
            GO
            """;

        var plan = await TestHelper.PrepareAsync(script);

        Assert.Equal(
            [false, true, false],
            plan.Batches.Select(batch => batch.ContinueOnError));
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task RouteDirectivesAreOrderedNoOpsDuringExecution(
        TigerQueryExecutionMode executionMode)
    {
        var routedEvents = await RunAsync(executionMode, RoutedScript);
        var unroutedEvents = await RunAsync(executionMode, UnroutedScript);

        Assert.Equal(unroutedEvents, routedEvents);
    }

    [Fact]
    public async Task RoutedScriptProducesTheSameLifecycleInBothModes()
    {
        var streamingEvents = await RunAsync(TigerQueryExecutionMode.Streaming, RoutedScript);
        var preparedEvents = await RunAsync(TigerQueryExecutionMode.Prepared, RoutedScript);

        // Prepared mode adds only its plan-ready notification, whose totals count
        // batch steps and their executions but not route directives.
        Assert.Equal("plan:3:4", preparedEvents[0]);
        Assert.Equal(streamingEvents, preparedEvents.Skip(1));
        Assert.Equal(
            [
                "open",
                "start:1:1",
                "execute:1:1",
                "end:1:1:True",
                "start:2:1",
                "execute:2:1",
                "end:2:1:True",
                "start:2:2",
                "execute:2:2",
                "end:2:2:True",
                "start:3:1",
                "execute:3:1",
                "end:3:1:True",
                "complete"
            ],
            streamingEvents);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task RouteDirectivesRaiseNoAdditionalCallbacks(
        TigerQueryExecutionMode executionMode)
    {
        var messages = 0;
        var resultSets = 0;
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = executionMode,
            OnMessage = (_, _) => messages++,
            OnResultSet = _ => resultSets++
        };
        var probe = new EngineProbe([]);
        var engine = probe.CreateEngine(options);

        var result = await engine.RunFromStringAsync(
            RoutedScript,
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(4, result.ExecutedBatches);
        Assert.Equal(0, result.FailedBatches);
        Assert.Equal(0, messages);
        Assert.Equal(0, resultSets);
        Assert.Equal(4, probe.ExecutionCount);
    }

    [Fact]
    public async Task PreparedDirectiveSyntaxErrorDoesNotOpenOrExecute()
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
                "PRINT 'first';\r\nGO\r\n:Out one.csv two.csv\r\n",
                TestContext.Current.CancellationToken));

        Assert.Empty(events);
        Assert.Equal(0, probe.OpenCount);
        Assert.Equal(0, probe.ExecutionCount);
        Assert.Equal(0, planReadyCount);
    }

    [Fact]
    public async Task StreamingDirectiveSyntaxErrorStillFollowsEarlierExecution()
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var engine = probe.CreateEngine(new TigerQueryEngineOptions());

        await Assert.ThrowsAsync<TigerQueryException>(
            () => engine.RunFromStringAsync(
                "PRINT 'first';\r\nGO\r\n:Out one.csv two.csv\r\n",
                TestContext.Current.CancellationToken));

        Assert.Equal(["open", "execute:1:1"], events);
    }

    private static async Task<List<string>> RunAsync(
        TigerQueryExecutionMode executionMode,
        string script)
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = executionMode,
            OnExecutionPlanReady = plan =>
                events.Add($"plan:{plan.LogicalBatchCount}:{plan.TotalExecutionCount}"),
            OnBatchStart = batch => events.Add($"start:{batch.BatchNumber}:{batch.ExecutionIndex}"),
            OnBatchEnd = batch =>
                events.Add($"end:{batch.BatchNumber}:{batch.ExecutionIndex}:{batch.Success}")
        };
        var engine = probe.CreateEngine(options);

        await engine.RunFromStringAsync(script, TestContext.Current.CancellationToken);
        events.Add("complete");

        return events;
    }
}
