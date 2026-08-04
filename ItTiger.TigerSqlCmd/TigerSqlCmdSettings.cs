using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery;
using ItTiger.TigerQuery.Engine;
using Microsoft.Extensions.Logging;
using System.Text;

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
        Description = "Name of a saved SQL Server connection (managed via the 'connection' command).",
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

    // Result-output options stay application-owned. The engine remains responsible for
    // paths, file names, serialization, routing state, and file lifetime.
    [TigerCliOption("-o|--output", ValueName = "file",
        Description = "Result output: Initial path for routed result sets; :Out can replace it.",
        DescriptionResourceKey = "Opt_Run_Output_Description")]
    public string? OutputPath { get; set; }

    [TigerCliOption("--format|--result-format",
        Description = "Result output: Structured result-set format.",
        DescriptionResourceKey = "Opt_Run_ResultFormat_Description")]
    public ResultSetOutputFormat ResultSetFormat { get; set; } = ResultSetOutputFormat.Csv;

    [TigerCliOption("--output-mode|--result-file-mode",
        Description = "Result output: Write one file or one file per result set.",
        DescriptionResourceKey = "Opt_Run_OutputMode_Description")]
    public ResultSetFileMode ResultSetFileMode { get; set; } = ResultSetFileMode.SingleFile;

    [TigerCliOption("--output-encoding|--encoding", ValueName = "name",
        Description = "Result output: .NET encoding name for all routed files; default is UTF-8 with BOM.",
        DescriptionResourceKey = "Opt_Run_OutputEncoding_Description")]
    public string? OutputEncoding { get; set; }

    [TigerCliOption("-e|--error-output", ValueName = "file",
        Description = "Message/error routing: Initial path for SQL error messages; :Error can replace it.",
        DescriptionResourceKey = "Opt_Run_ErrorOutput_Description")]
    public string? ErrorOutputPath { get; set; }

    [TigerCliOption("--out-behavior",
        Description = "Message/error routing: Select whether :Out also redirects normal messages.",
        DescriptionResourceKey = "Opt_Run_OutBehavior_Description")]
    public OutDirectiveBehavior OutBehavior { get; set; } = OutDirectiveBehavior.ResultSetsOnly;

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
    public List<KeyValuePair<string, string>> Variables { get; set; } = [];

    public override TigerCliValidationResult Validate()
    {
        if (OutputPath is not null && string.IsNullOrWhiteSpace(OutputPath))
            return TigerCliValidationResult.Error(T("--output must not be empty or whitespace."));

        if (ErrorOutputPath is not null && string.IsNullOrWhiteSpace(ErrorOutputPath))
            return TigerCliValidationResult.Error(T("--error-output must not be empty or whitespace."));

        if (OutputEncoding is not null)
        {
            try
            {
                _ = GetOutputEncoding();
            }
            catch (ArgumentException)
            {
                return TigerCliValidationResult.Error(
                    F("'{0}' is not a supported .NET output encoding name.", OutputEncoding));
            }
        }

        return TigerCliValidationResult.Success();
    }

    /// <summary>
    /// Builds the application-selected initial routing configuration. TigerQuery owns
    /// every reusable behavior after this direct settings-to-options mapping.
    /// </summary>
    internal OutputRoutingOptions ToOutputRoutingOptions()
    {
        return new OutputRoutingOptions
        {
            InitialOutPath = OutputPath,
            InitialErrorPath = ErrorOutputPath,
            ResultSetFormat = ResultSetFormat,
            ResultSetFileMode = ResultSetFileMode,
            OutBehavior = OutBehavior,
            FileEncoding = GetOutputEncoding()
        };
    }

    private Encoding? GetOutputEncoding()
    {
        if (OutputEncoding is null)
            return null;

        return Encoding.GetEncoding(OutputEncoding);
    }
}
