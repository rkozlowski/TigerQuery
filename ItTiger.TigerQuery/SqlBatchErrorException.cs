using ItTiger.TigerQuery.Events;

namespace ItTiger.TigerQuery;

/// <summary>
/// Represents one or more SQL Server errors that the provider reported for a batch
/// through informational-message delivery instead of throwing.
/// </summary>
/// <remarks>
/// <para>
/// The engine enables
/// <see cref="Microsoft.Data.SqlClient.SqlConnection.FireInfoMessageEventOnUserErrors"/>
/// so that a script keeps running past a recoverable server error and every
/// diagnostic reaches the message callback in order. As a result SQL Server
/// user errors — severity 11 through 16, which is what <c>RAISERROR</c> and
/// <c>THROW</c> normally produce — arrive as events rather than as a
/// <see cref="Microsoft.Data.SqlClient.SqlException"/>. This exception is the
/// engine's representation of those errors so that a failed batch always carries
/// a diagnostic, whichever transport the provider chose.
/// </para>
/// <para>
/// It is never thrown out of a run method. It appears as
/// <see cref="Events.BatchEnd.Exception"/> for the failing attempt and, when that
/// failure stops the run, as <see cref="Engine.ExecutionResult.Exception"/>. The
/// individual server diagnostics remain available through <see cref="Errors"/>
/// with their original number, severity, state, procedure, and line.
/// </para>
/// </remarks>
[Serializable]
public sealed class SqlBatchErrorException : Exception
{
    /// <summary>Initializes an exception with no message and no server diagnostics.</summary>
    public SqlBatchErrorException()
    {
    }

    /// <summary>Initializes an exception with a message and no server diagnostics.</summary>
    /// <param name="message">The error message.</param>
    public SqlBatchErrorException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes an exception with a message and underlying exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The exception that caused this failure.</param>
    public SqlBatchErrorException(string message, Exception inner)
        : base(message, inner)
    {
    }

    internal SqlBatchErrorException(IReadOnlyList<SqlCmdMessage> errors)
        : base(FormatMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>
    /// Gets the server errors observed during the batch, in the order the provider
    /// reported them.
    /// </summary>
    /// <remarks>
    /// The same messages were already delivered to
    /// <see cref="Engine.TigerQueryEngineOptions.OnMessage"/>; they are repeated here
    /// so the failure can be inspected without retaining the message stream.
    /// </remarks>
    public IReadOnlyList<SqlCmdMessage> Errors { get; } = [];

    private static string FormatMessage(IReadOnlyList<SqlCmdMessage> errors)
    {
        if (errors.Count == 0)
            return "The batch reported a SQL Server error.";

        var first = errors[0];
        var summary =
            $"{first.Text} (Severity {first.Severity}, State {first.State}, Number {first.Number})";

        return errors.Count == 1
            ? summary
            : $"{summary} and {errors.Count - 1} further SQL Server error(s).";
    }
}
