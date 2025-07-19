using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Events;
using ItTiger.TigerSqlCmd.Logging;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;


namespace ItTiger.TigerSqlCmd;

public sealed class TigerSqlCmdCommand : AsyncCommand<TigerSqlCmdSettings>
{
    private Verbosity _verbosity = Verbosity.Normal;

    private void WriteMessage(SqlCmdMessage message, bool isException)
    {
        var colour = message.Type switch
        {
            SqlCmdMessageType.Print => "silver",
            SqlCmdMessageType.Raiserror => "silver",
            SqlCmdMessageType.Info => "silver",
            SqlCmdMessageType.Warning => "orange3",
            SqlCmdMessageType.Error => "red3",
            SqlCmdMessageType.Exception => "red3",
            SqlCmdMessageType.FatalError => "red",
            SqlCmdMessageType.FatalException => "red",
            _ => "gray"
        };
        var minVerbosity = message.Type switch
        {
            SqlCmdMessageType.Print => Verbosity.Normal,
            SqlCmdMessageType.Raiserror => Verbosity.Normal,
            SqlCmdMessageType.Info => Verbosity.Normal,
            SqlCmdMessageType.Warning => Verbosity.Quiet,
            SqlCmdMessageType.Error => Verbosity.Quiet,
            SqlCmdMessageType.Exception => Verbosity.Quiet,
            SqlCmdMessageType.FatalError => Verbosity.Quiet,
            SqlCmdMessageType.FatalException => Verbosity.Quiet,
            _ => Verbosity.VeryVerbose
        };
        if (_verbosity >= minVerbosity)
        {
            AnsiConsole.MarkupLine($"[{colour}]{Markup.Escape(message.Text)}[/]");
        }        
    }

    private void WriteBatchStart(BatchStart start)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            AnsiConsole.MarkupLine($"[gray]--> Batch {start.BatchNumber} ({start.ExecutionIndex}/{start.ExecutionCount})[/] executing...");
        }
    }

    private void WriteBatchEnd(BatchEnd end)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            var duration = end.Duration.TotalMilliseconds.ToString("F0");
            var status = end.Success ? "[green]completed[/]" : "[red]failed[/]";
            AnsiConsole.MarkupLine($"{status} in {duration}ms");
        }
    }

    private void WriteResultSet(ResultSetInfo rsi)
    {
        if (rsi.Columns.Count == 0)
        {
            AnsiConsole.MarkupLine("[gray](No columns returned)[/]");
            return;
        }

        var table = new Table();

        // Add columns
        foreach (var col in rsi.Columns)
        {
            table.AddColumn($"[silver]{Markup.Escape(col.Name)}[/]");
        }

        // Add rows
        foreach (var row in rsi.Rows)
        {
            var cells = row.Select(value =>
            {
                var str = value switch
                {
                    null => "[gray](null)[/]",
                    DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                    _ => Markup.Escape(value.ToString() ?? "")
                };
                return str;
            }).ToArray();

            table.AddRow(cells);
        }

        AnsiConsole.Write(table);
    }


    public override async Task<int> ExecuteAsync(CommandContext context, TigerSqlCmdSettings settings)
    {
        ILogger? logger = null;
        _verbosity = settings.Verbosity;
        if (!string.IsNullOrWhiteSpace(settings.LogFile))
        { 
            var loggerFactory = NLogSetup.CreateLoggerFactory(settings.LogFile, settings.LogLevel);
            logger = loggerFactory.CreateLogger("TigerSqlCmd");
            logger.LogInformation("Starting tiger-sqlcmd with mode: {Mode}", settings.Mode);
        }

        var variables = SqlCmdVariableParser.Parse(settings.Variables);
        if (_verbosity >= Verbosity.Normal)
        {
            AnsiConsole.MarkupLine($"[gray]Mode:[/] {settings.Mode}");
            if (_verbosity == Verbosity.VeryVerbose)
            {
                AnsiConsole.MarkupLine($"[gray]Connecting to:[/] [blue]{Markup.Escape(settings.ConnectionString)}[/]");
            }

            if (_verbosity >= Verbosity.Verbose)
            {
                if (settings.FilePath != null)
                    AnsiConsole.MarkupLine($"[gray]Executing file:[/] [green]{Markup.Escape(settings.FilePath)}[/]");
                else
                    AnsiConsole.MarkupLine($"[gray]Executing query:[/] [green]{Markup.Escape(settings.Query ?? "")}[/]");

                if (variables.Any())
                {
                    AnsiConsole.MarkupLine("[gray]Variables:[/]");
                    foreach (var (key, value) in variables)
                        AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(key)}[/] = [yellow]{Markup.Escape(value)}[/]");
                }
            }
        }
        

        var options = new TigerQueryEngineOptions
        {
            ConnectionString = settings.ConnectionString,
            Mode = settings.Mode,
            Variables = variables,
            Logger = logger,
            OnMessage = WriteMessage,
            OnBatchStart = WriteBatchStart,
            OnBatchEnd = WriteBatchEnd,
            OnResultSet = WriteResultSet
        };

        var engine = new TigerQueryEngine(options);
        ExecutionResult result;
        if (!string.IsNullOrWhiteSpace(settings.FilePath))
        {
            result = await engine.RunFromFileAsync(settings.FilePath);
        }
        else
        {
            result = await engine.RunFromStringAsync(settings.Query ?? "");
        }        
        return (int)result.ResultCode;
    }
}
