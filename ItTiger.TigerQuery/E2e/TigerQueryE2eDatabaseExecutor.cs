using ItTiger.TigerQuery.Engine;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.E2e;

internal sealed class TigerQueryE2eDatabaseExecutor : ISqlServerE2eDatabaseExecutor
{
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
