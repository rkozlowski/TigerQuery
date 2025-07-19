using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

public sealed class BatchEnd
{
    public int BatchNumber { get; init; }
    public int ExecutionIndex { get; init; }
    public int ExecutionCount { get; init; }

    public bool Success { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan Duration { get; init; }
}
