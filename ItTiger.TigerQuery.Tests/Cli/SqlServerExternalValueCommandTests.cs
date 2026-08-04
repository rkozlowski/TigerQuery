using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>Phase 6 command-level coverage for configuring external values safely.</summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class SqlServerExternalValueCommandTests : IDisposable
{
    private const string ServerEnvironment =
        "{\"Source\":\"EnvironmentVariable\",\"Name\":\"TQ_E2E_SERVER\"}";
    private const string UsernameJsonFile =
        "{\"Source\":\"File\",\"Path\":\"/run/secrets/sql-auth.json\",\"Format\":\"Json\",\"Key\":\"username\"}";
    private const string PasswordTextFile =
        "{\"Source\":\"File\",\"Path\":\"/run/secrets/sql-password\",\"Format\":\"Text\"}";
    private const string FullConnectionEnvironment =
        "{\"Source\":\"EnvironmentVariable\",\"Name\":\"TQ_E2E_CONNECTION_STRING\"}";

    private readonly TempConnectionStore temp = new();

    public void Dispose() => temp.Dispose();

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args) =>
        CliTestRunner.RunAsync(temp.Store, args);

    [Fact]
    public async Task BootstrapAddSupportsFullyNonInteractiveSqlAuthenticationReferences()
    {
        var result = await RunAsync(
            "connection", "add-e2e-bootstrap",
            "--non-interactive",
            "--authentication", "SqlPassword",
            "--server-reference", ServerEnvironment,
            "--username-reference", UsernameJsonFile,
            "--password-reference", PasswordTextFile);

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        var profile = temp.Store.Find(TigerSqlCmdApp.DefaultE2eBootstrapConnectionName)!;
        Assert.Equal(SqlServerE2eMetadata.True, profile.Metadata[SqlServerE2eMetadata.Enabled]);
        Assert.Equal("TQ_E2E_SERVER", profile.ServerValue.Reference!.Name);
        Assert.Equal("username", profile.UsernameValue!.Reference!.Key);
        Assert.Equal("/run/secrets/sql-password", profile.PasswordValue!.Reference!.Path);
        Assert.Null(profile.PlainPassword);
        Assert.Null(profile.EncryptedPassword);

        var connection = profile.BuildConnectionStringBuilder(
            new SqlServerExternalValueResolutionOptions
            {
                EnvironmentReader = _ => "sql.example.test",
                FileReader = path => path.EndsWith("sql-auth.json", StringComparison.Ordinal)
                    ? "{\"username\":\"ci-user\"}"
                    : "ci-password"
            });
        Assert.Equal("sql.example.test", connection.DataSource);
        Assert.Equal("ci-user", connection.UserID);
        Assert.Equal("ci-password", connection.Password);

        var persisted = File.ReadAllText(temp.FilePath);
        Assert.DoesNotContain("ci-password", persisted);
        Assert.DoesNotContain("ci-user", persisted);
    }

    [Fact]
    public async Task BootstrapAddSupportsAFullConnectionStringReference()
    {
        var result = await RunAsync(
            "connection", "add-e2e-bootstrap",
            "--name", "full-bootstrap",
            "--non-interactive",
            "--connection-string-reference", FullConnectionEnvironment);

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        var profile = temp.Store.Find("full-bootstrap")!;
        Assert.True(profile.UsesFullConnectionString);
        Assert.Equal("TQ_E2E_CONNECTION_STRING", profile.ConnectionStringValue!.Reference!.Name);
        Assert.Equal(SqlServerE2eMetadata.True, profile.Metadata[SqlServerE2eMetadata.Enabled]);
        Assert.False(profile.ServerValue.IsReference);
        Assert.Equal(string.Empty, profile.Server);
    }

    [Fact]
    public async Task FullConnectionStringAndFieldOptionsFailBeforeStoreMutation()
    {
        var result = await RunAsync(
            "connection", "add", "mixed",
            "--non-interactive",
            "--connection-string-reference", FullConnectionEnvironment,
            "--server-reference", ServerEnvironment);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains("cannot be combined", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Null(temp.Store.Find("mixed"));
    }

    [Theory]
    [InlineData("\"literal-secret-must-not-appear\"")]
    [InlineData("{\"Source\":\"SecretProviderValue\",\"Name\":\"do-not-echo\"}")]
    [InlineData("{\"Source\":\"File\",\"Path\":\"secret-path\",\"Format\":\"Json\"}")]
    public async Task InvalidReferenceJsonFailsSafelyBeforeStoreMutation(string reference)
    {
        var result = await RunAsync(
            "connection", "add", "invalid-reference",
            "--non-interactive",
            "--server-reference", reference);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains("reference", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("literal-secret-must-not-appear", result.StdErr);
        Assert.DoesNotContain("SecretProviderValue", result.StdErr);
        Assert.DoesNotContain("secret-path", result.StdErr);
        Assert.Null(temp.Store.Find("invalid-reference"));
    }

    [Fact]
    public async Task ShowAndListDescribeReferencesWithoutResolvingOrPrintingSecrets()
    {
        temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "referenced",
            ServerValue = EnvironmentReference("TQ_DISPLAY_SERVER"),
            DatabaseValue = JsonFileReference("/config/sql.json", "database"),
            Authentication = AuthenticationType.SqlPassword,
            UsernameValue = EnvironmentReference("TQ_DISPLAY_USER"),
            PasswordValue = SqlServerConnectionValue.Literal("literal-password-must-not-display"),
            Encrypt = EncryptOption.Mandatory,
            Options = new Dictionary<string, string>
            {
                ["Access Token"] = "literal-token-must-not-display"
            }
        });

        var show = await RunAsync("connection", "show", "referenced", "--non-interactive");
        var list = await RunAsync("connection", "list", "--non-interactive");
        var output = show.StdOut + show.StdErr + list.StdOut + list.StdErr;

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, show.ExitCode);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, list.ExitCode);
        Assert.Contains("TQ_DISPLAY_SERVER", output);
        Assert.Contains("/config/sql.json", output);
        Assert.Contains("TQ_DISPLAY_USER", output);
        Assert.Contains("redacted", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("literal-password-must-not-display", output);
        Assert.DoesNotContain("literal-token-must-not-display", output);
    }

    [Fact]
    public async Task ShowAndListRedactLiteralFullConnectionStrings()
    {
        temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "literal-full",
            ConnectionStringValue = SqlServerConnectionValue.Literal(
                "Server=private-server;User ID=user;Password=literal-secret")
        });

        var show = await RunAsync("connection", "show", "literal-full", "--non-interactive");
        var list = await RunAsync("connection", "list", "--non-interactive");
        var output = show.StdOut + list.StdOut;

        Assert.Contains("redacted", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-server", output);
        Assert.DoesNotContain("literal-secret", output);
    }

    [Fact]
    public async Task UnrelatedEditPreservesFieldReferences()
    {
        var add = await RunAsync(
            "connection", "add", "editable",
            "--non-interactive",
            "--authentication", "SqlPassword",
            "--server-reference", ServerEnvironment,
            "--username-reference", UsernameJsonFile,
            "--password-reference", PasswordTextFile);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        var edit = await RunAsync(
            "connection", "edit", "editable",
            "--non-interactive",
            "--metadata", "app.role=worker");

        Assert.True(
            edit.ExitCode == (int)TigerSqlCmdExitCode.Ok,
            edit.StdErr + Environment.NewLine + edit.StdOut);
        var profile = temp.Store.Find("editable")!;
        Assert.Equal("TQ_E2E_SERVER", profile.ServerValue.Reference!.Name);
        Assert.Equal("username", profile.UsernameValue!.Reference!.Key);
        Assert.Equal("/run/secrets/sql-password", profile.PasswordValue!.Reference!.Path);
        Assert.Equal("worker", profile.Metadata["app.role"]);
    }

    [Fact]
    public async Task UnrelatedEditPreservesFullConnectionStringReference()
    {
        var add = await RunAsync(
            "connection", "add", "full-editable",
            "--non-interactive",
            "--connection-string-reference", FullConnectionEnvironment);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        var edit = await RunAsync(
            "connection", "edit", "full-editable",
            "--non-interactive",
            "--metadata", "app.role=worker");

        Assert.True(
            edit.ExitCode == (int)TigerSqlCmdExitCode.Ok,
            edit.StdErr + Environment.NewLine + edit.StdOut);
        var profile = temp.Store.Find("full-editable")!;
        Assert.Equal(
            "TQ_E2E_CONNECTION_STRING",
            profile.ConnectionStringValue!.Reference!.Name);
        Assert.Equal("worker", profile.Metadata["app.role"]);
    }

    [Fact]
    public async Task EditCannotMixAFullProfileWithFieldInput()
    {
        var add = await RunAsync(
            "connection", "add", "full-mode",
            "--non-interactive",
            "--connection-string-reference", FullConnectionEnvironment);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        var edit = await RunAsync(
            "connection", "edit", "full-mode",
            "--non-interactive",
            "--server", "must-not-replace");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, edit.ExitCode);
        Assert.Contains("cannot be combined", edit.StdErr, StringComparison.OrdinalIgnoreCase);
        var unchanged = temp.Store.Find("full-mode")!;
        Assert.True(unchanged.UsesFullConnectionString);
        Assert.Equal(
            "TQ_E2E_CONNECTION_STRING",
            unchanged.ConnectionStringValue!.Reference!.Name);
    }

    [Fact]
    public async Task EditCannotMixAFieldProfileWithAFullReference()
    {
        var add = await RunAsync(
            "connection", "add", "field-mode",
            "--non-interactive",
            "--server-reference", ServerEnvironment);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        var edit = await RunAsync(
            "connection", "edit", "field-mode",
            "--non-interactive",
            "--connection-string-reference", FullConnectionEnvironment);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, edit.ExitCode);
        Assert.Contains("cannot be combined", edit.StdErr, StringComparison.OrdinalIgnoreCase);
        var unchanged = temp.Store.Find("field-mode")!;
        Assert.False(unchanged.UsesFullConnectionString);
        Assert.Equal("TQ_E2E_SERVER", unchanged.ServerValue.Reference!.Name);
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("Pwd")]
    [InlineData("Access Token")]
    public async Task SensitiveEscapeHatchValuesAreRejectedWithoutEchoingThem(string key)
    {
        const string Secret = "command-line-secret";
        var result = await RunAsync(
            "connection", "add", "unsafe-opt",
            "--non-interactive",
            "--server", "srv",
            "--opt", $"{key}={Secret}");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains("external reference", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, result.StdErr);
        Assert.Null(temp.Store.Find("unsafe-opt"));
    }

    [Fact]
    public async Task HelpExposesOnlyTheFiveReferenceOptions()
    {
        var result = await RunAsync("connection", "add", "demo", "--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("--server-reference", result.StdOut);
        Assert.Contains("--database-reference", result.StdOut);
        Assert.Contains("--username-reference", result.StdOut);
        Assert.Contains("--password-reference", result.StdOut);
        Assert.Contains("--connection-string-reference", result.StdOut);
        Assert.DoesNotContain("--external-value", result.StdOut);
    }

    private static SqlServerConnectionValue EnvironmentReference(string name) =>
        SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.EnvironmentVariable,
            Name = name
        });

    private static SqlServerConnectionValue JsonFileReference(string path, string key) =>
        SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.File,
            Path = path,
            Format = SqlServerExternalFileFormat.Json,
            Key = key
        });
}
