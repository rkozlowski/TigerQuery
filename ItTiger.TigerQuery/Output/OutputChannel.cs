namespace ItTiger.TigerQuery.Output;

/// <summary>
/// Identifies one routable payload channel.
/// </summary>
/// <remarks>
/// A physical destination belongs to exactly one channel for the whole run. Batch
/// lifecycle, plan readiness, progress, and logging are not channels and are never
/// redirected.
/// </remarks>
internal enum OutputChannel
{
    ResultSets,
    NormalMessages,
    ErrorMessages
}

/// <summary>
/// Records where a message came from, which is what decides whether it can enter a
/// routed message file.
/// </summary>
/// <remarks>
/// The message callback's <c>isException</c> Boolean cannot make this decision: SQL
/// Server diagnostics surfaced through a thrown
/// <see cref="Microsoft.Data.SqlClient.SqlException"/> also use <c>true</c>, and a
/// synthetic message built from an arbitrary exception has an
/// <see cref="Events.SqlCmdMessage.IsError"/> of <see langword="true"/> only because
/// of its synthetic severity.
/// </remarks>
internal enum MessageOrigin
{
    /// <summary>
    /// A SQL Server diagnostic, delivered either through <c>InfoMessage</c> or on a
    /// thrown <see cref="Microsoft.Data.SqlClient.SqlException"/>.
    /// </summary>
    /// <remarks>
    /// This is the only origin TigerQuery currently models as script output. Its
    /// <see cref="Events.SqlCmdMessage.IsError"/> value selects the normal or error
    /// channel.
    /// </remarks>
    ServerDiagnostic,

    /// <summary>
    /// A synthetic message converted from an engine, infrastructure, cancellation,
    /// or application exception.
    /// </summary>
    /// <remarks>
    /// Never written to a routed file. It reaches the message callback and the
    /// logger only, which keeps connection strings, framework text, and unrelated
    /// exception detail out of the stable script-error file contract.
    /// </remarks>
    EngineException
}
