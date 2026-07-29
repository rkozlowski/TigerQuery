namespace ItTiger.TigerQuery.Engine;

/// <summary>
/// Controls whether a script is parsed incrementally during execution or fully
/// prepared before a SQL connection is opened.
/// </summary>
public enum TigerQueryExecutionMode
{
    /// <summary>
    /// Parses and executes one logical batch at a time.
    /// </summary>
    /// <remarks>
    /// This is the default. The connection is opened before parsing begins, total
    /// counts are unknown, and a later parser failure can occur after earlier SQL
    /// batches have executed.
    /// </remarks>
    Streaming = 0,

    /// <summary>
    /// Parses the complete TigerQuery/sqlcmd structure before opening the SQL
    /// connection, then executes the prepared logical batches sequentially.
    /// </summary>
    /// <remarks>
    /// A parser failure or cancellation during preparation prevents connection
    /// opening and SQL execution. Preparation validates TigerQuery/sqlcmd structure,
    /// not T-SQL. It retains all expanded logical batch text for the run, so memory
    /// use grows with the complete script. The internal plan is not exposed or reusable.
    /// </remarks>
    Prepared = 1
}
