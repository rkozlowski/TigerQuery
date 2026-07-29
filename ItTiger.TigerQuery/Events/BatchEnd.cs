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

    /// <summary>Gets the one-based repeat iteration that completed.</summary>
    public int ExecutionIndex { get; init; }

    /// <summary>Gets the batch's positive <c>GO n</c> repeat count.</summary>
    public int ExecutionCount { get; init; }

    /// <summary>Gets whether the execution attempt completed without a caught exception.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the exception caught for the attempt, or <see langword="null"/> on success.</summary>
    public Exception? Exception { get; init; }

    /// <summary>Gets elapsed time for this execution attempt.</summary>
    public TimeSpan Duration { get; init; }
}
