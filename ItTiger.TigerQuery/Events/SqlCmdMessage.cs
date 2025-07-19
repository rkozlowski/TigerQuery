using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

public sealed class SqlCmdMessage
{
    public const byte SeverityException = 254;
    public const byte SeverityFatalException = 255;
    public string Text { get; init; } = "";
    
    public byte Severity { get; init; }
    public int? LineNumber { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public SqlCmdMessageType Type => Severity switch
    {
        0 => SqlCmdMessageType.Print,
        >= 1 and <= 10 => SqlCmdMessageType.Raiserror,
        >= 11 and <= 16 => SqlCmdMessageType.Warning,       // optional: often used for validation
        >= 17 and <= 19 => SqlCmdMessageType.Error,
        >= 20 and < SeverityException => SqlCmdMessageType.FatalError,
        SeverityException => SqlCmdMessageType.Exception,
        SeverityFatalException => SqlCmdMessageType.FatalException
    };

    public bool IsError => Type >= SqlCmdMessageType.Warning;
    public bool IsFatalError => Type >= SqlCmdMessageType.FatalError;

    public byte State { get; internal set; }
    public int Number { get; internal set; }
    public string? Procedure { get; internal set; }

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] {Type} (Severity {Severity}): {Text}";
    public static SqlCmdMessage FromSqlError(SqlError error)
    {
        return new SqlCmdMessage
        {
            Text = error.Message,
            LineNumber = error.LineNumber > 0 ? error.LineNumber : null,
            Severity = (byte)error.Class,
            State = error.State,
            Number = error.Number,
            Procedure = error.Procedure,
            Timestamp = DateTime.UtcNow
        };
    }

    public static SqlCmdMessage FromException(Exception exception)
    {
        return new SqlCmdMessage
        {
            Text = exception.Message,
            LineNumber = null,
            Severity = exception is TigerQueryException ? SeverityFatalException : SeverityException,
            State = 0,
            Number = -1,
            Procedure = null,
            Timestamp = DateTime.UtcNow
        };
    }
}
