using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// End-to-end <c>exec</c> tests that actually start a child process.
/// </summary>
/// <remarks>
/// <para>
/// tiger-sqlcmd runs out of process here for two reasons: the child inherits standard
/// output and standard error rather than having them redirected, so only a real parent
/// process with redirected streams can capture what the child wrote; and the process exit
/// code is the thing under test.
/// </para>
/// <para>
/// The child is <c>tq-test-child</c>, a deterministic test asset that echoes its arguments
/// and selected environment variables and exits with a requested code. Nothing here depends
/// on SqlPackage or any other external tool.
/// </para>
/// </remarks>
public sealed class TigerSqlCmdExecProcessTests : IDisposable
{
    private const string Placeholder = TigerSqlCmdExecPlan.ConnectionStringPlaceholder;
    private const string EnvironmentVariable = "TIGER_SQL_CONNECTION_STRING";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "TigerSqlCmdExecProcessTests", Guid.NewGuid().ToString("N"));

    private readonly string _storePath;

    /// <summary>
    /// What the saved profile resolves to, taken from the profile itself rather than
    /// restated, so these tests assert on exec's handoff and not on TigerQuery's
    /// connection-string construction.
    /// </summary>
    private readonly string _expectedConnectionString;

    public TigerSqlCmdExecProcessTests()
    {
        Directory.CreateDirectory(_directory);
        _storePath = Path.Combine(_directory, "connections.json");

        var profile = new SqlServerConnectionProfile
        {
            Name = "local",
            Server = "sql01",
            Database = "AppDb"
        };

        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = _storePath },
            new NoOpConnectionPasswordProtector());
        store.Add(profile);

        _expectedConnectionString = profile.BuildConnectionString();
        Assert.Contains("sql01", _expectedConnectionString);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a unique leftover temp directory is harmless.
        }
    }

    private static string ChildExecutable => TigerSqlCmdTestChild.ExecutablePath;

    private Task<TigerSqlCmdProcessResult> RunExecAsync(params string[] arguments)
    {
        string[] head =
        [
            "exec", "-c", "local", "--non-interactive", "--no-color",
            "--tq-connection-store-file", _storePath
        ];

        return TigerSqlCmdProcessRunner.RunAsync(
            new Dictionary<string, string?>
            {
                // Prove nothing leaks in from the developer's machine or an earlier test.
                [SqlServerConnectionStoreEnvironment.ConnectionStoreFile] = null,
                [EnvironmentVariable] = null
            },
            _directory,
            [.. head, .. arguments]);
    }

    private static IReadOnlyList<string> ChildArguments(string stdout) =>
        TigerSqlCmdTestChild.Arguments(stdout);

    // ── Argument substitution ────────────────────────────────────────

    [Fact]
    public async Task ArgumentSubstitution_ReplacesThePlaceholderInTheChildsRealArgv()
    {
        var result = await RunExecAsync("--", ChildExecutable, Placeholder);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([_expectedConnectionString], ChildArguments(result.StdOut));
    }

    [Fact]
    public async Task ArgumentSubstitution_WorksInsideALargerArgument()
    {
        var result = await RunExecAsync(
            "--", ChildExecutable, $"/TargetConnectionString:{Placeholder}", "/Action:Script");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [$"/TargetConnectionString:{_expectedConnectionString}", "/Action:Script"],
            ChildArguments(result.StdOut));
    }

    [Fact]
    public async Task ArgumentSubstitution_ReplacesEveryOccurrenceInEveryArgument()
    {
        var result = await RunExecAsync(
            "--", ChildExecutable,
            $"/Source:{Placeholder}",
            $"/Target:{Placeholder}",
            $"{Placeholder}|{Placeholder}");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                $"/Source:{_expectedConnectionString}",
                $"/Target:{_expectedConnectionString}",
                $"{_expectedConnectionString}|{_expectedConnectionString}"
            ],
            ChildArguments(result.StdOut));
    }

    [Fact]
    public async Task ArgumentsContainingSpaces_ReachTheChildWithoutBeingReparsed()
    {
        // A shell would split these; ArgumentList round-trips them exactly. The glob, the
        // shell variable, and the Windows variable must all arrive as literal text.
        var result = await RunExecAsync(
            "--", ChildExecutable,
            "/Out:C:\\reports\\my report.sql",
            "two  spaces",
            "$HOME %PATH% *.sql",
            "quote\"inside",
            Placeholder);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                "/Out:C:\\reports\\my report.sql",
                "two  spaces",
                "$HOME %PATH% *.sql",
                "quote\"inside",
                _expectedConnectionString
            ],
            ChildArguments(result.StdOut));
    }

    [Fact]
    public async Task AnExecutablePathContainingSpaces_IsStartedDirectly()
    {
        var spacedDirectory = Path.Combine(_directory, "child tools", "test child");
        Directory.CreateDirectory(spacedDirectory);
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(ChildExecutable)!))
            File.Copy(file, Path.Combine(spacedDirectory, Path.GetFileName(file)));

        var spacedExecutable = Path.Combine(spacedDirectory, Path.GetFileName(ChildExecutable));

        var result = await RunExecAsync("--", spacedExecutable, Placeholder);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([_expectedConnectionString], ChildArguments(result.StdOut));
    }

    // ── Environment-variable handoff ─────────────────────────────────

    [Fact]
    public async Task EnvironmentHandoff_SetsTheVariableForTheChildOnly()
    {
        var result = await RunExecAsync(
            "--connection-string-env", EnvironmentVariable,
            "--", ChildExecutable, "--echo-env", EnvironmentVariable);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"ENV[{EnvironmentVariable}]={_expectedConnectionString}", result.StdOut);
        // The value reached the child through the environment, not through argv.
        Assert.DoesNotContain(_expectedConnectionString, string.Join('\n', ChildArguments(result.StdOut)));
    }

    [Fact]
    public async Task EnvironmentHandoff_InheritsTheRestOfTheParentEnvironment()
    {
        var result = await TigerSqlCmdProcessRunner.RunAsync(
            new Dictionary<string, string?>
            {
                [SqlServerConnectionStoreEnvironment.ConnectionStoreFile] = null,
                ["TQ_EXEC_INHERITED"] = "inherited-value"
            },
            _directory,
            "exec", "-c", "local", "--non-interactive", "--no-color",
            "--tq-connection-store-file", _storePath,
            "--connection-string-env", EnvironmentVariable,
            "--", ChildExecutable,
            "--echo-env", "TQ_EXEC_INHERITED",
            "--echo-env", EnvironmentVariable);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ENV[TQ_EXEC_INHERITED]=inherited-value", result.StdOut);
        Assert.Contains($"ENV[{EnvironmentVariable}]={_expectedConnectionString}", result.StdOut);
    }

    [Fact]
    public async Task EnvironmentHandoff_DoesNotSetTheVariableInAnUnrelatedRun()
    {
        // The same tiger-sqlcmd process, one command later, must not carry the value. Running
        // a second exec without --connection-string-env is the observable form of "the parent
        // process environment was not modified".
        var result = await RunExecAsync("--", ChildExecutable, "--echo-env", EnvironmentVariable, Placeholder);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"ENV[{EnvironmentVariable}]=(unset)", result.StdOut);
    }

    [Fact]
    public async Task BothHandoffs_AreAppliedTogether()
    {
        var result = await RunExecAsync(
            "--connection-string-env", EnvironmentVariable,
            "--", ChildExecutable, $"/Target:{Placeholder}", "--echo-env", EnvironmentVariable);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"ENV[{EnvironmentVariable}]={_expectedConnectionString}", result.StdOut);
        Assert.Contains($"/Target:{_expectedConnectionString}", ChildArguments(result.StdOut));
    }

    // ── Process behavior ─────────────────────────────────────────────

    [Fact]
    public async Task TheChildInheritsStandardOutputAndStandardError()
    {
        var result = await RunExecAsync(
            "--", ChildExecutable, "--stderr", "child-diagnostic-line", Placeholder);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ARGC=", result.StdOut);
        Assert.Contains("child-diagnostic-line", result.StdErr);
        // Nothing of TigerSqlCmd's own is mixed into the child's streams on success.
        Assert.DoesNotContain("Child command line:", result.StdOut + result.StdErr);
    }

    [Fact]
    public async Task TheChildInheritsTheCallersWorkingDirectory()
    {
        var result = await RunExecAsync("--", ChildExecutable, Placeholder);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"CWD={_directory}", result.StdOut);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    [InlineData(200)]
    public async Task TheChildsExitCodeIsReturnedUnchanged(int exitCode)
    {
        var result = await RunExecAsync(
            "--", ChildExecutable, "--exit", exitCode.ToString(), Placeholder);

        Assert.Equal(exitCode, result.ExitCode);
    }

    [Fact]
    public async Task AnExecutableThatCannotBeFound_ReportsTheDedicatedStartFailureCode()
    {
        var missing = Path.Combine(_directory, "tq-exec-no-such-tool");

        var result = await RunExecAsync("--", missing, $"/Target:{Placeholder}");

        Assert.Equal((int)TigerSqlCmdExitCode.ChildProcessStartFailed, result.ExitCode);
        Assert.Contains("Could not start the child executable", result.StdErr);
        Assert.Contains(missing, result.StdErr);
    }

    // ── Redaction ────────────────────────────────────────────────────

    [Fact]
    public async Task AStartFailureReportsThePlaceholderAndNeverTheResolvedValue()
    {
        var missing = Path.Combine(_directory, "tq-exec-no-such-tool");

        var result = await RunExecAsync(
            "--connection-string-env", EnvironmentVariable,
            "--", missing, $"/TargetConnectionString:{Placeholder}");
        var output = result.StdOut + result.StdErr;

        Assert.Equal((int)TigerSqlCmdExitCode.ChildProcessStartFailed, result.ExitCode);
        Assert.Contains($"/TargetConnectionString:{Placeholder}", output);
        Assert.DoesNotContain(_expectedConnectionString, output, StringComparison.Ordinal);
        Assert.DoesNotContain("Initial Catalog=AppDb", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASuccessfulRunNeverPrintsTheResolvedConnectionStringItself()
    {
        // The child echoes the value, so the assertion is that nothing beyond the child's own
        // output mentions it: no "running", no resolved command line, no diagnostics.
        var result = await RunExecAsync(
            "--connection-string-env", EnvironmentVariable,
            "--", ChildExecutable, "--echo-env", EnvironmentVariable);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StdErr);
        Assert.Equal(
            1,
            result.StdOut.Split(_expectedConnectionString, StringSplitOptions.None).Length - 1);
    }
}
