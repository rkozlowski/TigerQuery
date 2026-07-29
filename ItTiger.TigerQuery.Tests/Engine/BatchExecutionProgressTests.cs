using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Events;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Engine;

public sealed class BatchExecutionProgressTests
{
    [Fact]
    public async Task PreparedSingleBatchReportsBoundedProgress()
    {
        ExecutionPlanReady? plan = null;
        var starts = new List<BatchStart>();
        var ends = new List<BatchEnd>();
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = value => plan = value,
            OnBatchStart = starts.Add,
            OnBatchEnd = ends.Add
        };
        var engine = CreateEngine(options);

        await engine.RunFromStringAsync(
            "SELECT 1;\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.NotNull(plan);
        Assert.Equal(1, plan!.LogicalBatchCount);
        Assert.Equal(1L, plan.TotalExecutionCount);

        var start = Assert.Single(starts);
        var end = Assert.Single(ends);
        var expected = new ProgressPosition(1, 1, 1, 1L, 1, 1L);

        Assert.Equal(expected, ProgressPosition.From(start));
        Assert.Equal(expected, ProgressPosition.From(end));
        Assert.True(end.Success);
    }

    [Fact]
    public async Task PreparedRepeatedBatchesReportExactPlanProgress()
    {
        ExecutionPlanReady? plan = null;
        var starts = new List<BatchStart>();
        var ends = new List<BatchEnd>();
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = value => plan = value,
            OnBatchStart = starts.Add,
            OnBatchEnd = ends.Add
        };
        var engine = CreateEngine(options);

        await engine.RunFromStringAsync(
            "SELECT 1;\r\nGO 2\r\nSELECT 2;\r\nGO 3\r\n",
            TestContext.Current.CancellationToken);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.LogicalBatchCount);
        Assert.Equal(5L, plan.TotalExecutionCount);
        Assert.Equal(
            new long[] { 1, 2, 3, 4, 5 },
            starts.Select(start => start.OverallExecutionNumber));
        Assert.Equal(
            new[] { 1, 1, 2, 2, 2 },
            starts.Select(start => start.BatchNumber));
        Assert.Equal(
            new[] { 1, 2, 1, 2, 3 },
            starts.Select(start => start.ExecutionIndex));
        Assert.Equal(
            new[] { 2, 2, 3, 3, 3 },
            starts.Select(start => start.ExecutionCount));
        Assert.All(starts, start =>
        {
            Assert.Equal(plan.LogicalBatchCount, start.TotalLogicalBatchCount);
            Assert.Equal(plan.TotalExecutionCount, start.TotalExecutionCount);
        });
        Assert.Equal(
            starts.Select(ProgressPosition.From),
            ends.Select(ProgressPosition.From));
        Assert.All(ends, end => Assert.True(end.Success));
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming, null, null)]
    [InlineData(TigerQueryExecutionMode.Prepared, 4, 3L)]
    public async Task ZeroAndNegativeRepeatsDoNotProduceCallbacksOrAdvanceOverallProgress(
        TigerQueryExecutionMode executionMode,
        int? totalLogicalBatchCount,
        long? totalExecutionCount)
    {
        var starts = new List<BatchStart>();
        var ends = new List<BatchEnd>();
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = executionMode,
            OnBatchStart = starts.Add,
            OnBatchEnd = ends.Add
        };
        var engine = CreateEngine(options);
        var script = """
            SELECT 'zero';
            GO 0
            SELECT 'negative';
            GO -2
            SELECT 'repeated';
            GO 2
            SELECT 'last';
            GO
            """;

        await engine.RunFromStringAsync(
            script,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new long[] { 1, 2, 3 },
            starts.Select(start => start.OverallExecutionNumber));
        Assert.Equal(
            new[] { 3, 3, 4 },
            starts.Select(start => start.BatchNumber));
        Assert.Equal(
            new[] { 1, 2, 1 },
            starts.Select(start => start.ExecutionIndex));
        Assert.All(starts, start =>
        {
            Assert.Equal(totalLogicalBatchCount, start.TotalLogicalBatchCount);
            Assert.Equal(totalExecutionCount, start.TotalExecutionCount);
        });
        Assert.Equal(
            starts.Select(ProgressPosition.From),
            ends.Select(ProgressPosition.From));
    }

    [Fact]
    public async Task FailedPreparedExecutionKeepsFullPlanTotalsAndMatchingCallbackOrder()
    {
        ExecutionPlanReady? plan = null;
        var starts = new List<BatchStart>();
        var ends = new List<BatchEnd>();
        var events = new List<string>();
        var options = new TigerQueryEngineOptions
        {
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            OnExecutionPlanReady = value =>
            {
                plan = value;
                events.Add("plan");
            },
            OnBatchStart = start =>
            {
                starts.Add(start);
                events.Add("start");
            },
            OnBatchEnd = end =>
            {
                ends.Add(end);
                events.Add("end");
            }
        };
        var engine = CreateEngine(
            options,
            events,
            (_, _, _, _, _) => throw new InvalidOperationException("Expected test failure."));

        var result = await engine.RunFromStringAsync(
            "SELECT 1;\r\nGO 2\r\nSELECT 2;\r\nGO 3\r\n",
            TestContext.Current.CancellationToken);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.LogicalBatchCount);
        Assert.Equal(5L, plan.TotalExecutionCount);
        Assert.Equal(ExecutionResultCode.UnhandledException, result.ResultCode);
        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(["plan", "open", "start", "execute:1:1", "end"], events);

        var start = Assert.Single(starts);
        var end = Assert.Single(ends);
        Assert.Equal(
            new ProgressPosition(1, 1, 2, 1L, 2, 5L),
            ProgressPosition.From(start));
        Assert.Equal(ProgressPosition.From(start), ProgressPosition.From(end));
        Assert.False(end.Success);
        Assert.IsType<InvalidOperationException>(end.Exception);
    }

    private static TigerQueryEngine CreateEngine(
        TigerQueryEngineOptions options,
        List<string>? events = null,
        Func<QueryExecutionContext, SqlBatch, int, int, CancellationToken, Task>? execute = null)
    {
        return new TigerQueryEngine(
            options,
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                events?.Add("open");
                return Task.CompletedTask;
            },
            async (context, batch, batchNumber, executionIndex, cancellationToken) =>
            {
                events?.Add($"execute:{batchNumber}:{executionIndex}");
                if (execute is not null)
                {
                    await execute(
                        context,
                        batch,
                        batchNumber,
                        executionIndex,
                        cancellationToken);
                }
            });
    }

    private readonly record struct ProgressPosition(
        int BatchNumber,
        int ExecutionIndex,
        int ExecutionCount,
        long OverallExecutionNumber,
        int? TotalLogicalBatchCount,
        long? TotalExecutionCount)
    {
        public static ProgressPosition From(BatchStart value)
        {
            return new ProgressPosition(
                value.BatchNumber,
                value.ExecutionIndex,
                value.ExecutionCount,
                value.OverallExecutionNumber,
                value.TotalLogicalBatchCount,
                value.TotalExecutionCount);
        }

        public static ProgressPosition From(BatchEnd value)
        {
            return new ProgressPosition(
                value.BatchNumber,
                value.ExecutionIndex,
                value.ExecutionCount,
                value.OverallExecutionNumber,
                value.TotalLogicalBatchCount,
                value.TotalExecutionCount);
        }
    }
}
