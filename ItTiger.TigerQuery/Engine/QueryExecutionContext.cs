using ItTiger.TigerQuery.Events;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Engine;

/// <summary>
/// Holds mutable per-run parser state and executes batches on a caller-supplied
/// SQL connection.
/// </summary>
/// <remarks>
/// The context does not own or dispose the connection. It is mutable and not
/// safe for concurrent parsing or execution.
/// </remarks>
public sealed class QueryExecutionContext
{
    private readonly Dictionary<string, SqlCmdVariable> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly TigerQueryEngineOptions _options;

    private readonly SqlConnection _connection;

    /// <summary>
    /// Gets or sets whether execution should continue after a nonfatal batch failure.
    /// </summary>
    /// <remarks>
    /// Initialized from <see cref="TigerQueryEngineOptions.ContinueOnError"/> and
    /// updated while parsing <c>:ON ERROR IGNORE</c> or <c>:ON ERROR EXIT</c>.
    /// </remarks>
    public bool ContinueOnError { get; set; } = true;

    /// <summary>Initializes a per-run context.</summary>
    /// <param name="options">The parser, execution, and callback options.</param>
    /// <param name="connection">
    /// The SQL connection used by <see cref="ExecuteBatchAsync"/>. The context
    /// does not open or dispose it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> or <paramref name="connection"/> is
    /// <see langword="null"/>.
    /// </exception>
    public QueryExecutionContext(TigerQueryEngineOptions options, SqlConnection connection)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ContinueOnError = _options.ContinueOnError;
        // Initialize programmatic variables
        if (options.Variables != null && options.Mode != SqlCmdMode.Normal)
        {
            foreach (var kv in options.Variables)
            {
                _variables[kv.Key] = new SqlCmdVariable(
                    name: kv.Key,
                    value: kv.Value,
                    canBeOverridden: options.Mode == SqlCmdMode.SqlCmd
                );
            }
        }
    }

    /// <summary>Gets the configured parser mode.</summary>
    public SqlCmdMode Mode => _options.Mode;

    /// <summary>
    /// Gets the case-insensitive variable table for this run.
    /// </summary>
    /// <remarks>
    /// The dictionary cannot be modified through this view, although each
    /// <see cref="SqlCmdVariable"/> reflects subsequent permitted assignments.
    /// </remarks>
    public IReadOnlyDictionary<string, SqlCmdVariable> Variables => _variables;

    

    /// <summary>Applies a value parsed from a <c>:setvar</c> command.</summary>
    /// <param name="name">The variable name without reference delimiters.</param>
    /// <param name="value">The parsed replacement value.</param>
    /// <remarks>
    /// Empty or whitespace names are ignored. Assignments to protected
    /// <see cref="SqlCmdMode.SqlCmdEx"/> programmatic variables are also ignored.
    /// Other assignments add or replace variables case-insensitively.
    /// </remarks>
    public void SetVariableFromScript(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_variables.TryGetValue(name, out var existing))
        {
            if (!existing.CanBeOverridden)
            {
                _options.Logger?.Log(LogLevel.Trace, "Setting variable '{name}' ignored.", name);
                return; // silently ignore per SqlCmd behavior
            }
        }

        _variables[name] = new SqlCmdVariable(
            name: name,
            value: value,
            canBeOverridden: true
        );
        _options.Logger?.Log(LogLevel.Trace, "Variable '{name}' updated.", name);
    }

    /// <summary>Looks up a variable value by case-insensitive name.</summary>
    /// <param name="name">The name without reference delimiters.</param>
    /// <returns>The current value, or <see langword="null"/> when undefined.</returns>
    public string? GetVariableValue(string name)
    {
        return _variables.TryGetValue(name, out var variable)
            ? variable.Value
            : null;
    }

    /// <summary>Expands sqlcmd variable references in text.</summary>
    /// <param name="input">The source text to process.</param>
    /// <returns>
    /// Expanded text, or the original text in <see cref="SqlCmdMode.Normal"/>.
    /// Undefined references remain literal.
    /// </returns>
    public string ExpandVariables(string input)
    {
        if (Mode == SqlCmdMode.Normal || string.IsNullOrEmpty(input))
            return input;

        return VariableExpansionHelper.Expand(input, GetVariableValue);
    }

    private static List<ColumnInfo> GetColumnInfo(SqlDataReader reader)
    {
        var schemaTable = reader.GetSchemaTable();
        var columns = new List<ColumnInfo>();

        if (schemaTable == null)
            return columns;

        foreach (DataRow row in schemaTable.Rows)
        {
            var precisionRaw = row["NumericPrecision"];
            int? precision = precisionRaw is DBNull ? null : Convert.ToInt32(precisionRaw);

            columns.Add(new ColumnInfo
            {
                Name = row["ColumnName"] as string ?? "",
                ClrType = row["DataType"] as Type ?? typeof(object),
                SqlTypeName = row["DataTypeName"] as string ?? "",
                Precision = precision,
                Scale = row["NumericScale"] as int?,
                Length = row["ColumnSize"] as int?,
                IsNullable = row["AllowDBNull"] as bool?
            });
        }

        return columns;
    }


    /// <summary>
    /// Executes one batch iteration and materializes each result set for the
    /// configured result-set callback.
    /// </summary>
    /// <param name="batch">The batch whose SQL text is submitted.</param>
    /// <param name="batchNumber">The one-based logical batch number.</param>
    /// <param name="executionIndex">The one-based repeat iteration.</param>
    /// <param name="cancellationToken">A token passed to provider operations.</param>
    /// <returns>Zero after execution, including for an empty batch.</returns>
    /// <remarks>
    /// A null or whitespace-only batch is skipped. The supplied connection must
    /// already be open. Result sets are fully read before callbacks are invoked.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> is cancelled during provider work.
    /// </exception>
    /// <exception cref="SqlException">SQL Server rejects or fails the batch.</exception>
    public async Task<int> ExecuteBatchAsync(
        SqlBatch batch,
        int batchNumber,
        int executionIndex,
        CancellationToken cancellationToken)
    {
        if (batch is null || string.IsNullOrWhiteSpace(batch.Text))
            return 0;

        await using var command = _connection.CreateCommand();
        command.CommandText = batch.Text;
        command.CommandType = CommandType.Text;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        int resultSetIndex = 0;

        do
        {
            resultSetIndex++;

            var result = new ResultSetInfo
            {
                BatchNumber = batchNumber,
                ExecutionIndex = executionIndex,
                ResultSetIndex = resultSetIndex,
                Columns = GetColumnInfo(reader)
            };

            while (await reader.ReadAsync(cancellationToken))
            {
                var values = new object?[reader.FieldCount];
                reader.GetValues(values);
                result.Rows.Add(values);
            }

            _options.OnResultSet?.Invoke(result);
        }
        while (await reader.NextResultAsync(cancellationToken));

        return 0;
    }

}
