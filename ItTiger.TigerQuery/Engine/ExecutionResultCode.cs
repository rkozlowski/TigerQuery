using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Engine;


public enum ExecutionResultCode
{
    Success = 0,
    BatchFailed = 1,
    Fatal = 2,
    UserCancelled = 3,
    ConnectionFailed = 4,
    ParseError = 5,
    UnhandledException = 6,
    FatalException = 7
}