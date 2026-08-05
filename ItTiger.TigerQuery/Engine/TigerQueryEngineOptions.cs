using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ItTiger.TigerQuery.Events;
using Microsoft.Extensions.Logging;

namespace ItTiger.TigerQuery.Engine;

/// <summary>
/// Configures parsing, SQL execution, variables, logging, and synchronous callbacks
/// for a <see cref="TigerQueryEngine"/>.
/// </summary>
/// <remarks>
/// An engine reads these options throughout a run. Callback delegates are invoked
/// synchronously on the execution path and should not throw.
/// </remarks>
public sealed class TigerQueryEngineOptions
{

    /// <summary>Gets the SQL Server connection string used for execution.</summary>
    /// <remarks>
    /// Parser-only consumers may leave this empty. Run methods pass it to
    /// <see cref="Microsoft.Data.SqlClient.SqlConnection"/> before opening.
    /// </remarks>
    public string ConnectionString { get; init; } = String.Empty;

    /// <summary>
    /// Gets whether parsing is interleaved with SQL execution or completed
    /// before the SQL connection is opened.
    /// </summary>
    /// <remarks>
    /// <see cref="TigerQueryExecutionMode.Streaming"/> is the default and retains
    /// only the current logical batch. <see cref="TigerQueryExecutionMode.Prepared"/>
    /// retains every expanded logical batch until execution completes.
    /// </remarks>
    public TigerQueryExecutionMode ExecutionMode { get; init; }
        = TigerQueryExecutionMode.Streaming;

    /// <summary>
    /// Gets the parser mode that controls sqlcmd directives and variable support.
    /// </summary>
    public SqlCmdMode Mode { get; init; } = SqlCmdMode.SqlCmd;

    /// <summary>
    /// Gets programmatic variables available before script parsing begins.
    /// </summary>
    /// <remarks>
    /// Names are matched case-insensitively. In <see cref="SqlCmdMode.SqlCmd"/>,
    /// these values seed the variable table and <c>:setvar</c> may replace them.
    /// In <see cref="SqlCmdMode.SqlCmdEx"/>, they take precedence and assignments
    /// to matching names are ignored; non-conflicting script-local variables can
    /// still be created and updated. Programmatic variables are ignored in
    /// <see cref="SqlCmdMode.Normal"/>.
    /// </remarks>
    public IDictionary<string, string>? Variables { get; init; }

    /// <summary>
    /// Gets a reserved transaction preference.
    /// </summary>
    /// <remarks>
    /// The current engine does not create or manage a transaction from this option.
    /// It is retained for API compatibility.
    /// </remarks>
    public bool EnableTransaction { get; init; } = false;

    /// <summary>
    /// Gets the initial policy for continuing after a failed batch execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:ON ERROR IGNORE</c> and <c>:ON ERROR EXIT</c> update this policy for
    /// subsequent parser-produced batches. Fatal SQL errors always stop execution.
    /// </para>
    /// <para>
    /// A batch fails when SQL Server reports an error of severity 11 or higher for
    /// it, whether the provider throws or reports the error as an informational
    /// message. Under an effective exit-on-error policy the triggering batch ends
    /// unsuccessfully and no further batch is started. Under an effective continue
    /// policy the batch still counts towards <see cref="ExecutionResult.FailedBatches"/>
    /// and the next scheduled batch runs.
    /// </para>
    /// <para>
    /// This policy controls how much of the script runs, not what the run reports.
    /// Either way the run's <see cref="ExecutionResult.ResultCode"/> is not
    /// <see cref="ExecutionResultCode.Success"/> once any batch attempt has failed.
    /// </para>
    /// </remarks>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>
    /// Gets whether non-SQL exceptions caught during batch execution may be ignored
    /// when the effective continue-on-error policy is enabled.
    /// </summary>
    /// <remarks>
    /// The default is false. <see cref="TigerQueryException"/> and exceptions raised
    /// outside the batch execution catch path are never made continuable by this option.
    /// </remarks>
    public bool ContinueOnErrorForUnhandledExceptions { get; init; } = false;

    /// <summary>Gets the optional destination for structured engine diagnostics.</summary>
    /// <remarks>The engine does not dispose the logger.</remarks>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Gets the script-directed output routing and file-output configuration.
    /// </summary>
    /// <remarks>
    /// The default routes every channel to the callbacks below and creates no files.
    /// Invalid routing configuration fails at run start, before parsing, connection
    /// opening, or output-file creation.
    /// </remarks>
    public OutputRoutingOptions OutputRouting { get; init; } = new();

    /// <summary>
    /// Gets a callback for SQL Server messages and exceptions observed during execution.
    /// </summary>
    /// <remarks>
    /// The Boolean argument is true when the message was raised while handling an
    /// exception. Server messages delivered by the provider use false, including
    /// error diagnostics, which the provider reports that way for severities 11
    /// through 16. The callback can occur between matching batch-start and batch-end
    /// callbacks. A diagnostic that the provider delivers both as a message and on a
    /// thrown exception for the same attempt is raised once.
    /// </remarks>
    public Action<SqlCmdMessage, bool>? OnMessage { get; init; }

    /// <summary>Gets a callback for each fully materialized SQL result set.</summary>
    /// <remarks>
    /// The callback runs before the matching batch-end callback. Each payload and
    /// its row arrays are newly allocated for that result set and are not reused or
    /// mutated by the engine after delivery, so callers may retain them.
    /// </remarks>
    public Action<ResultSetInfo>? OnResultSet { get; init; }

    /// <summary>Gets a callback invoked immediately before each batch execution attempt.</summary>
    /// <remarks>
    /// Repeated batches raise this callback once per positive repeat iteration.
    /// It is not raised for zero or negative repeat counts.
    /// </remarks>
    public Action<BatchStart>? OnBatchStart { get; init; }

    /// <summary>Gets a callback invoked after each started batch execution attempt.</summary>
    /// <remarks>
    /// SQL messages and result-set callbacks may occur after batch start and before
    /// batch end. Callback exceptions are not a supported control-flow mechanism and
    /// are not guaranteed to be converted into an <see cref="ExecutionResult"/>.
    /// </remarks>
    public Action<BatchEnd>? OnBatchEnd { get; init; }

    /// <summary>
    /// Gets a callback invoked once in prepared mode after the complete TigerQuery/sqlcmd structure
    /// has been parsed successfully and before the SQL connection is opened.
    /// </summary>
    /// <remarks>
    /// This callback is not raised in streaming mode or when preparation fails or
    /// is cancelled. An empty prepared script raises it with zero counts. Cancellation
    /// is checked immediately before and after invocation; if the callback cancels
    /// the supplied run token, no connection or batch callback follows.
    /// </remarks>
    public Action<ExecutionPlanReady>? OnExecutionPlanReady { get; init; }

}
