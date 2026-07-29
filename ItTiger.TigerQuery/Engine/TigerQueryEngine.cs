using ItTiger.TigerQuery.Events;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ItTiger.TigerQuery.Engine;

public sealed class TigerQueryEngine
{
    private readonly TigerQueryEngineOptions _options;
    private readonly Func<SqlConnection, CancellationToken, Task> _openConnectionAsync;
    private readonly Func<QueryExecutionContext, SqlBatch, int, int, CancellationToken, Task> _executeBatchAsync;

    public TigerQueryEngine(TigerQueryEngineOptions options)
        : this(options, OpenSqlConnectionAsync, ExecuteContextBatchAsync)
    {
    }

    internal TigerQueryEngine(
        TigerQueryEngineOptions options,
        Func<SqlConnection, CancellationToken, Task> openConnectionAsync,
        Func<QueryExecutionContext, SqlBatch, int, int, CancellationToken, Task> executeBatchAsync)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _openConnectionAsync = openConnectionAsync ?? throw new ArgumentNullException(nameof(openConnectionAsync));
        _executeBatchAsync = executeBatchAsync ?? throw new ArgumentNullException(nameof(executeBatchAsync));
    }

    private static async Task OpenSqlConnectionAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken);
    }

    private static async Task ExecuteContextBatchAsync(
        QueryExecutionContext context,
        SqlBatch batch,
        int batchNumber,
        int executionIndex,
        CancellationToken cancellationToken)
    {
        await context.ExecuteBatchAsync(
            batch,
            batchNumber,
            executionIndex,
            cancellationToken);
    }

    private void ConfigureConnection(SqlConnection connection)
    {
        connection.ConnectionString = _options.ConnectionString;
        connection.InfoMessage += (s, e) =>
        {
            foreach (SqlError error in e.Errors)
            {
                var msg = SqlCmdMessage.FromSqlError(error);

                LogAndRaise(msg);
            }
        };

        connection.FireInfoMessageEventOnUserErrors = true;
    }

    private void LogAndRaise(SqlCmdMessage msg, bool isException = false)
    {
        // Logging
        var level = msg.Type switch
        {
            SqlCmdMessageType.Print => LogLevel.Information,
            SqlCmdMessageType.Raiserror => LogLevel.Information,
            SqlCmdMessageType.Warning => LogLevel.Warning,
            SqlCmdMessageType.Exception => LogLevel.Error,
            SqlCmdMessageType.Error => LogLevel.Error,
            SqlCmdMessageType.FatalError => LogLevel.Critical,
            _ => LogLevel.Debug
        };

        if (msg.Severity == SqlCmdMessage.SeverityException)
        {
            _options.Logger?.Log(level, "Exception: {Message}", msg.Text);
        }
        else if (msg.IsError)
        {
            _options.Logger?.Log(level,
                "SQL {Type}: {Message} (Severity {Severity}, State {State}, Number {Number}, Procedure {Procedure})",
                msg.Type, msg.Text, msg.Severity, msg.State, msg.Number, msg.Procedure ?? "-");
        }
        else
        {
            _options.Logger?.Log(level, "SQL {Type}: {Message}", msg.Type, msg.Text);
        }
        _options.OnMessage?.Invoke(msg, isException);        
    }

    internal static async IAsyncEnumerable<ExecutionBatch> ReadStreamingBatchesAsync(
        SqlCmdParser parser,
        QueryExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var batch in parser.ReadBatchesAsync(cancellationToken))
        {
            yield return new ExecutionBatch(batch, context.ContinueOnError);
        }
    }

    internal static async Task<PreparedExecutionPlan> PrepareExecutionPlanAsync(
        SqlCmdParser parser,
        QueryExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var batches = new List<ExecutionBatch>();
        long totalExecutionCount = 0;

        await foreach (var executionBatch in ReadStreamingBatchesAsync(
            parser,
            context,
            cancellationToken))
        {
            batches.Add(executionBatch);
            totalExecutionCount += Math.Max(0L, executionBatch.Batch.ExecCount);
        }

        return new PreparedExecutionPlan(
            [.. batches],
            totalExecutionCount);
    }

    private static async IAsyncEnumerable<ExecutionBatch> ReadPreparedBatchesAsync(
        PreparedExecutionPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        foreach (var executionBatch in plan.Batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return executionBatch;
        }
    }

    private async Task<ExecutionResult> ExecuteBatchesAsync(
        IAsyncEnumerable<ExecutionBatch> executionBatches,
        QueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        var batchIndex = 0;

        var totalSw = Stopwatch.StartNew();
        Exception? ex = null;
        ExecutionResultCode resultCode = ExecutionResultCode.Success;
        int executed = 0;
        int failed = 0;
        bool stop = false;

        await foreach (var executionBatch in executionBatches.WithCancellation(cancellationToken))
        {
            var batch = executionBatch.Batch;
            context.ContinueOnError = executionBatch.ContinueOnError;
            batchIndex++;

            var executionCount = batch.ExecCount;
            for (var index = 0;
                TryGetExecutionIndex(executionCount, index, out var executionIndex);
                index++)
            {
                ex = null;
                cancellationToken.ThrowIfCancellationRequested();

                _options.OnBatchStart?.Invoke(new BatchStart
                {
                    BatchNumber = batchIndex,
                    ExecutionIndex = executionIndex,
                    ExecutionCount = executionCount,
                    SqlText = batch.Text
                });

                var sw = Stopwatch.StartNew();
                
                bool success = true;

                try
                {
                    _options.Logger?.LogInformation(
                        "Executing batch {Batch} ({Index}/{Count})",
                        batchIndex,
                        executionIndex,
                        executionCount);
                    await _executeBatchAsync(
                        context,
                        batch,
                        batchIndex,
                        executionIndex,
                        cancellationToken);
                    executed++;
                }
                catch (OperationCanceledException oce)
                {
                    _options.Logger?.LogWarning("Execution cancelled by user.");
                    ex = oce;
                    resultCode = ExecutionResultCode.UserCancelled;
                    stop = true;
                    success = false;
                    failed++;
                    var msg = SqlCmdMessage.FromException(oce);
                    LogAndRaise(msg, true);
                }
                catch (SqlException se)
                {
                    ex = se;
                    success = false;
                    failed++;

                    bool fatal = false;

                    foreach (SqlError error in se.Errors)
                    {
                        var msg = SqlCmdMessage.FromSqlError(error);
                        if (msg.IsFatalError)
                            fatal = true;
                        LogAndRaise(msg, true);
                    }

                    if (fatal || !context.ContinueOnError)
                    {
                        stop = true;
                        resultCode = fatal ? ExecutionResultCode.Fatal : ExecutionResultCode.BatchFailed;
                    }
                }
                catch (Exception e)
                {
                    success = false;
                    ex = e;
                    failed++;
                    var msg = SqlCmdMessage.FromException(e);
                    LogAndRaise(msg, true);
                    if (e is TigerQueryException || !context.ContinueOnError || !_options.ContinueOnErrorForUnhandledExceptions)
                    {
                        stop = true;
                        resultCode = e is TigerQueryException ? ExecutionResultCode.FatalException : ExecutionResultCode.UnhandledException;
                    }
                }

                _options.OnBatchEnd?.Invoke(new BatchEnd
                {
                    BatchNumber = batchIndex,
                    ExecutionIndex = executionIndex,
                    ExecutionCount = executionCount,
                    Success = success,
                    Exception = ex,
                    Duration = sw.Elapsed
                });
                if (stop)
                    break;
            }
            if (stop)
                break;
        }
        return new ExecutionResult
        {
            ResultCode = resultCode,
            Exception = ex, 
            ExecutedBatches = executed,
            FailedBatches = failed,
            TotalDuration = totalSw.Elapsed
        };
    }

    internal static bool TryGetExecutionIndex(
        int executionCount,
        int zeroBasedIndex,
        out int executionIndex)
    {
        if (zeroBasedIndex < 0 || zeroBasedIndex >= executionCount)
        {
            executionIndex = 0;
            return false;
        }

        executionIndex = zeroBasedIndex + 1;
        return true;
    }

    private async Task<ExecutionResult> RunStreamingAsync(
        TextReader input,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection();
        ConfigureConnection(connection);
        await _openConnectionAsync(connection, cancellationToken);
        var context = new QueryExecutionContext(_options, connection);
        var parser = new SqlCmdParser(input, _options, context);

        return await ExecuteBatchesAsync(
            ReadStreamingBatchesAsync(parser, context, cancellationToken),
            context,
            cancellationToken);
    }

    private async Task<ExecutionResult> RunPreparedAsync(
        TextReader input,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection();
        var context = new QueryExecutionContext(_options, connection);
        var parser = new SqlCmdParser(input, _options, context);
        var plan = await PrepareExecutionPlanAsync(
            parser,
            context,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        _options.OnExecutionPlanReady?.Invoke(new ExecutionPlanReady
        {
            LogicalBatchCount = plan.LogicalBatchCount,
            TotalExecutionCount = plan.TotalExecutionCount
        });
        cancellationToken.ThrowIfCancellationRequested();

        ConfigureConnection(connection);
        cancellationToken.ThrowIfCancellationRequested();
        await _openConnectionAsync(connection, cancellationToken);

        return await ExecuteBatchesAsync(
            ReadPreparedBatchesAsync(plan, cancellationToken),
            context,
            cancellationToken);
    }

    /// <summary>
    /// Runs SQL read from <paramref name="input"/> using the configured
    /// <see cref="TigerQueryEngineOptions.ExecutionMode"/>.
    /// </summary>
    /// <remarks>
    /// Streaming mode opens the SQL connection before parsing batches
    /// incrementally. Prepared mode parses the complete TigerQuery/sqlcmd
    /// structure before opening the connection, but T-SQL validation and all
    /// connection, permission, compilation, and runtime failures still occur
    /// during execution.
    /// </remarks>
    public async Task<ExecutionResult> RunAsync(
        TextReader input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return _options.ExecutionMode switch
        {
            TigerQueryExecutionMode.Streaming =>
                await RunStreamingAsync(input, cancellationToken),
            TigerQueryExecutionMode.Prepared =>
                await RunPreparedAsync(input, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported TigerQuery execution mode: {_options.ExecutionMode}.")
        };
    }

    /// <summary>
    /// Runs a script file using the configured execution mode.
    /// </summary>
    public async Task<ExecutionResult> RunFromFileAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(path, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await RunAsync(reader, cancellationToken);
    }

    /// <summary>
    /// Runs a script string using the configured execution mode.
    /// </summary>
    public async Task<ExecutionResult> RunFromStringAsync(string script, CancellationToken cancellationToken = default)
    {
        using var reader = new StringReader(script);
        return await RunAsync(reader, cancellationToken);
    }
}
