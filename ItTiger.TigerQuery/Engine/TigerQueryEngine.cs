using ItTiger.TigerQuery.Events;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using System.Threading;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace ItTiger.TigerQuery.Engine;

public sealed class TigerQueryEngine
{
    private readonly TigerQueryEngineOptions _options;

    public TigerQueryEngine(TigerQueryEngineOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_options.ConnectionString);
        connection.InfoMessage += (s, e) =>
        {
            foreach (SqlError error in e.Errors)
            {
                var msg = SqlCmdMessage.FromSqlError(error);

                LogAndRaise(msg);
            }
        };

        connection.FireInfoMessageEventOnUserErrors = true;
        await connection.OpenAsync(cancellationToken);
        return connection;
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

    private async Task<ExecutionResult> ExecuteBatchesAsync(SqlCmdParser parser, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        var batchIndex = 0;

        var totalSw = Stopwatch.StartNew();
        Exception? ex = null;
        ExecutionResultCode resultCode = ExecutionResultCode.Success;
        int executed = 0;
        int failed = 0;
        bool stop = false;

        await foreach (var batch in parser.ReadBatchesAsync(cancellationToken))
        {
            batchIndex++;
            for (int i = 1; i <= batch.ExecCount; i++)
            {
                ex = null;
                cancellationToken.ThrowIfCancellationRequested();

                _options.OnBatchStart?.Invoke(new BatchStart
                {
                    BatchNumber = batchIndex,
                    ExecutionIndex = i,
                    ExecutionCount = batch.ExecCount,
                    SqlText = batch.Text
                });

                var sw = Stopwatch.StartNew();
                
                bool success = true;

                try
                {
                    _options.Logger?.LogInformation("Executing batch {Batch} ({Index}/{Count})", batchIndex, i, batch.ExecCount);
                    await context.ExecuteBatchAsync(batch, batchIndex, i, cancellationToken);
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
                    ExecutionIndex = i,
                    ExecutionCount = batch.ExecCount,
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


    public async Task<ExecutionResult> RunAsync(TextReader input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var context = new QueryExecutionContext(_options, connection);
        var parser = new SqlCmdParser(input, _options, context);
        
        return await ExecuteBatchesAsync(parser, context, cancellationToken);        
    }

    public async Task<ExecutionResult> RunFromFileAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(path, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await RunAsync(reader, cancellationToken);
    }

    public async Task<ExecutionResult> RunFromStringAsync(string script, CancellationToken cancellationToken = default)
    {
        using var reader = new StringReader(script);
        return await RunAsync(reader, cancellationToken);
    }
}
