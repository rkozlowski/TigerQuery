using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerSqlCmd;


public enum Verbosity
{
    Silent,       // No console output at all
    Quiet,        // Errors only
    Normal,       // Default (e.g. messages, warnings)
    Verbose,      // Adds batch start/end, durations, etc.
    VeryVerbose   // Adds fine-grained logs, variable dumps, timings
}
