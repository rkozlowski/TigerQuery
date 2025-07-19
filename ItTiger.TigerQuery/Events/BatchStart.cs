using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

public sealed class BatchStart
{
    public int BatchNumber { get; init; }          // 1-based batch ID in script
    public int ExecutionIndex { get; init; }       // 1-based loop index (1..GO n)
    public int ExecutionCount { get; init; }       // Always >= 1
    public string SqlText { get; init; } = "";     // Raw batch text
}
