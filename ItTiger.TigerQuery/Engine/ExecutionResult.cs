using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Engine;

/// <summary>
/// Summarizes batch execution after the engine reaches its result-producing path.
/// </summary>
public sealed class ExecutionResult
{
    /// <summary>Gets the terminal execution outcome.</summary>
    public ExecutionResultCode ResultCode { get; init; }

    /// <summary>
    /// Gets the number of batch execution attempts that completed successfully.
    /// </summary>
    /// <remarks>Positive <c>GO n</c> iterations are counted individually.</remarks>
    public int ExecutedBatches { get; init; }

    /// <summary>Gets the number of batch execution attempts that failed.</summary>
    public int FailedBatches { get; init; }

    /// <summary>
    /// Gets the final caught exception retained by the coordinator, if any.
    /// </summary>
    /// <remarks>
    /// Exceptions that escape parsing, connection opening, cancellation checks, or
    /// callbacks are thrown to the caller instead of being stored here.
    /// </remarks>
    public Exception? Exception { get; init; }

    /// <summary>Gets elapsed time spent in the batch-execution coordinator.</summary>
    /// <remarks>
    /// Prepared parsing and connection opening occur before this timer starts.
    /// </remarks>
    public TimeSpan TotalDuration { get; init; }
}
