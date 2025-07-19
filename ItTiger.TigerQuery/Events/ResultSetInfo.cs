using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;


public sealed class ResultSetInfo
{
    public int BatchNumber { get; init; }
    public int ExecutionIndex { get; init; }
    public int ResultSetIndex { get; init; }

    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];
    public List<object?[]> Rows { get; init; } = [];
}

