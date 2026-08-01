namespace ItTiger.TigerQuery.Engine;

internal readonly record struct ExecutionBatch(
    SqlBatch Batch,
    bool ContinueOnError);

internal sealed class PreparedExecutionPlan
{
    public PreparedExecutionPlan(IReadOnlyList<ExecutionStep> steps, long totalExecutionCount)
    {
        Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        LogicalBatchCount = steps.Count(step => step is ExecuteBatchStep);
        TotalExecutionCount = totalExecutionCount;
    }

    /// <summary>
    /// Gets every parsed step in source order, including the output-route directives
    /// that a final-state snapshot would collapse.
    /// </summary>
    public IReadOnlyList<ExecutionStep> Steps { get; }

    /// <summary>Gets the batch steps only, in source order.</summary>
    public IEnumerable<ExecutionBatch> Batches =>
        Steps.OfType<ExecuteBatchStep>().Select(step => step.Execution);

    /// <summary>Gets the number of batch steps; route directives are not counted.</summary>
    public int LogicalBatchCount { get; }

    public long TotalExecutionCount { get; }
}
