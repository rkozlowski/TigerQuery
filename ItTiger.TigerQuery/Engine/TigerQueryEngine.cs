using ItTiger.TigerQuery.Events;
using ItTiger.TigerQuery.Output;
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
/// stops the run without starting any later batch. The effective policy decides
/// whether later batches run; it does not decide the outcome. Any failed attempt
/// makes the run's <see cref="ExecutionResult.ResultCode"/> something other than
/// <see cref="ExecutionResultCode.Success"/>, and a later successful batch never
/// clears an earlier failure.
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

    // Non-null only while a run owns output destinations. Provider message events are
    // delivered synchronously, so the router has to be reachable from the connection
    // handler as well as from the coordinator.
    private OutputRouter? _activeRouter;

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
        LogAndRaise(message, isException: false, MessageOrigin.ServerDiagnostic);
    }

    private void LogAndRaise(SqlCmdMessage msg, bool isException, MessageOrigin origin)
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

        // Logging above is deliberately independent of routing: redirecting a message
        // to a file must never suppress the configured logger.
        if (_activeRouter is null)
        {
            _options.OnMessage?.Invoke(msg, isException);
            return;
        }

        _activeRouter.RouteMessage(msg, isException, origin);
    }

    internal static IAsyncEnumerable<ExecutionStep> ReadStreamingStepsAsync(
        SqlCmdParser parser,
        CancellationToken cancellationToken = default)
    {
        return parser.ReadExecutionStepsAsync(cancellationToken);
    }

    internal static async Task<PreparedExecutionPlan> PrepareExecutionPlanAsync(
        SqlCmdParser parser,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<ExecutionStep>();
        long totalExecutionCount = 0;

        await foreach (var step in ReadStreamingStepsAsync(parser, cancellationToken))
        {
            steps.Add(step);

            // Route directives are ordered work, but they are not batches and must not
            // change the totals reported through ExecutionPlanReady.
            if (step is ExecuteBatchStep batchStep)
            {
                totalExecutionCount += Math.Max(0L, batchStep.Execution.Batch.ExecCount);
            }
        }

        return new PreparedExecutionPlan(
            [.. steps],
            totalExecutionCount);
    }

    private static async IAsyncEnumerable<ExecutionStep> ReadPreparedStepsAsync(
        PreparedExecutionPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return step;
        }
    }

    private async Task<ExecutionResult> ExecuteStepsAsync(
        IAsyncEnumerable<ExecutionStep> executionSteps,
        QueryExecutionContext context,
        OutputRouter router,
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

        await foreach (var step in executionSteps.WithCancellation(cancellationToken))
        {
            if (step is not ExecuteBatchStep batchStep)
            {
                try
                {
                    ApplyRouteStep(step, router);
                }
                catch (OutputRoutingException routeFailure)
                {
                    // A route change that fails outside a batch stops before the next
                    // batch and uses the same output-failure classification.
                    _options.Logger?.LogError(
                        routeFailure,
                        "The output route could not be changed to {Path}.",
                        routeFailure.Path);
                    ex = routeFailure;
                    resultCode = ExecutionResultCode.OutputFailed;
                    stop = true;
                    break;
                }

                continue;
            }

            var executionBatch = batchStep.Execution;
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
                bool sqlCompleted = false;
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
                    sqlCompleted = true;

                    // Message text is flushed at batch boundaries rather than per
                    // message. Provider message events are synchronous, so a write
                    // failure raised inside one was captured by the router and is
                    // rethrown here, at a safe coordinator boundary.
                    router.FlushAtBatchBoundary();
                    router.ThrowIfFailed();

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
                catch (OutputRoutingException outputFailure)
                {
                    ReportOutputFailure(outputFailure, sqlCompleted);
                    ex = outputFailure;
                    success = false;
                    failed++;
                    resultCode = ExecutionResultCode.OutputFailed;
                    stop = true;
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
                    LogAndRaise(msg, true, MessageOrigin.EngineException);
                }
                catch (SqlException se)
                {
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

                        LogAndRaise(msg, true, MessageOrigin.ServerDiagnostic);
                    }

                    // An output failure captured while handling a provider message stays
                    // the primary exception; the SQL exception is secondary context.
                    var outputFailure = TakeContemporaneousOutputFailure(router, se);
                    if (outputFailure is not null)
                    {
                        ReportOutputFailure(outputFailure, sqlCompleted);
                        ex = outputFailure;
                        resultCode = ExecutionResultCode.OutputFailed;
                        stop = true;
                    }
                    else
                    {
                        ex = se;
                        if (fatal || !context.ContinueOnError)
                        {
                            stop = true;
                            resultCode = fatal ? ExecutionResultCode.Fatal : ExecutionResultCode.BatchFailed;
                        }
                    }
                }
                catch (Exception e)
                {
                    success = false;
                    failed++;

                    var outputFailure = TakeContemporaneousOutputFailure(router, e);
                    if (outputFailure is not null)
                    {
                        ReportOutputFailure(outputFailure, sqlCompleted);
                        ex = outputFailure;
                        resultCode = ExecutionResultCode.OutputFailed;
                        stop = true;
                    }
                    else
                    {
                        ex = e;
                        var msg = SqlCmdMessage.FromException(e);
                        LogAndRaise(msg, true, MessageOrigin.EngineException);
                        if (e is TigerQueryException || !context.ContinueOnError || !_options.ContinueOnErrorForUnhandledExceptions)
                        {
                            stop = true;
                            resultCode = e is TigerQueryException ? ExecutionResultCode.FatalException : ExecutionResultCode.UnhandledException;
                        }
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

        // A batch that failed under an effective continue-on-error policy did not stop
        // the run, but it is still a failure of the run. The terminal result code is the
        // automation contract, so reaching the end of the script cannot report success
        // and a later successful batch cannot erase an earlier failed one. Fatal,
        // cancellation, and output failures already set a more specific code and keep it.
        if (resultCode == ExecutionResultCode.Success && failed > 0)
            resultCode = ExecutionResultCode.BatchFailed;

        // Every destination is flushed and closed whether the run succeeded or failed.
        // A cleanup failure never replaces an earlier primary cause.
        var completionFailure = router.Complete();
        if (completionFailure is not null)
        {
            if (resultCode == ExecutionResultCode.Success)
            {
                ReportOutputFailure(completionFailure, sqlCompleted: true);
                ex = completionFailure;
                resultCode = ExecutionResultCode.OutputFailed;
            }
            else
            {
                _options.Logger?.LogError(
                    completionFailure,
                    "Completing output for {Path} failed after an earlier failure.",
                    completionFailure.Path);
            }
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

    /// <summary>
    /// Returns an output failure captured while <paramref name="thrown"/> was in
    /// flight, attaching that exception as secondary diagnostic context.
    /// </summary>
    private static OutputRoutingException? TakeContemporaneousOutputFailure(
        OutputRouter router,
        Exception thrown)
    {
        var failure = router.TakePendingFailure();
        if (failure is not null)
        {
            failure.Data[OutputRoutingException.ContemporaneousExceptionDataKey] = thrown;
        }

        return failure;
    }

    /// <summary>
    /// Logs an output failure and raises it as an engine message.
    /// </summary>
    /// <remarks>
    /// The message uses the engine-exception origin, so it reaches the message
    /// callback and the logger but can never enter a routed <c>:Error</c> file.
    /// </remarks>
    private void ReportOutputFailure(OutputRoutingException failure, bool sqlCompleted)
    {
        _options.Logger?.LogError(
            failure,
            sqlCompleted
                ? "SQL execution completed before output failed for {Path}."
                : "Output failed for {Path} while the batch result was being read.",
            failure.Path);

        LogAndRaise(SqlCmdMessage.FromException(failure), true, MessageOrigin.EngineException);
    }

    /// <summary>
    /// Applies one output-route step to the run's routing state.
    /// </summary>
    /// <remarks>
    /// Both execution modes reach this method through the same ordered step stream,
    /// so route transitions occur at the same points relative to batches. Applying a
    /// directive resolves and reserves paths but creates no file.
    /// </remarks>
    /// <exception cref="OutputRoutingException">
    /// The directive path cannot be resolved or collides with another channel.
    /// </exception>
    private void ApplyRouteStep(ExecutionStep step, OutputRouter router)
    {
        var (command, directive) = step switch
        {
            SetOutRouteStep outStep => (":Out", outStep.Directive),
            SetErrorRouteStep errorStep => (":Error", errorStep.Directive),
            _ => throw new InvalidOperationException(
                $"Unsupported TigerQuery execution step: {step.GetType().Name}.")
        };

        _options.Logger?.Log(
            LogLevel.Debug,
            "Applying {Command} at line {Line}, column {Column}.",
            command,
            directive.Line,
            directive.Column);

        if (step is SetOutRouteStep)
        {
            router.ApplyOutDirective(directive.Path);
        }
        else
        {
            router.ApplyErrorDirective(directive.Path);
        }
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
        OutputRoutingConfiguration routingConfiguration,
        CancellationToken cancellationToken)
    {
        // Initial routes are resolved before the connection opens, so a run that
        // cannot route fails without contacting SQL Server.
        using var router = new OutputRouter(_options, routingConfiguration);
        await using var connection = new SqlConnection();
        ConfigureConnection(connection);
        _activeRouter = router;
        try
        {
            await _openConnectionAsync(connection, cancellationToken);
            var context = new QueryExecutionContext(_options, connection)
            {
                OutputRouter = router
            };
            var parser = new SqlCmdParser(input, _options, context);

            return await ExecuteStepsAsync(
                ReadStreamingStepsAsync(parser, cancellationToken),
                context,
                router,
                totalLogicalBatchCount: null,
                totalExecutionCount: null,
                cancellationToken);
        }
        finally
        {
            _activeRouter = null;
        }
    }

    private async Task<ExecutionResult> RunPreparedAsync(
        TextReader input,
        OutputRoutingConfiguration routingConfiguration,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection();
        using var router = new OutputRouter(_options, routingConfiguration);
        var context = new QueryExecutionContext(_options, connection);
        var parser = new SqlCmdParser(input, _options, context);
        var plan = await PrepareExecutionPlanAsync(
            parser,
            cancellationToken);

        // Statically known routing failures are found here, before plan readiness and
        // before the connection opens, so no output file is created for a plan that
        // cannot route.
        OutputRoutePlanValidator.Validate(plan.Steps, _options, routingConfiguration);

        cancellationToken.ThrowIfCancellationRequested();
        _options.OnExecutionPlanReady?.Invoke(new ExecutionPlanReady
        {
            LogicalBatchCount = plan.LogicalBatchCount,
            TotalExecutionCount = plan.TotalExecutionCount
        });
        cancellationToken.ThrowIfCancellationRequested();

        ConfigureConnection(connection);
        cancellationToken.ThrowIfCancellationRequested();
        _activeRouter = router;
        try
        {
            await _openConnectionAsync(connection, cancellationToken);
            context.OutputRouter = router;

            return await ExecuteStepsAsync(
                ReadPreparedStepsAsync(plan, cancellationToken),
                context,
                router,
                plan.LogicalBatchCount,
                plan.TotalExecutionCount,
                cancellationToken);
        }
        finally
        {
            _activeRouter = null;
        }
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

        // Routing configuration is validated first: encoding, base directory, and enum
        // values must be usable before parsing, connection opening, or file creation.
        var routingConfiguration = OutputRoutingConfiguration.Create(_options.OutputRouting);

        return _options.ExecutionMode switch
        {
            TigerQueryExecutionMode.Streaming =>
                await RunStreamingAsync(input, routingConfiguration, cancellationToken),
            TigerQueryExecutionMode.Prepared =>
                await RunPreparedAsync(input, routingConfiguration, cancellationToken),
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
