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

}
