using ItTiger.TigerQuery.Engine;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Application-level tests for the tiger-sqlcmd composition: metadata-driven help and
/// version output, interaction modes, and the exit-code contract for outcomes that need
/// no SQL Server (usage, validation, prompt cancellation, connection resolution).
/// </summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class TigerSqlCmdAppTests : IDisposable
{
    private readonly TempConnectionStore _temp = new();

    public void Dispose() => _temp.Dispose();

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args)
        => CliTestRunner.RunAsync(_temp.Store, args);

    // ── Metadata and help ────────────────────────────────────────────

    [Fact]
    public async Task Help_ListsTopLevelCommandsAndGroup()
    {
        var result = await RunAsync("--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("tiger-sqlcmd", result.StdOut);
        Assert.Contains("run", result.StdOut);
        Assert.Contains("connections", result.StdOut);
    }

    [Fact]
    public async Task Help_ShowsRepositoryAndDocumentationLinksFromAssemblyMetadata()
    {
        var result = await RunAsync("--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("github.com/rkozlowski/TigerQuery", result.StdOut);
        Assert.Contains("ittiger.net/projects/tigerquery", result.StdOut);
    }

    [Fact]
    public async Task Help_PolishCulture_LocalizesGroupDescription()
    {
        var result = await RunAsync("--culture", "pl-PL", "--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("Zarządzanie zapisanymi połączeniami", result.StdOut);
    }

    [Fact]
    public async Task Version_Reports081()
    {
        var result = await RunAsync("--version");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("0.8.1", result.StdOut);
    }

    [Fact]
    public async Task VersionFull_Reports081WithBuildTimestamp()
    {
        var result = await RunAsync("--version-full");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        // InformationalVersion is Version+UtcBuildTimestamp (see Version.props).
        Assert.Contains("0.8.1+", result.StdOut);
    }

    // ── Interaction modes and framework exit codes ───────────────────

    [Fact]
    public async Task NonInteractive_MissingRequiredConnection_FailsAsValidationWithoutPrompting()
    {
        var result = await RunAsync("--non-interactive", "-q", "SELECT 1");

        // TigerCli reports a required option that could not be prompted for in
        // non-interactive mode through its Validation category.
        Assert.Equal((int)TigerSqlCmdExitCode.ValidationError, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task UnknownOption_FailsAsUsage()
    {
        var result = await RunAsync("--non-interactive", "--bogus-option");

        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task PromptCancellation_MapsToCancelledExitCode()
    {
        await RunAsync("--non-interactive", "connections", "add", "demo", "--server", "srv");

        // Interactive default command with no queued answers: the connection prompt
        // times out, which TigerCli maps through the Cancelled kind.
        var result = await CliTestRunner.RunAsync(
            _temp.Store,
            host => host.WithPromptTimeout(TimeSpan.FromMilliseconds(50)));

        Assert.Equal((int)TigerSqlCmdExitCode.Cancelled, result.ExitCode);
    }

    // ── Connection failures ──────────────────────────────────────────

    [Fact]
    public async Task UnknownSavedConnection_EmptyStore_ReturnsConnectionFailed_NotUnhandledException()
    {
        // With no saved connections the provider has no choices, so provider validation
        // is skipped and the handler's resolver reports the failure cleanly.
        var result = await RunAsync("--non-interactive", "-c", "does-not-exist", "-q", "SELECT 1");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task UnknownSavedConnection_WithSavedConnections_FailsProviderValidation()
    {
        await RunAsync("--non-interactive", "connections", "add", "demo", "--server", "srv");

        // With choices available, TigerCli's provider validation rejects the value
        // before the handler runs ("not an available choice").
        var result = await RunAsync("--non-interactive", "-c", "does-not-exist", "-q", "SELECT 1");

        Assert.Equal((int)TigerSqlCmdExitCode.ValidationError, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    // ── Exit-code contract locks ─────────────────────────────────────

    [Fact]
    public void EngineResultCodes_MapOneToOneOntoExitCodes()
    {
        foreach (var code in Enum.GetValues<ExecutionResultCode>())
            Assert.Equal((int)code, (int)code.ToExitCode());
    }

    [Fact]
    public void FrameworkExitCodes_UseTheDocumentedBand()
    {
        Assert.Equal(20, (int)TigerSqlCmdExitCode.InvalidArguments);
        Assert.Equal(21, (int)TigerSqlCmdExitCode.ValidationError);
        Assert.Equal(3, (int)TigerSqlCmdExitCode.Cancelled);
        Assert.Equal(6, (int)TigerSqlCmdExitCode.UnhandledException);
    }
}
