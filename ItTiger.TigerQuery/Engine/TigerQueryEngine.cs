using ItTiger.TigerQuery.Events;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ItTiger.TigerQuery.Engine;

/// <summary>
/// Parses sqlcmd-style scripts and executes their logical batches sequentially
/// against SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// The engine has no console output. Messages, result sets, and batch lifecycle
/// information are delivered through <see cref="TigerQueryEngineOptions"/> callbacks.
/// A single engine should not be used for concurrent runs because its options and
/// callback destinations are shared.
/// </para>
/// <para>
/// Callbacks are synchronous. In prepared mode the order is plan-ready, then
/// batch-start, any messages/result sets, and batch-end for each scheduled
/// execution. Streaming mode omits plan-ready and preserves the same per-batch order.
/// </para>
/// <para>
/// Both modes share one batch coordinator, so error accounting, the effective
/// <c>:ON ERROR</c> policy, and the batch lifecycle behave identically. A batch
/// attempt fails when an exception is caught for it or when SQL Server reports an
/// error of severity 11 or higher during it — including the severity 11-16 user
/// errors the provider delivers as informational messages instead of throwing. A
/// failing attempt increments the failed count, ends with an unsuccessful batch-end
/// carrying a diagnostic, and, unless the effective policy is continue-on-error,
/// stops the run without starting any later batch.
/// </para>
/// </remarks>
public sealed class TigerQueryEngine
{
    private readonly TigerQueryEngineOptions _options;
    private readonly Func<SqlConnection, CancellationToken, Task> _openConnectionAsync;
    private readonly Func<QueryExecutionContext, SqlBatch, int, int, CancellationToken, Task> _executeBatchAsync;

    // Non-null only between the start and end of one batch execution attempt, which is
    // what confines server diagnostics to the batch that produced them.
    private BatchDiagnostics? _activeBatchDiagnostics;

    /// <summary>Initializes an engine with fixed run options.</summary>
    /// <param name="options">The parsing, execution, and callback configuration.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
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
                ReportServerMessage(SqlCmdMessage.FromSqlError(error));
            }
        };

        // Keeps a recoverable server error from aborting the reader, so the remaining
        // diagnostics of a batch are delivered in order. The consequence is that SQL
        // Server user errors (severity 11-16, what RAISERROR and THROW normally
        // produce) arrive as events instead of as a SqlException, which is why the
        // coordinator attributes them to the active batch below rather than treating
        // a normal return as success.
        connection.FireInfoMessageEventOnUserErrors = true;
    }

    /// <summary>
    /// Routes one provider-delivered server message to the active batch and to the
    /// message callback.
    /// </summary>
    internal void ReportServerMessage(SqlCmdMessage message)
    {
        _activeBatchDiagnostics?.RecordDelivered(message);
        LogAndRaise(message);
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
        int? totalLogicalBatchCount,
        long? totalExecutionCount,
        CancellationToken cancellationToken)
    {
        var batchIndex = 0;
        long overallExecutionNumber = 0;

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
                overallExecutionNumber = checked(overallExecutionNumber + 1);

                _options.OnBatchStart?.Invoke(new BatchStart
                {
                    BatchNumber = batchIndex,
                    TotalLogicalBatchCount = totalLogicalBatchCount,
                    ExecutionIndex = executionIndex,
                    ExecutionCount = executionCount,
                    OverallExecutionNumber = overallExecutionNumber,
                    TotalExecutionCount = totalExecutionCount,
                    SqlText = batch.Text
                });

                var sw = Stopwatch.StartNew();

                bool success = true;
                var diagnostics = new BatchDiagnostics();
                _activeBatchDiagnostics = diagnostics;

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

                    // The provider can complete a batch normally after having reported a
                    // server error as an informational message. Such an attempt did not
                    // succeed, and the effective :on error policy has to apply to it
                    // exactly as it does to a thrown SqlException.
                    if (diagnostics.HasError)
                    {
                        success = false;
                        failed++;
                        ex = new SqlBatchErrorException(diagnostics.Errors);

                        if (diagnostics.HasFatalError || !context.ContinueOnError)
                        {
                            stop = true;
                            resultCode = diagnostics.HasFatalError
                                ? ExecutionResultCode.Fatal
                                : ExecutionResultCode.BatchFailed;
                        }
                    }
                    else
                    {
                        executed++;
                    }
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

                        // A diagnostic already delivered as an informational message for
                        // this attempt is not published a second time.
                        if (diagnostics.WasDelivered(msg))
                            continue;

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
                finally
                {
                    _activeBatchDiagnostics = null;
                }

                _options.OnBatchEnd?.Invoke(new BatchEnd
                {
                    BatchNumber = batchIndex,
                    TotalLogicalBatchCount = totalLogicalBatchCount,
                    ExecutionIndex = executionIndex,
                    ExecutionCount = executionCount,
                    OverallExecutionNumber = overallExecutionNumber,
                    TotalExecutionCount = totalExecutionCount,
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
            totalLogicalBatchCount: null,
            totalExecutionCount: null,
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
            plan.LogicalBatchCount,
            plan.TotalExecutionCount,
            cancellationToken);
    }

    /// <summary>
    /// Runs SQL read from <paramref name="input"/> using the configured
    /// <see cref="TigerQueryEngineOptions.ExecutionMode"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Streaming mode opens the SQL connection before parsing batches
    /// incrementally, so earlier batches can execute before a later parser error.
    /// Prepared mode parses the complete TigerQuery/sqlcmd structure before opening
    /// the connection; parser failure therefore prevents SQL execution. Prepared
    /// mode retains every expanded logical batch for the duration of the run.
    /// </para>
    /// <para>
    /// Preparation does not validate T-SQL. Connection, permission, T-SQL syntax,
    /// compilation, and runtime failures remain execution-time behavior. Parser
    /// and connection-opening exceptions escape this method.
    /// </para>
    /// <para>
    /// Cancellation observed while an active provider batch operation is in the
    /// engine's batch catch path produces <see cref="ExecutionResultCode.UserCancelled"/>.
    /// Cancellation during parsing, preparation, connection opening, between
    /// executions, or in callbacks is propagated as <see cref="OperationCanceledException"/>.
    /// </para>
    /// </remarks>
    /// <param name="input">The script reader. The engine does not dispose it.</param>
    /// <param name="cancellationToken">A token observed during parsing and execution.</param>
    /// <returns>A result when execution reaches the coordinator's normal result path.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="input"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="TigerQueryException">TigerQuery/sqlcmd structure is malformed.</exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation is observed outside an active batch provider operation.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The configured <see cref="TigerQueryEngineOptions.ExecutionMode"/> is unsupported.
    /// </exception>
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
    /// Opens and runs a script file using the configured execution mode.
    /// </summary>
    /// <param name="path">The script file path.</param>
    /// <param name="encoding">
    /// The fallback encoding when no byte-order mark is detected; UTF-8 by default.
    /// </param>
    /// <param name="cancellationToken">A token observed during parsing and execution.</param>
    /// <returns>A result when execution reaches the coordinator's normal result path.</returns>
    /// <remarks>
    /// Byte-order marks override <paramref name="encoding"/>. The file reader is
    /// disposed after the run.
    /// </remarks>
    /// <exception cref="TigerQueryException">TigerQuery/sqlcmd structure is malformed.</exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation is observed outside an active batch provider operation.
    /// </exception>
    public async Task<ExecutionResult> RunFromFileAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(path, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await RunAsync(reader, cancellationToken);
    }

    /// <summary>
    /// Runs a script string using the configured execution mode.
    /// </summary>
    /// <param name="script">The complete script text.</param>
    /// <param name="cancellationToken">A token observed during parsing and execution.</param>
    /// <returns>A result when execution reaches the coordinator's normal result path.</returns>
    /// <exception cref="TigerQueryException">TigerQuery/sqlcmd structure is malformed.</exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation is observed outside an active batch provider operation.
    /// </exception>
    public async Task<ExecutionResult> RunFromStringAsync(string script, CancellationToken cancellationToken = default)
    {
        using var reader = new StringReader(script);
        return await RunAsync(reader, cancellationToken);
    }
}
