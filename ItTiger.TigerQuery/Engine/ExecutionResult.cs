using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Engine;

public sealed class ExecutionResult
{
    public ExecutionResultCode ResultCode { get; init; }
    public int ExecutedBatches { get; init; }
    public int FailedBatches { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan TotalDuration { get; init; }
}
