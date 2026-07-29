namespace ItTiger.TigerQuery.Events;

/// <summary>
/// Describes a successfully prepared script immediately before connection
/// opening and SQL execution.
/// </summary>
/// <remarks>
/// This is a count-only notification. The prepared plan is internal, belongs to
/// one run, and cannot be inspected, retained, or executed through the public API.
/// </remarks>
public sealed class ExecutionPlanReady
{
    /// <summary>
    /// Gets the number of logical batches produced by the parser.
    /// </summary>
    public int LogicalBatchCount { get; init; }

    /// <summary>
    /// Gets the total number of scheduled batch executions, including positive
    /// <c>GO n</c> repeat counts.
    /// </summary>
    /// <remarks>
    /// Logical batches with zero or negative repeat counts contribute zero to
    /// this total. The value does not estimate work within a SQL batch.
    /// </remarks>
    public long TotalExecutionCount { get; init; }
}
