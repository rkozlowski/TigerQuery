using ItTiger.TigerQuery.Events;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Engine;

public sealed class QueryExecutionContext
{
    private readonly Dictionary<string, SqlCmdVariable> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly TigerQueryEngineOptions _options;

    private readonly SqlConnection _connection;

    public bool ContinueOnError { get; set; } = true;
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

    public SqlCmdMode Mode => _options.Mode;

    public IReadOnlyDictionary<string, SqlCmdVariable> Variables => _variables;

    

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

    public string? GetVariableValue(string name)
    {
        return _variables.TryGetValue(name, out var variable)
            ? variable.Value
            : null;
    }

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


    public async Task<int> ExecuteBatchAsync(SqlBatch batch, int batchNumber, int executionIndex, CancellationToken cancellationToken)
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
