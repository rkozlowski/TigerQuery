using ItTiger.TigerCli.Commands;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// Option surface for the basic default command: a friendly, interactive query runner.
/// Deliberately small — just a saved connection and a SQL query, both promptable. No
/// file/script mode, variables, mode tuning or logging (use <c>run</c> for those).
/// </summary>
public sealed class TigerSqlCmdQuerySettings : TigerCliSettings
{
    // Refers to a saved connection profile by name, not a raw connection string. Required so
    // a missing value fails in non-interactive mode; Promptable so an interactive run prompts
    // a selection from the saved connections surfaced by the "connections" provider.
    [TigerCliOption("-c|--connection",
        ValueName = "name",
        Required = true,
        Description = "Name of a saved SQL Server connection (managed via the 'connections' command).",
        DescriptionResourceKey = "Opt_Connection_Description",
        Provider = "connections",
        Promptable = TigerCliPromptable.Normal)]
    public string Connection { get; set; } = default!;

    // Promptable so an interactive run asks for the query when it is not supplied.
    [TigerCliOption("-q|--query",
        ValueName = "sql",
        Required = true,
        Description = "SQL query to run.",
        DescriptionResourceKey = "Opt_Query_Description",
        Promptable = TigerCliPromptable.Normal)]
    public string Query { get; set; } = default!;
}
