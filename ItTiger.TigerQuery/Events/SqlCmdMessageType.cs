using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

public enum SqlCmdMessageType
{
    Print,          // PRINT and RAISERROR with severity 0
    Info,           // Optional: For general info from the server
    Raiserror,      // RAISERROR with severity 1–10 (non-fatal, e.g. user messaging)
    Warning,        // RAISERROR with severity 11–16
    Exception,      // Non-SQL Exception
    Error,          // RAISERROR with severity 17–19
    FatalError,     // RAISERROR with severity 20–25
    FatalException  // Non-SQL Fatal Exception
}
