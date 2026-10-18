using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd;
using ItTiger.TigerSqlCmd.Logging;
using Microsoft.Extensions.Logging;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Failure paths keep the diagnosis and drop the secret: each run carries synthetic
/// sensitive values in the profile, the SQLCMD variables, and the child handoff, and the
/// console, the log file, and the store are checked for both.
/// </summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class TigerSqlCmdSensitiveDiagnosticsTests : IDisposable
{
    private const string SyntheticPassword = "synthetic-sql-password-91d3";
    private const string SyntheticVariableValue = "synthetic-variable-value-5a07";
    private const string ForeignCiphertext = "Zm9yZWlnbi1kcGFwaS1ibG9iLWZyb20tYW5vdGhlci11c2Vy";

    // Nothing listens on port 1, so the provider fails fast with its own network error.
    private const string RefusedServer = "tcp:127.0.0.1,1";

    private readonly TempConnectionStore _temp = new();
    private readonly string _logPath = Path.Combine(
        Path.GetTempPath(), "TigerQueryCliTests", $"{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        _temp.Dispose();
        // Closes the file target the run configured, so the log can be deleted.
        NLog.LogManager.Configuration = null;
        try
        {
            File.Delete(_logPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a unique leftover temp file is harmless.
        }
    }

    [Fact]
    public async Task UnreachableServer_ReportsStageAndSqlErrorWithoutTheConnectionSecrets()
    {
        _temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "refused",
            Server = RefusedServer,
            Authentication = AuthenticationType.SqlPassword,
            Username = "synthetic-login",
            PasswordValue = SqlServerConnectionValue.Literal(SyntheticPassword),
            Encrypt = EncryptOption.Optional,
            ConnectTimeout = 5
        });

        var result = await CliTestRunner.RunAsync(
            _temp.Store,
            "run", "--non-interactive",
            "-c", "refused",
            "-q", "SELECT N'$(Probe)';",
            "-v", $"Probe={SyntheticVariableValue}",
            "--log-file", _logPath,
            "--log-level", "Trace",
            "--verbosity", "VeryVerbose");
        var log = ReadLog();
        var console = result.StdOut + result.StdErr;

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("Could not connect to SQL Server using saved connection 'refused':", result.StdErr);
        Assert.Matches(@"\(SQL error -?\d+, state \d+, class \d+\)", result.StdErr);
        Assert.Contains("for saved connection refused: SQL error", log);
        // The exception is recorded by its type and message, not serialized raw.
        Assert.Contains("Microsoft.Data.SqlClient.SqlException: ", log);
        Assert.DoesNotContain("   at ", log);

        foreach (var output in new[] { console, log })
        {
            Assert.DoesNotContain(SyntheticPassword, output);
            Assert.DoesNotContain("Password=", output, StringComparison.OrdinalIgnoreCase);
        }

        // The log file never records SQLCMD variable values or query text; verbose console
        // output does, because the user asked for it.
        Assert.DoesNotContain(SyntheticVariableValue, log);
        Assert.DoesNotContain("SELECT N'", log);
    }

    [Fact]
    public void LogFile_RecordsTheExceptionChainByTypeAndMessageOnly()
    {
        using var loggerFactory = NLogSetup.CreateLoggerFactory(_logPath, LogLevel.Trace);
        var logger = loggerFactory.CreateLogger("TigerSqlCmd");
        try
        {
            throw new InvalidOperationException(
                "The output file could not be replaced.",
                new SelfDescribingException("Access to the path is denied."));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Writing {Path} failed.", "out.csv");
        }

        var log = ReadLog();

        Assert.Contains("Writing out.csv failed.", log);
        Assert.Contains(
            "System.InvalidOperationException: The output file could not be replaced. ---> "
                + $"{typeof(SelfDescribingException).FullName}: Access to the path is denied.",
            log);
        Assert.DoesNotContain(SelfDescribingException.OwnState, log);
        Assert.DoesNotContain("   at ", log);
    }

    [Fact]
    public async Task UndecryptablePassword_FailsBeforeConnectingAndNamesTheCause()
    {
        WriteCopiedStore();

        var result = await CliTestRunner.RunAsync(
            _temp.Store,
            "run", "--non-interactive",
            "-c", "copied",
            "-q", "SELECT 1;",
            "--log-file", _logPath,
            "--log-level", "Trace");
        var log = ReadLog();

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("could not be decrypted here", result.StdErr);
        Assert.DoesNotContain("Could not connect to SQL Server", result.StdErr);
        Assert.Contains("could not be decrypted here", log);
        Assert.DoesNotContain(ForeignCiphertext, result.StdOut + result.StdErr + log);
    }

    [Fact]
    public async Task UndecryptablePassword_ExecReportsItWithoutStartingTheChild()
    {
        WriteCopiedStore();

        var result = await CliTestRunner.RunAsync(
            _temp.Store,
            "exec", "--non-interactive",
            "-c", "copied",
            "--connection-string-env", "DB_CONNECTION",
            "--", "tiger-sqlcmd-test-child-that-does-not-exist", "--target={connection-string}");

        // 21 would mean a start was attempted; 4 means resolution stopped it first.
        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("could not be decrypted here", result.StdErr);
        Assert.DoesNotContain(ForeignCiphertext, result.StdOut + result.StdErr);
    }

    [Fact]
    public async Task TypedPassword_StoreThatCannotKeepIt_IsRefusedAndNothingIsSaved()
    {
        using var nonPersisting = new NonPersistingStore();
        var context = new SqlServerConnectionCommandContext(
            () => nonPersisting.Store,
            SqlServerConnectionValidationPolicy.DatabaseOptional);

        var result = await new AddSqlServerConnectionCommand(context)
            .ExecuteAsync(new AddSqlServerConnectionSettings
            {
                Name = "linux-login",
                Server = "srv",
                Authentication = AuthenticationType.SqlPassword,
                Username = "synthetic-login",
                Password = SyntheticPassword
            });

        Assert.Equal(TigerCliExitKind.ValidationError, result);
        Assert.Null(nonPersisting.Store.Find("linux-login"));
        Assert.False(File.Exists(nonPersisting.FilePath));
    }

    [Fact]
    public async Task TypedPasswordOnEdit_StoreThatCannotKeepIt_LeavesTheProfileUnchanged()
    {
        using var nonPersisting = new NonPersistingStore();
        nonPersisting.Store.Add(new SqlServerConnectionProfile { Name = "app", Server = "srv" });
        var before = File.ReadAllText(nonPersisting.FilePath);
        var context = new SqlServerConnectionCommandContext(
            () => nonPersisting.Store,
            SqlServerConnectionValidationPolicy.DatabaseOptional);

        var result = await new EditSqlServerConnectionCommand(context)
            .ExecuteAsync(new SqlServerConnectionSettings
            {
                Name = "app",
                Server = "srv",
                Authentication = AuthenticationType.SqlPassword,
                Username = "synthetic-login",
                Password = SyntheticPassword
            });

        Assert.Equal(TigerCliExitKind.ValidationError, result);
        Assert.Equal(before, File.ReadAllText(nonPersisting.FilePath));
        Assert.Equal(AuthenticationType.Integrated, nonPersisting.Store.Find("app")!.Authentication);
    }

    [Fact]
    public async Task TypedPassword_StoreThatProtectsIt_IsSavedWithoutPlainText()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var context = new SqlServerConnectionCommandContext(
            () => _temp.Store,
            SqlServerConnectionValidationPolicy.DatabaseOptional);

        var result = await new AddSqlServerConnectionCommand(context)
            .ExecuteAsync(new AddSqlServerConnectionSettings
            {
                Name = "windows-login",
                Server = "srv",
                Authentication = AuthenticationType.SqlPassword,
                Username = "synthetic-login",
                Password = SyntheticPassword
            });

        Assert.Equal(TigerCliExitKind.Success, result);
        Assert.Equal(PasswordEncryptionType.DPAPI, _temp.Store.Find("windows-login")!.PasswordEncryption);
        Assert.DoesNotContain(SyntheticPassword, File.ReadAllText(_temp.FilePath));
    }

    // The run's file target keeps the log open for writing; closing it is what makes the
    // file complete and readable.
    private string ReadLog()
    {
        NLog.LogManager.Configuration = null;
        return File.ReadAllText(_logPath);
    }

    private void WriteCopiedStore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_temp.FilePath)!);
        File.WriteAllText(_temp.FilePath, $$"""
            [
              {
                "Name": "copied",
                "Server": "{{RefusedServer}}",
                "Authentication": 1,
                "Username": "synthetic-login",
                "EncryptedPassword": "{{ForeignCiphertext}}",
                "PasswordEncryption": 1,
                "Encrypt": 0
              }
            ]
            """);
    }

    /// <summary>An exception whose own <see cref="ToString"/> shows state its message does not.</summary>
    private sealed class SelfDescribingException(string message) : Exception(message)
    {
        public const string OwnState = "self-described-state-0c4e";

        public override string ToString() => $"{base.ToString()} {OwnState}";
    }

    /// <summary>A temp store with the protector the non-Windows platforms default to.</summary>
    private sealed class NonPersistingStore : IDisposable
    {
        public NonPersistingStore()
        {
            FilePath = Path.Combine(
                Path.GetTempPath(), "TigerQueryCliTests", $"{Guid.NewGuid():N}.json");
            Store = new SqlServerConnectionStore(
                new SqlServerConnectionStoreOptions { FilePath = FilePath },
                new NonPersistingConnectionPasswordProtector());
        }

        public string FilePath { get; }

        public SqlServerConnectionStore Store { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(FilePath);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a unique leftover temp file is harmless.
            }
        }
    }
}
