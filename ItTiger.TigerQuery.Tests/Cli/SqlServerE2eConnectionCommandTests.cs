using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>Phase 5 command-level coverage for safe E2E profile creation.</summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class SqlServerE2eConnectionCommandTests : IDisposable
{
    private readonly TempConnectionStore temp = new();

    public void Dispose() => temp.Dispose();

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args) =>
        CliTestRunner.RunAsync(temp.Store, args);

    [Fact]
    public async Task RegularAdd_E2eAuthorizesWithoutMakingTheProfileTheBootstrap()
    {
        var result = await RunAsync(
            "connections", "add", "general-e2e",
            "--non-interactive", "--server", "srv", "--e2e");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        var profile = temp.Store.Find("general-e2e")!;
        Assert.Equal(SqlServerE2eMetadata.True, profile.Metadata[SqlServerE2eMetadata.Enabled]);
        Assert.False(profile.Metadata.ContainsKey(SqlServerE2eMetadata.AllowDatabaseCreation));

        var resolution = SqlServerE2eConnectionResolver.Resolve(
            temp.Store,
            new SqlServerE2eConnectionResolutionOptions
            {
                DefaultConnectionName = TigerSqlCmdApp.DefaultE2eBootstrapConnectionName
            });

        Assert.Equal(SqlServerE2eResolutionStatus.NotConfigured, resolution.Status);
        Assert.Null(resolution.Profile);
        Assert.Equal(TigerSqlCmdApp.DefaultE2eBootstrapConnectionName, resolution.RequestedName);
    }

    [Fact]
    public async Task RegularAdd_CanAuthorizeDatabaseCreationExplicitly()
    {
        var result = await RunAsync(
            "connections", "add", "database-e2e",
            "--non-interactive", "--server", "srv",
            "--e2e", "--allow-database-create");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        var metadata = temp.Store.Find("database-e2e")!.Metadata;
        Assert.Equal(SqlServerE2eMetadata.True, metadata[SqlServerE2eMetadata.Enabled]);
        Assert.Equal(
            SqlServerE2eMetadata.True,
            metadata[SqlServerE2eMetadata.AllowDatabaseCreation]);
    }

    [Fact]
    public async Task RegularAdd_DatabaseCreationPermissionRequiresE2eAuthorization()
    {
        var result = await RunAsync(
            "connections", "add", "invalid",
            "--non-interactive", "--server", "srv",
            "--allow-database-create");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Null(temp.Store.Find("invalid"));
    }

    [Fact]
    public async Task BootstrapAdd_UsesTheTigerSqlCmdHostDefault()
    {
        var result = await RunAsync(
            "connections", "add-e2e-bootstrap",
            "--non-interactive", "--server", "srv");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        var profile = temp.Store.Find(TigerSqlCmdApp.DefaultE2eBootstrapConnectionName)!;
        Assert.Equal(SqlServerE2eMetadata.True, profile.Metadata[SqlServerE2eMetadata.Enabled]);
        Assert.Single(profile.Metadata);
    }

    [Fact]
    public async Task BootstrapAdd_ExplicitNameOverridesTheHostDefault()
    {
        var result = await RunAsync(
            "connections", "add-e2e-bootstrap",
            "--non-interactive", "--name", "explicit-bootstrap", "--server", "srv",
            "--allow-database-create");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Null(temp.Store.Find(TigerSqlCmdApp.DefaultE2eBootstrapConnectionName));
        var metadata = temp.Store.Find("explicit-bootstrap")!.Metadata;
        Assert.Equal(SqlServerE2eMetadata.True, metadata[SqlServerE2eMetadata.Enabled]);
        Assert.Equal(
            SqlServerE2eMetadata.True,
            metadata[SqlServerE2eMetadata.AllowDatabaseCreation]);
    }

    [Fact]
    public async Task OrdinaryEditPreservesReservedE2eMetadataWithoutExposingLifecycleOptions()
    {
        var add = await RunAsync(
            "connections", "add", "editable-e2e",
            "--non-interactive", "--server", "before", "--e2e");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        var edit = await RunAsync(
            "connections", "edit", "editable-e2e",
            "--non-interactive", "--server", "after");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, edit.ExitCode);
        var profile = temp.Store.Find("editable-e2e")!;
        Assert.Equal("after", profile.Server);
        Assert.Equal(SqlServerE2eMetadata.True, profile.Metadata[SqlServerE2eMetadata.Enabled]);

        var help = await RunAsync("connections", "edit", "editable-e2e", "--help");
        Assert.DoesNotContain("--e2e", help.StdOut);
        Assert.DoesNotContain("--allow-database-create", help.StdOut);
    }

    [Theory]
    [InlineData(SqlServerE2eMetadata.Enabled)]
    [InlineData("ittiger.future.setting")]
    public async Task GenericMetadataSetRejectsKnownAndUnknownReservedKeys(string key)
    {
        var result = await RunAsync(
            "connections", "add", "generic",
            "--non-interactive", "--server", "srv", "--metadata", $"{key}=true");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains("reserved", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Null(temp.Store.Find("generic"));
    }

    [Theory]
    [InlineData(SqlServerE2eMetadata.Enabled)]
    [InlineData("ittiger.future.setting")]
    public async Task GenericMetadataRemoveRejectsKnownAndUnknownReservedKeys(string key)
    {
        var profile = new SqlServerConnectionProfile
        {
            Name = "generic",
            Server = "srv",
            Authentication = AuthenticationType.Integrated,
            Encrypt = EncryptOption.Mandatory
        };
        profile.SetReservedMetadata(key, "preserved");
        temp.Store.Add(profile);

        var result = await RunAsync(
            "connections", "edit", "generic",
            "--non-interactive", "--remove-metadata", key);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Equal("preserved", temp.Store.Find("generic")!.Metadata[key]);
    }

    [Fact]
    public async Task ReservedMetadataRemainsReadableAndFilterable()
    {
        var profile = new SqlServerConnectionProfile
        {
            Name = "newer-profile",
            Server = "srv",
            Authentication = AuthenticationType.Integrated,
            Encrypt = EncryptOption.Mandatory
        };
        profile.SetReservedMetadata("ittiger.future.setting", "future-value");
        temp.Store.Add(profile);

        var show = await RunAsync(
            "connections", "show", "newer-profile", "--non-interactive");
        var list = await RunAsync(
            "connections", "list", "--non-interactive",
            "--metadata", "ittiger.future.setting=future-value");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, show.ExitCode);
        Assert.Contains("ittiger.future.setting", show.StdOut);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, list.ExitCode);
        Assert.Contains("newer-profile", list.StdOut);
    }

    [Fact]
    public async Task PolishHelpContainsTheBootstrapCommandResources()
    {
        var result = await RunAsync("connections", "--help", "--culture", "pl-PL");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("add-e2e-bootstrap", result.StdOut);
        Assert.Contains("startowe", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }
}
