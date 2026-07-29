using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

/// <summary>Categorizes SQL Server messages and engine-observed exceptions.</summary>
public enum SqlCmdMessageType
{
    /// <summary>A <c>PRINT</c> message or SQL message with severity zero.</summary>
    Print,

    /// <summary>A general informational message not classified by SQL severity.</summary>
    Info,

    /// <summary>A nonfatal <c>RAISERROR</c> or SQL message with severity 1 through 10.</summary>
    Raiserror,

    /// <summary>A SQL message with severity 11 through 16.</summary>
    Warning,

    /// <summary>A non-SQL exception represented by the engine's synthetic severity.</summary>
    Exception,

    /// <summary>A SQL message with severity 17 through 19.</summary>
    Error,

    /// <summary>A fatal SQL message with severity 20 or greater.</summary>
    FatalError,

    /// <summary>A <see cref="TigerQueryException"/> represented as a fatal exception message.</summary>
    FatalException
}
