using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

[Collection(TigerCliAppCollection.Name)]
public sealed class TigerSqlCmdE2eSessionCommandTests : IDisposable
{
    private readonly TempConnectionStore temp = new();

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task HelpUsesSingularConnectionGroupWithoutCompatibilityAlias()
    {
        var help = await RunAsync("--help");
        var oldGroup = await RunAsync("connections", "--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, help.ExitCode);
        Assert.Contains("connection", help.StdOut);
        Assert.Contains("e2e", help.StdOut);
        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, oldGroup.ExitCode);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("drop")]
    [InlineData("cleanup")]
    public async Task E2eCommandsRequireSessionId(string command)
    {
        var result = await RunAsync("e2e", command, "--non-interactive");
        Assert.NotEqual((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task E2eCleanupRejectsInvalidSessionId(string sessionId)
    {
        var result = await RunAsync(
            "e2e", "cleanup", "--session-id", sessionId, "--non-interactive");
        Assert.NotEqual((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.False(File.Exists(temp.FilePath));
    }

    [Fact]
    public async Task CloneE2eCreatesNonOwningProtectedConnection()
    {
        temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "source",
            Server = "sql01",
            Authentication = AuthenticationType.Integrated
        });
        var sessionId = Guid.NewGuid();

        var result = await RunAsync(
            "connection", "clone-e2e", "source",
            "--database", "ExistingDb",
            "--session-id", sessionId.ToString("D"),
            "--connection-name-part", "read only",
            "--non-interactive");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        var clone = Assert.Single(temp.Store.Load(), profile => profile.Name != "source");
        Assert.StartsWith("E2E-read-only-", clone.Name, StringComparison.Ordinal);
        Assert.Equal("ExistingDb", clone.Database);
        Assert.Equal(SqlServerE2eMetadata.False, clone.Metadata[SqlServerE2eMetadata.AllowDatabaseDrop]);
    }

    [Fact]
    public async Task CleanupPartialFailureReturnsNonzeroAndKeepsOwnershipRecord()
    {
        var sessionId = Guid.NewGuid();
        var profile = new SqlServerConnectionProfile
        {
            Name = "malformed-owner",
            Server = "sql01",
            Database = "_TQ_E2E_owned_suffix"
        };
        SqlServerE2eMetadata.ConfigureSessionProfile(
            profile, sessionId, profile.Database!, allowDatabaseDrop: true);
        profile.SetReservedMetadata(SqlServerE2eMetadata.AllowDatabaseDrop, "True");
        temp.Store.Add(profile);

        var result = await RunAsync(
            "e2e", "cleanup", "--session-id", sessionId.ToString("D"), "--non-interactive");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains("Cleanup failed", result.StdErr, StringComparison.Ordinal);
        Assert.NotNull(temp.Store.Find("malformed-owner"));
    }

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args) =>
        CliTestRunner.RunAsync(temp.Store, args);
}
