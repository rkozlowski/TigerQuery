using ItTiger.TigerQuery;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ItTiger.TigerSqlCmd;


public sealed class TigerSqlCmdSettings : CommandSettings
{
    [CommandOption("-c|--connection <CONNSTR>")]
    [Description("SQL Server connection string.")]
    //[CommandRequired]
    public string ConnectionString { get; set; } = default!;

    [CommandOption("-f|--file <FILEPATH>")]
    [Description("Path to the SQL script file to execute.")]
    public string? FilePath { get; set; }

    [CommandOption("-q|--query <QUERY>")]
    [Description("Inline SQL query to execute.")]
    public string? Query { get; set; }

    [CommandOption("-m|--mode <MODE>")]
    [Description("Execution mode: normal | sqlcmd | sqlcmdex (default: sqlcmd)")]
    public SqlCmdMode Mode { get; set; } = SqlCmdMode.SqlCmd;

    [CommandOption("-l|--log-file <PATH>")]
    [Description("Optional path to the log file.")]
    public string? LogFile { get; init; }

    [CommandOption("--log-level <LEVEL>")]
    [Description("Minimum log level: Trace, Debug, Information, Warning, Error, Critical (default: Information).")]
    public LogLevel LogLevel { get; init; } = LogLevel.Information;

    [CommandOption("--verbosity <LEVEL>")]
    [Description("Controls output verbosity: Silent, Quiet, Normal, Verbose, VeryVerbose")]
    public Verbosity Verbosity { get; set; } = Verbosity.Normal;

    [CommandOption("-v|--var <VAR>")]
    [Description("SQLCMD-style variable. Repeatable. Use -v name=value, -v name \"value\", or -v \"name=value\".")]
    public string[] Variables { get; set; } = Array.Empty<string>();

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(FilePath) && string.IsNullOrWhiteSpace(Query))
            return ValidationResult.Error("Either --file or --query must be specified.");
        if (!string.IsNullOrWhiteSpace(FilePath) && !string.IsNullOrWhiteSpace(Query))
            return ValidationResult.Error("You cannot specify both --file and --query.");
        return ValidationResult.Success();
    }
}
