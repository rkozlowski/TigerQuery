using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Enums;
using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Primitives;
using ItTiger.TigerCli.Rendering;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerQuery.Events;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// Verbosity-gated rendering of TigerQuery engine events (messages, batch progress and
/// result sets). Shared by the basic default command and the advanced <c>run</c> command
/// so both present output identically; the engine itself is unchanged. The settings
/// instance supplies the run-culture text helpers for the renderer's own labels.
/// </summary>
internal sealed class TigerSqlCmdRenderer
{
    private readonly Verbosity _verbosity;
    private readonly TigerCliSettings _settings;

    public TigerSqlCmdRenderer(Verbosity verbosity, TigerCliSettings settings)
    {
        _verbosity = verbosity;
        _settings = settings;
    }

    public void WriteMessage(SqlCmdMessage message, bool isException)
    {
        // Semantic theme roles, not raw colours: SQL messages are payload (plain text),
        // warnings/errors take the theme's severity styles, anything unknown is muted.
        var style = message.Type switch
        {
            SqlCmdMessageType.Print => null,
            SqlCmdMessageType.Raiserror => null,
            SqlCmdMessageType.Info => null,
            SqlCmdMessageType.Warning => "Warning",
            SqlCmdMessageType.Error => "Error",
            SqlCmdMessageType.Exception => "Error",
            SqlCmdMessageType.FatalError => "Error",
            SqlCmdMessageType.FatalException => "Error",
            _ => "Muted"
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
            var text = CliMarkupParser.Escape(message.Text);
            TigerConsole.MarkupLine(style is null ? text : $"[{style}]{text}[/]");
        }
    }

    public void WriteBatchStart(BatchStart start)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            TigerConsole.MarkupLine(_settings.E(
                "[Muted]--> Batch {0} ({1}/{2})[/] executing...",
                start.BatchNumber, start.ExecutionIndex, start.ExecutionCount));
        }
    }

    public void WriteBatchEnd(BatchEnd end)
    {
        if (_verbosity >= Verbosity.Verbose)
        {
            var duration = end.Duration.TotalMilliseconds.ToString("F0");
            TigerConsole.MarkupLine(end.Success
                ? _settings.E("[Success]completed[/] in {0}ms", duration)
                : _settings.E("[Error]failed[/] in {0}ms", duration));
        }
    }

    public void WriteResultSet(ResultSetInfo rsi)
    {
        if (rsi.Columns.Count == 0)
        {
            if (_verbosity >= Verbosity.Verbose)
            {
                TigerConsole.MarkupLine(_settings.T("[Muted](No columns returned)[/]"));
            }
            return;
        }

        var table = new CliTable();
        table.DefaultCellStyle = new CliCellStyle
        {
            NullDisplayValue = "[Muted](null)[/]",
            FormattingMode = CliFormattingMode.Raw
        };
        var headerStyle = new CliCellStyle
        {
            Wrapping = CliWrapping.SingleLineTruncate,
            FormattingMode = CliFormattingMode.Raw
        };
        table.Header.HeaderStyle = headerStyle;

        foreach (var col in rsi.Columns)
        {
            bool isNumeric = IsNumericType(col.ClrType);
            bool isDateTime = col.ClrType == typeof(DateTime) || col.ClrType == typeof(DateTime?);

            var dataStyle = new CliCellStyle
            {
                Wrapping = isNumeric ? CliWrapping.SingleLineTruncate : CliWrapping.SymbolWrapTruncate,
                HorizontalAlignment = isNumeric ? CliTextAlignment.Right : CliTextAlignment.Left
            };

            if (isDateTime)
            {
                dataStyle.Formatter = CliFormatter.FromDelegate(obj =>
                    obj is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm:ss") : obj?.ToString() ?? string.Empty);
            }

            table.Header.Elements.Add(new CliTableElement(col.Name, dataStyle));
        }

        // Add rows - values processed by column formatters and null display
        foreach (var row in rsi.Rows)
        {
            table.Records.Add(row.ToList());
        }
        TigerConsole.Render(table);
    }

    private static bool IsNumericType(Type type)
    {
        if (type == null)
            return false;

        // Unwrap Nullable<T>
        type = Nullable.GetUnderlyingType(type) ?? type;

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte or
            TypeCode.SByte or
            TypeCode.Int16 or
            TypeCode.UInt16 or
            TypeCode.Int32 or
            TypeCode.UInt32 or
            TypeCode.Int64 or
            TypeCode.UInt64 or
            TypeCode.Single or
            TypeCode.Double or
            TypeCode.Decimal => true,
            _ => false
        };
    }
}
