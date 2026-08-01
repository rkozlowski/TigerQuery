using ItTiger.TigerQuery.Events;

namespace ItTiger.TigerQuery.Engine;

/// <summary>
/// Collects the SQL Server diagnostics the provider delivers through
/// <see cref="Microsoft.Data.SqlClient.SqlConnection.InfoMessage"/> while one batch
/// execution attempt is in progress.
/// </summary>
/// <remarks>
/// A message observed outside a batch attempt is not attributed to any batch. The
/// delivered-message set exists so that an error reported first as an event and then
/// again on a thrown exception is surfaced to the caller only once.
/// </remarks>
internal sealed class BatchDiagnostics
{
    private readonly List<SqlCmdMessage> errors = [];
    private readonly HashSet<(int Number, byte Severity, byte State, int? Line, string Text)> delivered = [];

    /// <summary>Gets the server errors observed during the attempt, in arrival order.</summary>
    public IReadOnlyList<SqlCmdMessage> Errors => errors;

    /// <summary>Gets whether at least one message qualified as an error.</summary>
    public bool HasError => errors.Count > 0;

    /// <summary>Gets whether at least one observed error was fatal.</summary>
    public bool HasFatalError { get; private set; }

    /// <summary>Records a message the provider delivered during the attempt.</summary>
    public void RecordDelivered(SqlCmdMessage message)
    {
        delivered.Add(KeyOf(message));

        if (!message.IsError)
            return;

        errors.Add(message);
        if (message.IsFatalError)
            HasFatalError = true;
    }

    /// <summary>
    /// Determines whether an equivalent diagnostic was already delivered during this
    /// attempt.
    /// </summary>
    public bool WasDelivered(SqlCmdMessage message) => delivered.Contains(KeyOf(message));

    private static (int, byte, byte, int?, string) KeyOf(SqlCmdMessage message) =>
        (message.Number, message.Severity, message.State, message.LineNumber, message.Text);
}
