namespace ItTiger.TigerSqlCmd;

/// <summary>
/// Turns Ctrl+C / Ctrl+Break into a <see cref="CancellationToken"/> for the duration of
/// one command execution. TigerCli 0.8 does not hand command handlers a cancellation
/// token, so the query commands own this narrowly scoped bridge: the console event is
/// suppressed (<c>e.Cancel = true</c>) so the engine can observe the token, finish its
/// normal cancellation flow, and report <see cref="ItTiger.TigerQuery.Engine.ExecutionResultCode.UserCancelled"/>.
/// Dispose unhooks the process-global handler so nothing leaks between commands or tests.
/// </summary>
internal sealed class ConsoleCancellationScope : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ConsoleCancelEventHandler _handler;

    public ConsoleCancellationScope()
    {
        _handler = (_, e) =>
        {
            e.Cancel = true;
            RequestCancellation();
        };
        Console.CancelKeyPress += _handler;
    }

    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// Cancels the scope's token. Internal seam for the console handler and for tests,
    /// which cannot raise <see cref="Console.CancelKeyPress"/> directly.
    /// </summary>
    internal void RequestCancellation()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A Ctrl+C racing Dispose is a no-op: the scope's command already finished.
        }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= _handler;
        _cts.Dispose();
    }
}
