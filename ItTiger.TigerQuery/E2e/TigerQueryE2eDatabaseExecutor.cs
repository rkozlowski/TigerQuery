using ItTiger.TigerQuery.Engine;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ItTiger.TigerQuery.E2e;

internal sealed class TigerQueryE2eDatabaseExecutor : ISqlServerE2eDatabaseExecutor
{
    /// <summary>The length of SQL Server's <c>sysname</c>, which every object name fits.</summary>
    private const int SysnameLength = 128;

    public void ClearPool(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        SqlConnection.ClearPool(connection);
    }

    public Task ExecuteAsync(
        string connectionString,
        string script,
        CancellationToken cancellationToken) =>
        RunAsync(connectionString, script, names: null, cancellationToken);

    public async Task ExecuteAsync(
        string connectionString,
        string script,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // The engine has no parameter model, and this path is used for scripts whose
        // statements must not be separable, so the command is issued directly. A SQL
        // failure surfaces as the SqlException the callers already translate.
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = script;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(
                new SqlParameter(name, SqlDbType.NVarChar, SysnameLength) { Value = value });
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> QueryNamesAsync(
        string connectionString,
        string script,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await RunAsync(connectionString, script, names, cancellationToken);
        return names;
    }

    private static async Task RunAsync(
        string connectionString,
        string script,
        List<string>? names,
        CancellationToken cancellationToken)
    {
        var engine = new TigerQueryEngine(new TigerQueryEngineOptions
        {
            ConnectionString = connectionString,
            ExecutionMode = TigerQueryExecutionMode.Prepared,
            Mode = SqlCmdMode.Normal,
            ContinueOnError = false,
            OnResultSet = names is null
                ? null
                : resultSet =>
                {
                    foreach (var row in resultSet.Rows)
                    {
                        if (row.Length > 0 && row[0] is string name)
                            names.Add(name);
                    }
                }
        });

        var result = await engine.RunFromStringAsync(script, cancellationToken);
        if (result.ResultCode != ExecutionResultCode.Success || result.FailedBatches != 0)
        {
            throw new InvalidOperationException(
                $"SQL lifecycle execution failed with result '{result.ResultCode}'.",
                result.Exception);
        }
    }
}
