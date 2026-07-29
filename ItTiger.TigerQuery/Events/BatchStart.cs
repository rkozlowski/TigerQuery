using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

/// <summary>Describes a batch execution immediately before it starts.</summary>
public sealed class BatchStart
{
    /// <summary>Gets the one-based logical batch number in parser order.</summary>
    public int BatchNumber { get; init; }

    /// <summary>Gets the one-based repeat iteration for this execution.</summary>
    public int ExecutionIndex { get; init; }

    /// <summary>Gets the batch's positive <c>GO n</c> repeat count.</summary>
    public int ExecutionCount { get; init; }

    /// <summary>Gets the variable-expanded SQL text sent for this execution.</summary>
    public string SqlText { get; init; } = "";
}
