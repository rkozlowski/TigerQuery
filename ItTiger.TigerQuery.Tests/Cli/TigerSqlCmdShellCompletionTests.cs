using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Shell completion for the tiger-sqlcmd composition: activation scripts for every supported
/// shell, representative candidates from the real command tree, the host exit-code contract
/// for completion usage errors, and the guarantee that a Tab request never touches the
/// connection store.
/// </summary>
/// <remarks>
/// The completion wire format is TigerCli's internal contract with its own generated scripts,
/// so these tests assert on representative candidates rather than whole responses.
/// </remarks>
[Collection(TigerCliAppCollection.Name)]
public sealed class TigerSqlCmdShellCompletionTests : IDisposable
{
    private const string RequestToken = "--tigercli-complete";

    private readonly TempConnectionStore _temp = new();

    public void Dispose() => _temp.Dispose();

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args)
        => CliTestRunner.RunAsync(_temp.Store, args);

    /// <summary>Requests Bash candidates for a command line; each word carries the protocol's <c>_</c> prefix.</summary>
    private async Task<(string[] Candidates, string Fallback)> CompleteAsync(params string[] words)
    {
        var result = await RunAsync([RequestToken, "bash", .. words.Select(word => "_" + word)]);

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Empty(result.StdErr);
        var lines = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        return (lines[..^1], lines[^1]);
    }

    // ── Activation scripts ───────────────────────────────────────────

    [Theory]
    [InlineData("bash", "complete -F")]
    [InlineData("zsh", "#compdef tiger-sqlcmd")]
    [InlineData("fish", "complete -c 'tiger-sqlcmd'")]
    [InlineData("pwsh", "Register-ArgumentCompleter -Native")]
    [InlineData("powershell", "Register-ArgumentCompleter -Native")]
    public async Task CompletionOption_PrintsActivationScriptForTheApplicationName(string shell, string registration)
    {
        var result = await RunAsync("--completion", shell);

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains(registration, result.StdOut);
        Assert.Contains("tiger-sqlcmd", result.StdOut);
        Assert.Contains(RequestToken, result.StdOut);
        Assert.Empty(result.StdErr);
    }

    [Theory]
    [InlineData("--completion", "nope")]
    [InlineData("--completion")]
    [InlineData("run", "--completion", "bash")]
    public async Task CompletionOption_UsageErrors_UseTheHostInvalidArgumentsCode(params string[] args)
    {
        var result = await RunAsync(args);

        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, result.ExitCode);
    }

    [Fact]
    public async Task Help_ListsCompletionOption()
    {
        var result = await RunAsync("--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("--completion", result.StdOut);
    }

    // ── Candidates ───────────────────────────────────────────────────

    [Fact]
    public async Task Root_OffersTopLevelCommandsAndGroups()
    {
        var (candidates, _) = await CompleteAsync("");

        Assert.Contains("run", candidates);
        Assert.Contains("exec", candidates);
        Assert.Contains("connection", candidates);
        Assert.Contains("e2e", candidates);
    }

    [Fact]
    public async Task Root_OptionWord_OffersDefaultCommandAndRootOptions()
    {
        var (candidates, _) = await CompleteAsync("-");

        Assert.Contains("--connection", candidates);
        Assert.Contains("--query", candidates);
        Assert.Contains("--tq-connection-store-file", candidates);
        Assert.Contains("--version", candidates);
        Assert.Contains("--completion", candidates);
    }

    [Fact]
    public async Task ConnectionGroup_OffersItsCommands()
    {
        var (candidates, _) = await CompleteAsync("connection", "");

        Assert.Contains("list", candidates);
        Assert.Contains("add", candidates);
        Assert.Contains("add-e2e-bootstrap", candidates);
    }

    [Fact]
    public async Task RunMode_OffersEngineModes()
    {
        var (candidates, _) = await CompleteAsync("run", "--mode", "");

        Assert.Equal(["Normal", "SqlCmd", "SqlCmdEx"], candidates);
    }

    [Fact]
    public async Task ExecRawTail_IsLeftToTheShellsFileCompletion()
    {
        var (candidates, fallback) = await CompleteAsync("exec", "-c", "prod", "--", "-");

        Assert.Empty(candidates);
        Assert.Equal(":files", fallback);
    }

    [Fact]
    public async Task ConnectionValue_OffersNoSavedNames_AndNeverCreatesTheStore()
    {
        var store = Path.Combine(
            Path.GetTempPath(), "TigerQueryCliTests", Guid.NewGuid().ToString("N"), "connections.json");

        var (candidates, fallback) = await CompleteAsync(
            "run", "--tq-connection-store-file", store, "-c", "");

        Assert.Empty(candidates);
        Assert.Equal(":none", fallback);
        Assert.False(File.Exists(store));
        Assert.False(File.Exists(_temp.FilePath));
    }
}
