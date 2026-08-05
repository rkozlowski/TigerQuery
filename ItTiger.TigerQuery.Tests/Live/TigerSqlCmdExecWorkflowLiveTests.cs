using ItTiger.TigerQuery.Tests.Cli;
using ItTiger.TigerSqlCmd;
using System.Text.RegularExpressions;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// The documented end-to-end workflow for <c>exec</c>, run against a real SQL Server through
/// the shipped executable: <c>e2e create</c>, hand the generated connection to an external
/// tool through <c>exec</c>, run SQL against that same connection, then <c>e2e cleanup</c>.
/// </summary>
/// <remarks>
/// <para>
/// What the live server adds over the unconfigured <c>exec</c> tests is the only thing they
/// cannot cover: that the connection string <c>exec</c> hands over is the real, fully
/// resolved one for a database that actually exists — external references included — rather
/// than merely a correctly substituted string.
/// </para>
/// <para>
/// The external tool is <c>tq-test-child</c>, which reports what it was given and exits with
/// a requested code. It never opens a SQL connection, because <c>exec</c> never does either;
/// the SQL step is <c>tiger-sqlcmd run</c> against the same saved connection.
/// </para>
/// <para>
/// This uses the environment-selected store the rest of the live suite uses, so it runs the
/// documented workflow rather than a private arrangement. Cleanup is guaranteed and is keyed
/// to this run's own session GUID.
/// </para>
/// </remarks>
[Collection(LiveTestCollection.Name)]
public sealed class TigerSqlCmdExecWorkflowLiveTests : IDisposable
{
    private const string EnvironmentVariable = "TQ_EXEC_LIVE_CONNECTION";

    /// <summary>An exit code no TigerSqlCmd outcome uses, so pass-through is unambiguous.</summary>
    private const int ChildExitCode = 42;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "TigerSqlCmdExecWorkflowLiveTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecHandsTheSessionConnectionToAnExternalToolAndTheSessionCleansUp()
    {
        var configuration = SqlServerTestEnvironment.RequireConfiguration(
            requireDatabaseCreation: true);
        var storePath = configuration.Store.FilePath;
        var sessionId = Guid.NewGuid().ToString("D");
        Directory.CreateDirectory(_directory);

        // A SQL-authentication profile carries a password; an integrated one has none to leak.
        var password = configuration.Profile.BuildConnectionStringBuilder().Password;

        // 1. e2e create.
        var create = await RunAsync(
            storePath,
            "e2e", "create", "--session-id", sessionId, "--name-part", "exec-live");
        AssertSuccess(create, "e2e create");

        var databaseName = Captured(create.StdOut, @"Created E2E database (\S+?)\.");
        var connectionName = Captured(create.StdOut, @"Created E2E connection (\S+?)\.");

        try
        {
            // 2. Hand the generated connection to an external tool through exec. The child
            //    echoes the variable, so this asserts on the real resolved connection string.
            var deploy = await RunAsync(
                storePath,
                "exec", "--connection", connectionName,
                "--connection-string-env", EnvironmentVariable,
                "--", TigerSqlCmdTestChild.ExecutablePath,
                "--echo-env", EnvironmentVariable,
                "--exit", ChildExitCode.ToString());

            Assert.Equal(ChildExitCode, deploy.ExitCode);
            var handedOver = Assert.Contains(
                EnvironmentVariable, TigerSqlCmdTestChild.EchoedEnvironment(deploy.StdOut));
            // The generated database, resolved from the saved profile and its references.
            Assert.Contains(databaseName, handedOver, StringComparison.Ordinal);
            // TigerSqlCmd contributed nothing of its own: the value appears once, in the
            // child's echo, and standard error is untouched.
            Assert.Equal(1, Occurrences(deploy.StdOut, handedOver));
            Assert.Empty(deploy.StdErr);

            // 3. A run that does not echo must show no trace of the value anywhere.
            var quiet = await RunAsync(
                storePath,
                "exec", "--connection", connectionName,
                "--connection-string-env", EnvironmentVariable,
                "--", TigerSqlCmdTestChild.ExecutablePath, "--exit", "0");

            Assert.Equal(0, quiet.ExitCode);
            var quietOutput = quiet.StdOut + quiet.StdErr;
            Assert.DoesNotContain(handedOver, quietOutput, StringComparison.Ordinal);
            if (!string.IsNullOrEmpty(password))
                Assert.DoesNotContain(password, quietOutput, StringComparison.Ordinal);

            // 4. TigerSqlCmd SQL against the very same saved connection.
            var sql = await RunAsync(
                storePath,
                "run", "--connection", connectionName,
                "--query", "SELECT DB_NAME() AS DatabaseName;",
                "--mode", "SqlCmdEx");
            AssertSuccess(sql, "run");
            Assert.Contains(databaseName, sql.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            // 5. e2e cleanup, for this run's session only.
            var cleanup = await RunAsync(
                storePath, "e2e", "cleanup", "--session-id", sessionId);
            AssertSuccess(cleanup, "e2e cleanup");
            Assert.Contains(connectionName, cleanup.StdOut, StringComparison.Ordinal);
        }
    }

    private Task<TigerSqlCmdProcessResult> RunAsync(string storePath, params string[] arguments)
    {
        string[] tail = ["--non-interactive", "--no-color", "--tq-connection-store-file", storePath];

        return TigerSqlCmdProcessRunner.RunAsync(
            // The child inherits this process's environment, which is what supplies the
            // external references the configured bootstrap resolves through.
            new Dictionary<string, string?> { [EnvironmentVariable] = null },
            _directory,
            [.. arguments, .. tail]);
    }

    private static void AssertSuccess(TigerSqlCmdProcessResult result, string step) =>
        Assert.True(
            result.ExitCode == (int)TigerSqlCmdExitCode.Ok,
            $"'{step}' exited with {result.ExitCode}.{Environment.NewLine}"
                + result.StdErr + Environment.NewLine + result.StdOut);

    private static string Captured(string output, string pattern)
    {
        var match = Regex.Match(output, pattern);
        Assert.True(match.Success, $"'{pattern}' did not match:{Environment.NewLine}{output}");
        return match.Groups[1].Value;
    }

    private static int Occurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

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
}
