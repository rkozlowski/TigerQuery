using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

/// <summary>
/// Represents a SQL Server informational/error message or an exception converted
/// to the engine's message model.
/// </summary>
public sealed class SqlCmdMessage
{
    /// <summary>Gets the synthetic severity assigned to non-SQL exceptions.</summary>
    public const byte SeverityException = 254;

    /// <summary>Gets the synthetic severity assigned to <see cref="TigerQueryException"/>.</summary>
    public const byte SeverityFatalException = 255;

    /// <summary>Gets the message text.</summary>
    public string Text { get; init; } = "";

    /// <summary>Gets the SQL Server or synthetic severity.</summary>
    public byte Severity { get; init; }

    /// <summary>
    /// Gets the SQL Server-reported one-based line number, or
    /// <see langword="null"/> when unavailable.
    /// </summary>
    /// <remarks>SQL Server line numbers are relative to the submitted batch.</remarks>
    public int? LineNumber { get; init; }

    /// <summary>Gets the UTC time at which the message object was created.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Gets the category derived from <see cref="Severity"/>.</summary>
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

    /// <summary>Gets whether the category is a warning, error, or exception.</summary>
    public bool IsError => Type >= SqlCmdMessageType.Warning;

    /// <summary>Gets whether the category is a fatal SQL error or fatal exception.</summary>
    public bool IsFatalError => Type >= SqlCmdMessageType.FatalError;

    /// <summary>Gets the SQL Server state value, or zero for converted exceptions.</summary>
    public byte State { get; internal set; }

    /// <summary>Gets the SQL Server error number, or -1 for converted exceptions.</summary>
    public int Number { get; internal set; }

    /// <summary>Gets the SQL Server stored-procedure name when one was reported.</summary>
    public string? Procedure { get; internal set; }

    /// <summary>Formats the timestamp, category, severity, and message text.</summary>
    /// <returns>A concise display string for the message.</returns>
    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] {Type} (Severity {Severity}): {Text}";

    /// <summary>Creates a message from a SQL Server error.</summary>
    /// <param name="error">The provider error to copy.</param>
    /// <returns>A message containing the provider's diagnostics.</returns>
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

    /// <summary>Creates a synthetic message from a non-SQL exception.</summary>
    /// <param name="exception">The exception whose message and category are copied.</param>
    /// <returns>
    /// A fatal-exception message for <see cref="TigerQueryException"/>; otherwise
    /// a nonfatal exception message.
    /// </returns>
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
