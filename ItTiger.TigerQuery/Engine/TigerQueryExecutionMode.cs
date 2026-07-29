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
    Streaming = 0,

    /// <summary>
    /// Parses the complete TigerQuery/sqlcmd structure before opening the SQL
    /// connection, then executes the prepared logical batches sequentially.
    /// </summary>
    Prepared = 1
}
