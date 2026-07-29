using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ItTiger.TigerQuery.Events;
using Microsoft.Extensions.Logging;

namespace ItTiger.TigerQuery.Engine;

public sealed class TigerQueryEngineOptions
{

    public string ConnectionString { get; init; } = String.Empty; // for unit testing the parser

    /// <summary>
    /// Controls whether parsing is interleaved with SQL execution or completed
    /// before the SQL connection is opened.
    /// </summary>
    /// <remarks>
    /// Streaming is the default and retains only the current logical batch.
    /// Prepared mode retains all expanded logical batches until execution
    /// completes.
    /// </remarks>
    public TigerQueryExecutionMode ExecutionMode { get; init; }
        = TigerQueryExecutionMode.Streaming;

    /// <summary>
    /// Input mode: plain SQL or sqlcmd-style with variable support.
    /// </summary>
    public SqlCmdMode Mode { get; init; } = SqlCmdMode.SqlCmd;

    /// <summary>
    /// Custom variables to inject before script execution.
    /// </summary>
    public IDictionary<string, string>? Variables { get; init; }

    /// <summary>
    /// If true, all batches are wrapped in a transaction (except when explicitly overridden).
    /// </summary>
    public bool EnableTransaction { get; init; } = false;

    /// <summary>
    /// If true, continues on error. If false, stops on first failure.
    /// </summary>
    public bool ContinueOnError { get; init; } = true;

    public bool ContinueOnErrorForUnhandledExceptions { get; init; } = false;

    public ILogger? Logger { get; init; }


    public Action<SqlCmdMessage, bool>? OnMessage { get; init; }
    
    public Action<ResultSetInfo>? OnResultSet { get; init; }
    
    public Action<BatchStart>? OnBatchStart { get; init; }
    public Action<BatchEnd>? OnBatchEnd { get; init; }

    /// <summary>
    /// Called once in prepared mode after the complete TigerQuery/sqlcmd structure
    /// has been parsed successfully and before the SQL connection is opened.
    /// </summary>
    /// <remarks>
    /// This callback is not raised in streaming mode or when preparation fails or
    /// is cancelled. An empty prepared script raises it with zero counts.
    /// </remarks>
    public Action<ExecutionPlanReady>? OnExecutionPlanReady { get; init; }

}
