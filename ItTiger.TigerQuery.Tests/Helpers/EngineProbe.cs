using ItTiger.TigerQuery.Engine;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Helpers;

/// <summary>
/// Drives <see cref="TigerQueryEngine"/> without SQL Server by recording the
/// connection-opening and batch-execution seams as ordered event strings.
/// </summary>
internal sealed class EngineProbe
{
    private readonly List<string> _events;
    private readonly Func<QueryExecutionContext, SqlBatch, int, int, CancellationToken, Task> _execute;

    public EngineProbe(
        List<string> events,
        Func<QueryExecutionContext, SqlBatch, int, int, CancellationToken, Task>? execute = null)
    {
        _events = events;
        _execute = execute ?? ExecuteAsync;
    }

    public int OpenCount { get; private set; }

    public int ExecutionCount { get; private set; }

    public TigerQueryEngine CreateEngine(TigerQueryEngineOptions options)
    {
        return new TigerQueryEngine(options, OpenAsync, ExecuteAndCountAsync);
    }

    private Task OpenAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenCount++;
        _events.Add("open");
        return Task.CompletedTask;
    }

    private async Task ExecuteAndCountAsync(
        QueryExecutionContext context,
        SqlBatch batch,
        int batchNumber,
        int executionIndex,
        CancellationToken cancellationToken)
    {
        ExecutionCount++;
        await _execute(
            context,
            batch,
            batchNumber,
            executionIndex,
            cancellationToken);
    }

    private Task ExecuteAsync(
        QueryExecutionContext context,
        SqlBatch batch,
        int batchNumber,
        int executionIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Add($"execute:{batchNumber}:{executionIndex}");
        return Task.CompletedTask;
    }
}
