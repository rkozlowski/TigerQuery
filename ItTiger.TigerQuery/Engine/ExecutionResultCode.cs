using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Engine;

/// <summary>
/// Identifies the outcome recorded in an <see cref="ExecutionResult"/>.
/// </summary>
/// <remarks>
/// Parser, connection-opening, and some cancellation exceptions currently escape
/// the run method rather than being normalized into a result; the corresponding
/// values remain part of the public result-code contract for consumers.
/// </remarks>
public enum ExecutionResultCode
{
    /// <summary>
    /// Execution completed without a terminal failure.
    /// </summary>
    /// <remarks>
    /// Ignored nonfatal failures can still make
    /// <see cref="ExecutionResult.FailedBatches"/> greater than zero. This remains
    /// true for server errors reported without a thrown exception; an effective
    /// exit-on-error policy, however, never produces this code.
    /// </remarks>
    Success = 0,

    /// <summary>A batch failed and the effective error policy stopped execution.</summary>
    /// <remarks>
    /// The failure is either a caught nonfatal <see cref="Microsoft.Data.SqlClient.SqlException"/>
    /// or a <see cref="SqlBatchErrorException"/> built from server errors the provider
    /// reported as informational messages.
    /// </remarks>
    BatchFailed = 1,

    /// <summary>A fatal SQL Server error stopped execution.</summary>
    Fatal = 2,

    /// <summary>Cancellation was observed while a batch execution was in progress.</summary>
    UserCancelled = 3,

    /// <summary>Represents a failure while establishing the SQL connection.</summary>
    ConnectionFailed = 4,

    /// <summary>Represents a TigerQuery/sqlcmd structural parsing failure.</summary>
    ParseError = 5,

    /// <summary>An unexpected non-SQL exception stopped batch execution.</summary>
    UnhandledException = 6,

    /// <summary>A <see cref="TigerQueryException"/> stopped batch execution.</summary>
    FatalException = 7
}
