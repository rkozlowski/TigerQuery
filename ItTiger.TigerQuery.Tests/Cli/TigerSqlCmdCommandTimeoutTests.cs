using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Command-line contract for <c>run --command-timeout</c>: how it is declared, documented,
/// and validated. None of these runs reaches SQL Server.
/// </summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class TigerSqlCmdCommandTimeoutTests : IDisposable
{
    private readonly TempConnectionStore _store = new();

    public void Dispose() => _store.Dispose();

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args)
        => CliTestRunner.RunAsync(_store.Store, args);

    [Fact]
    public async Task RunHelp_DocumentsTheOptionAndDistinguishesItFromTheConnectionTimeout()
    {
        var result = await RunAsync("run", "--help");
        var help = result.StdOut;

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("--command-timeout", help);
        Assert.Contains("seconds", help);
        Assert.Contains("0 means no limit", help);
        Assert.Contains("not the connection timeout", help);
    }

    /// <summary>The option is a <c>run</c> option; the basic default command has no timeout knob.</summary>
    [Fact]
    public async Task TheOptionIsDeclaredOnRunAndNotOnTheDefaultCommand()
    {
        Assert.Contains("--command-timeout", (await RunAsync("run", "--help")).StdOut);
        Assert.DoesNotContain("--command-timeout", (await RunAsync("--help")).StdOut);
    }

    /// <summary>Omitted means unset, which is what keeps the pre-existing default in force.</summary>
    [Fact]
    public void OmittingTheOptionLeavesTheSettingUnset()
    {
        Assert.Null(new TigerSqlCmdSettings().CommandTimeout);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-30")]
    public async Task ANegativeTimeoutIsRejectedWithAnExplanation(string seconds)
    {
        var result = await RunAsync(
            "run", "-c", "local", "-q", "SELECT 1;", "--command-timeout", seconds, "--non-interactive");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains("--command-timeout must be 0 (no limit) or a positive number of seconds", result.StdErr);
        // Rejected as settings validation, before the store was touched at all.
        Assert.False(File.Exists(_store.FilePath));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("3600")]
    public async Task ANonNegativeTimeoutPassesValidationAndReachesConnectionResolution(string seconds)
    {
        // No such saved connection: getting as far as ConnectionFailed proves the value was
        // accepted, since a rejected value fails earlier with a validation error instead.
        var result = await RunAsync(
            "run", "-c", "does-not-exist", "-q", "SELECT 1;",
            "--command-timeout", seconds, "--non-interactive");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("30s")]
    [InlineData("1.5")]
    public async Task ANonIntegerTimeoutIsRejectedAsAUsageError(string seconds)
    {
        var result = await RunAsync(
            "run", "-c", "local", "-q", "SELECT 1;", "--command-timeout", seconds, "--non-interactive");

        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, result.ExitCode);
    }
}
