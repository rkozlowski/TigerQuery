using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;

namespace ItTiger.TigerQuery.Tests.Engine;

/// <summary>
/// Locks the engine half of the SQL batch command timeout: its default, the values it
/// accepts, and the point at which it rejects one.
/// </summary>
/// <remarks>
/// The value that actually reaches
/// <see cref="Microsoft.Data.SqlClient.SqlCommand.CommandTimeout"/> is proven against a
/// real server in <c>TigerSqlCmdCommandTimeoutLiveTests</c>; nothing short of a real
/// provider can observe it. What is provable without SQL Server is that a run either
/// carries the option unchanged or refuses to start, which is what these tests pin.
/// </remarks>
public sealed class CommandTimeoutTests
{
    /// <summary>
    /// Omitting the option must stay indistinguishable from the behavior before it existed:
    /// the engine sets nothing, so the provider's own 30-second default applies.
    /// </summary>
    [Fact]
    public void TheDefaultIsUnsetSoTheProviderDefaultApplies()
    {
        Assert.Null(new TigerQueryEngineOptions().CommandTimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(600)]
    [InlineData(int.MaxValue)]
    public async Task ANonNegativeTimeoutIsAcceptedAndTheRunProceeds(int seconds)
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var engine = probe.CreateEngine(new TigerQueryEngineOptions
        {
            CommandTimeoutSeconds = seconds
        });

        var result = await engine.RunFromStringAsync(
            "SELECT 1;\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(1, probe.ExecutionCount);
    }

    /// <summary>
    /// A negative value is a caller mistake, and it is refused before the run can open a
    /// connection or create an output file — not discovered on the first batch.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-30)]
    [InlineData(int.MinValue)]
    public async Task ANegativeTimeoutIsRejectedBeforeAnythingIsOpened(int seconds)
    {
        var events = new List<string>();
        var probe = new EngineProbe(events);
        var engine = probe.CreateEngine(new TigerQueryEngineOptions
        {
            CommandTimeoutSeconds = seconds
        });

        var failure = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => engine.RunFromStringAsync("SELECT 1;", TestContext.Current.CancellationToken));

        Assert.Contains("0 (no limit)", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, probe.OpenCount);
        Assert.Equal(0, probe.ExecutionCount);
        Assert.Empty(events);
    }

    /// <summary>File execution reaches the same guard: it is not an inline-only check.</summary>
    [Fact]
    public async Task ANegativeTimeoutIsRejectedOnTheFileExecutionPathToo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tq-command-timeout-{Guid.NewGuid():N}.sql");
        await File.WriteAllTextAsync(path, "SELECT 1;\r\nGO\r\n", TestContext.Current.CancellationToken);

        try
        {
            var probe = new EngineProbe([]);
            var engine = probe.CreateEngine(new TigerQueryEngineOptions
            {
                CommandTimeoutSeconds = -1
            });

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => engine.RunFromFileAsync(path, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(0, probe.OpenCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Both parser modes execute through the same batch path, so the timeout is not a
    /// mode-specific behavior.
    /// </summary>
    [Theory]
    [InlineData(SqlCmdMode.SqlCmd)]
    [InlineData(SqlCmdMode.SqlCmdEx)]
    [InlineData(SqlCmdMode.Normal)]
    public async Task TheTimeoutIsCarriedIndependentlyOfTheParserMode(SqlCmdMode mode)
    {
        var probe = new EngineProbe([]);
        var engine = probe.CreateEngine(new TigerQueryEngineOptions
        {
            Mode = mode,
            CommandTimeoutSeconds = 45
        });

        var result = await engine.RunFromStringAsync(
            "SELECT 1;\r\nGO\r\nSELECT 2;\r\nGO\r\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(2, probe.ExecutionCount);
    }
}
