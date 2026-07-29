namespace ItTiger.TigerQuery.Engine;

internal readonly record struct ExecutionBatch(
    SqlBatch Batch,
    bool ContinueOnError);

internal sealed class PreparedExecutionPlan
{
    public PreparedExecutionPlan(IReadOnlyList<ExecutionBatch> batches, long totalExecutionCount)
    {
        Batches = batches ?? throw new ArgumentNullException(nameof(batches));
        TotalExecutionCount = totalExecutionCount;
    }

    public IReadOnlyList<ExecutionBatch> Batches { get; }

    public int LogicalBatchCount => Batches.Count;

    public long TotalExecutionCount { get; }
}
