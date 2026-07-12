using ItTiger.TigerQuery.Engine;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Cancellation-token propagation tests at the seams that need no SQL Server: the
/// Ctrl+C scope the query commands wrap around engine execution, and the engine's own
/// token observation. A true end-to-end cancellation of a running batch requires a live
/// server (the token is only observed between/inside real batch executions), so that
/// integration scenario is deliberately excluded from the default suite.
/// </summary>
public sealed class CancellationTests
{
    [Fact]
    public void ConsoleCancellationScope_RequestCancellation_CancelsToken()
    {
        using var scope = new ConsoleCancellationScope();

        Assert.True(scope.Token.CanBeCanceled);
        Assert.False(scope.Token.IsCancellationRequested);

        scope.RequestCancellation();

        Assert.True(scope.Token.IsCancellationRequested);
    }

    [Fact]
    public void ConsoleCancellationScope_RequestAfterDispose_IsANoOp()
    {
        var scope = new ConsoleCancellationScope();
        scope.Dispose();

        // A Ctrl+C racing disposal must not throw or affect later scopes.
        scope.RequestCancellation();

        using var next = new ConsoleCancellationScope();
        Assert.False(next.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Engine_RunFromString_ObservesPreCancelledToken()
    {
        var engine = new TigerQueryEngine(new TigerQueryEngineOptions
        {
            // Never reached: the pre-cancelled token stops the run at connection open.
            ConnectionString = "Server=localhost;Integrated Security=true;Encrypt=false;Connect Timeout=1"
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunFromStringAsync("SELECT 1", cts.Token));
    }
}
