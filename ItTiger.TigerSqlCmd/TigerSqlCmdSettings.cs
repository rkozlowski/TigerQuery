using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery;
using Microsoft.Extensions.Logging;

namespace ItTiger.TigerSqlCmd;


[TigerCliExactlyOneOf(nameof(FilePath), nameof(Query))]
public sealed class TigerSqlCmdSettings : TigerCliSettings
{
    // Refers to a saved connection profile by name, not a raw connection string. Required so
    // a missing value fails in non-interactive mode; Promptable so semi-interactive runs prompt
    // a selection from the saved connections surfaced by the "connections" provider.
    [TigerCliOption("-c|--connection",
        ValueName = "name",
        Required = true,
        Description = "Name of a saved SQL Server connection (managed via the 'connections' command).",
        DescriptionResourceKey = "Opt_Connection_Description",
        Provider = "connections",
        Promptable = TigerCliPromptable.Normal)]
    public string Connection { get; set; } = default!;

    [TigerCliOption("-f|--file", ValueName = "file", Description = "Path to the SQL script file to execute.",
        DescriptionResourceKey = "Opt_Run_File_Description")]
    public string? FilePath { get; set; }

    [TigerCliOption("-q|--query", ValueName = "sql", Description = "Inline SQL query to execute.",
        DescriptionResourceKey = "Opt_Run_Query_Description")]
    public string? Query { get; set; }

    [TigerCliOption("-m|--mode", Description = "Execution mode.",
        DescriptionResourceKey = "Opt_Run_Mode_Description")]
    public SqlCmdMode Mode { get; set; } = SqlCmdMode.SqlCmd;

    [TigerCliOption("-l|--log-file", ValueName = "file", Description = "Path to the log file.",
        DescriptionResourceKey = "Opt_Run_LogFile_Description")]
    public string? LogFile { get; init; }

    [TigerCliOption("--log-level", Description = "Minimum log level.",
        DescriptionResourceKey = "Opt_Run_LogLevel_Description")]
    public LogLevel LogLevel { get; init; } = LogLevel.Information;

    [TigerCliOption("--verbosity", Description = "Controls output verbosity.",
        DescriptionResourceKey = "Opt_Run_Verbosity_Description")]
    public Verbosity Verbosity { get; set; } = Verbosity.Normal;

    [TigerCliOption("-v|--var", ValueName = "name=value", Description = "SQLCMD-style variable.",
        DescriptionResourceKey = "Opt_Run_Var_Description")]
    public List<KeyValuePair<string, string>> Variables { get; set; } = new List<KeyValuePair<string, string>>();
}
