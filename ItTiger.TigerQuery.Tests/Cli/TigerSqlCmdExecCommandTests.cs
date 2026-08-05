using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Command-level tests for <c>exec</c> that need no child process: registration and help,
/// every way the handoff configuration can be wrong, and the connection-resolution
/// contract it shares with the other TigerSqlCmd commands.
/// </summary>
/// <remarks>
/// Each of these runs must fail before <see cref="TigerSqlCmdChildProcess"/> is reached, so
/// they can name a child executable that does not exist without ever starting one.
/// </remarks>
[Collection(TigerCliAppCollection.Name)]
public sealed class TigerSqlCmdExecCommandTests : IDisposable
{
    private const string Placeholder = TigerSqlCmdExecPlan.ConnectionStringPlaceholder;
    private const string NeverStarted = "tq-exec-never-started";

    private readonly TempConnectionStore _default = new();
    private readonly TempConnectionStore _alternate = new();

    public void Dispose()
    {
        _default.Dispose();
        _alternate.Dispose();
    }

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args)
        => CliTestRunner.RunAsync(_default.Store, args);

    private Task<(int ExitCode, string StdOut, string StdErr)> AddConnectionAsync(string name)
        => RunAsync("connection", "add", name, "--non-interactive", "--server", "sql01", "--database", "AppDb");

    // ── Registration and help ────────────────────────────────────────

    [Fact]
    public async Task RootHelp_ListsExec()
    {
        var result = await RunAsync("--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("exec", result.StdOut);
    }

    [Fact]
    public async Task ExecHelp_ExplainsBothHandoffsAndTheirTradeOff()
    {
        var result = await RunAsync("exec", "--help");
        var help = result.StdOut;

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("tiger-sqlcmd exec", help);
        // The child is started directly; no shell is implied.
        Assert.Contains("directly", help);
        Assert.Contains("no shell", help, StringComparison.OrdinalIgnoreCase);
        // Both handoff methods, and that at least one is required.
        Assert.Contains(Placeholder, help);
        Assert.Contains("--connection-string-env", help);
        Assert.Contains("At least one handoff is required", help);
        // The credential-exposure trade-off and the recommendation.
        Assert.Contains("list processes", help);
        Assert.Contains("Prefer the environment variable", help);
        // A verified example of each mode.
        Assert.Contains("--connection-string-env DB_CONNECTION -- my-tool --report", help);
        Assert.Contains($"-- my-tool --target={Placeholder} --report", help);
    }

    [Fact]
    public async Task HelpErrors_DocumentsTheChildStartFailureCode()
    {
        var result = await RunAsync("--help-errors");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("Child process start failed", result.StdOut);
        Assert.Equal(21, (int)TigerSqlCmdExitCode.ChildProcessStartFailed);
    }

    // ── Missing or invalid handoff configuration ─────────────────────

    [Fact]
    public async Task MissingConnection_FailsTheSameWayEveryOtherCommandDoes()
    {
        var result = await RunAsync(
            "exec", "--non-interactive", "--connection-string-env", "DB", "--", NeverStarted);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task MissingSeparator_ReportsInvalidArguments()
    {
        await AddConnectionAsync("local");

        var result = await RunAsync(
            "exec", "-c", "local", "--non-interactive", "--connection-string-env", "DB");

        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("'--'", result.StdErr);
    }

    [Fact]
    public async Task MissingExecutableAfterSeparator_ReportsInvalidArguments()
    {
        await AddConnectionAsync("local");

        var result = await RunAsync(
            "exec", "-c", "local", "--non-interactive", "--connection-string-env", "DB", "--");

        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("No child executable", result.StdErr);
    }

    [Fact]
    public async Task MissingHandoffMode_ReportsInvalidArguments()
    {
        await AddConnectionAsync("local");

        var result = await RunAsync(
            "exec", "-c", "local", "--non-interactive", "--", NeverStarted, "--flag");

        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("No connection-string handoff", result.StdErr);
    }

    [Fact]
    public async Task InvalidEnvironmentVariableName_ReportsInvalidArguments()
    {
        await AddConnectionAsync("local");

        var result = await RunAsync(
            "exec", "-c", "local", "--non-interactive",
            "--connection-string-env", "DB-CONNECTION",
            "--", NeverStarted);

        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("not a valid environment-variable name", result.StdErr);
    }

    [Fact]
    public async Task InvalidHandoffConfiguration_IsRejectedBeforeTheConnectionIsResolved()
    {
        // No saved connection exists at all, and the store file was never created. A run that
        // reached resolution would report ConnectionFailed instead of InvalidArguments.
        var result = await RunAsync(
            "exec", "-c", "does-not-exist", "--non-interactive", "--", NeverStarted);

        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("No connection-string handoff", result.StdErr);
        Assert.False(File.Exists(_default.FilePath));
    }

    // ── Connection resolution ────────────────────────────────────────

    [Fact]
    public async Task UnknownConnection_FailsWithoutStartingAChild()
    {
        var result = await RunAsync(
            "exec", "-c", "does-not-exist", "--non-interactive",
            "--connection-string-env", "DB",
            "--", NeverStarted);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("does-not-exist", result.StdErr);
    }

    [Fact]
    public async Task UnresolvableExternalReference_FailsAndRedactsTheReferencedValue()
    {
        const string secret = "exec-external-secret-must-not-appear";

        _default.Store.Add(new SqlServerConnectionProfile
        {
            Name = "external",
            ConnectionStringValue = SqlServerConnectionValue.External(
                new SqlServerExternalValueReference
                {
                    Source = SqlServerExternalValueSource.EnvironmentVariable,
                    Name = "TQ_EXEC_MISSING_CONNECTION_STRING"
                })
        });

        var result = await RunAsync(
            "exec", "-c", "external", "--non-interactive",
            "--connection-string-env", "DB",
            "--", NeverStarted);

        // Lazily resolved at use, exactly as `run` resolves it, and failing cleanly.
        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("external values", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, result.StdOut + result.StdErr, StringComparison.Ordinal);
        // The reference itself stays unresolved in the store; nothing is written back.
        Assert.Contains("TQ_EXEC_MISSING_CONNECTION_STRING", File.ReadAllText(_default.FilePath));
    }

    [Fact]
    public async Task NonInteractive_MissingConnection_FailsInsteadOfPrompting()
    {
        await AddConnectionAsync("local");

        var result = await CliTestRunner.RunAsync(
            _default.Store,
            host => host.WithPromptTimeout(TimeSpan.FromMilliseconds(50)),
            "exec", "--non-interactive", "--connection-string-env", "DB", "--", NeverStarted);

        // Non-interactive: a validation failure, not a prompt that timed out (which would map
        // through Cancelled).
        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.NotEqual((int)TigerSqlCmdExitCode.Cancelled, result.ExitCode);
    }

    // ── Store selection ──────────────────────────────────────────────

    [Fact]
    public async Task TheCliStoreOptionSelectsTheStoreExecResolvesAgainst()
    {
        await AddConnectionAsync("in-default");

        // The alternate store is empty, so the provider offers no choices and validation is
        // skipped; the failure comes from exec's own resolver reading the selected store.
        var result = await RunAsync(
            "exec", "-c", "in-default", "--non-interactive",
            "--connection-string-env", "DB",
            "--tq-connection-store-file", _alternate.FilePath,
            "--", NeverStarted);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("in-default", result.StdErr);
    }

    [Fact]
    public async Task TheStoreEnvironmentVariableSelectsTheStoreExecResolvesAgainst()
    {
        await AddConnectionAsync("in-default");

        var result = await CliTestRunner.RunWithEnvironmentAsync(
            _default.Store,
            name => name == SqlServerConnectionStoreEnvironment.ConnectionStoreFile
                ? _alternate.FilePath
                : null,
            "exec", "-c", "in-default", "--non-interactive",
            "--connection-string-env", "DB",
            "--", NeverStarted);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("in-default", result.StdErr);
    }

    [Fact]
    public async Task TheCliStoreOptionOutranksTheEnvironmentVariable()
    {
        using var third = new TempConnectionStore();
        await AddConnectionAsync("in-default");

        // The environment names the alternate store; the option names an empty third one.
        // Both are empty, so what this pins is that the option is the store that was read:
        // the profile saved in the application default is invisible either way.
        var result = await CliTestRunner.RunWithEnvironmentAsync(
            _default.Store,
            name => name == SqlServerConnectionStoreEnvironment.ConnectionStoreFile
                ? _alternate.FilePath
                : null,
            "exec", "-c", "in-default", "--non-interactive",
            "--connection-string-env", "DB",
            "--tq-connection-store-file", third.FilePath,
            "--", NeverStarted);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.False(File.Exists(third.FilePath));
        Assert.False(File.Exists(_alternate.FilePath));
    }

    [Fact]
    public async Task AnUnconfiguredStoreIsNeverCreatedByExec()
    {
        var result = await RunAsync(
            "exec", "-c", "anything", "--non-interactive",
            "--connection-string-env", "DB",
            "--", NeverStarted);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.False(File.Exists(_default.FilePath));
        Assert.False(File.Exists(_alternate.FilePath));
    }
}
