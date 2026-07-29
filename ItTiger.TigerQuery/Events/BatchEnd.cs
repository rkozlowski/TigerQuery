using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

/// <summary>Describes the outcome of a batch execution attempt.</summary>
public sealed class BatchEnd
{
    /// <summary>Gets the one-based logical batch number in parser order.</summary>
    public int BatchNumber { get; init; }

    /// <summary>
    /// Gets the total number of logical batches in the complete prepared plan,
    /// or <see langword="null"/> when the total is unknown in streaming mode.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means unknown, not zero. In prepared mode the value
    /// describes the complete plan even when execution stops early. It represents
    /// batch execution progress, not progress within a SQL batch.
    /// </remarks>
    public int? TotalLogicalBatchCount { get; init; }

    /// <summary>Gets the one-based execution index within the current logical batch.</summary>
    public int ExecutionIndex { get; init; }

    /// <summary>Gets the repeat count for the current logical batch.</summary>
    public int ExecutionCount { get; init; }

    /// <summary>
    /// Gets the one-based execution attempt number across the whole run.
    /// </summary>
    /// <remarks>
    /// The value matches the corresponding batch-start callback, whose invocation
    /// advances the counter. Failed attempts retain the same value. It represents
    /// batch execution progress, not progress within a SQL batch.
    /// </remarks>
    public long OverallExecutionNumber { get; init; }

    /// <summary>
    /// Gets the total scheduled execution attempts in the complete prepared plan,
    /// or <see langword="null"/> when the total is unknown in streaming mode.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means unknown, not zero. In prepared mode the value
    /// includes positive <c>GO n</c> repeat counts and continues to describe the
    /// complete plan when execution stops early. It does not estimate progress
    /// within a SQL batch.
    /// </remarks>
    public long? TotalExecutionCount { get; init; }

    /// <summary>Gets whether the execution attempt completed without a caught exception.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the exception caught for the attempt, or <see langword="null"/> on success.</summary>
    public Exception? Exception { get; init; }

    /// <summary>Gets elapsed time for this execution attempt.</summary>
    public TimeSpan Duration { get; init; }
}
