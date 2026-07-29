using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;


/// <summary>
/// Contains column metadata and all rows materialized from one SQL result set.
/// </summary>
/// <remarks>
/// The engine invokes the result-set callback only after reading the complete
/// result set. The payload, row list, and row arrays are not reused or modified
/// by the engine after delivery and may be retained by the consumer.
/// </remarks>
public sealed class ResultSetInfo
{
    /// <summary>Gets the one-based logical batch number that produced the result set.</summary>
    public int BatchNumber { get; init; }

    /// <summary>Gets the one-based repeat iteration that produced the result set.</summary>
    public int ExecutionIndex { get; init; }

    /// <summary>Gets the one-based result-set number within the execution.</summary>
    public int ResultSetIndex { get; init; }

    /// <summary>Gets the result columns in ordinal order.</summary>
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];

    /// <summary>
    /// Gets the materialized rows; each array contains values in column ordinal order.
    /// </summary>
    /// <remarks>Database null values are represented by <see cref="DBNull.Value"/>.</remarks>
    public List<object?[]> Rows { get; init; } = [];
}

