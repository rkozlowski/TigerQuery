using ItTiger.TigerSqlCmd;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Application-level tests for the reusable <c>connections</c> command group hosted by
/// tiger-sqlcmd, asserting the host's final exit-code contract and both interaction modes
/// against an isolated temp-file store.
/// </summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class SqlServerConnectionCommandTests : IDisposable
{
    private readonly TempConnectionStore _temp = new();

    public void Dispose() => _temp.Dispose();

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args)
        => CliTestRunner.RunAsync(_temp.Store, args);

    private Task<(int ExitCode, string StdOut, string StdErr)> AddDemoAsync()
        => RunAsync("--non-interactive", "connections", "add", "demo", "--server", "srv");

    [Fact]
    public async Task Add_Show_List_Delete_HappyPath()
    {
        var add = await AddDemoAsync();
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);
        Assert.Contains("demo", add.StdOut);

        var edit = await RunAsync(
            "--non-interactive", "connections", "edit", "demo", "--server", "updated");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, edit.ExitCode);

        var show = await RunAsync("--non-interactive", "connections", "show", "demo");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, show.ExitCode);
        Assert.Contains("demo", show.StdOut);
        Assert.Contains("updated", show.StdOut);

        var list = await RunAsync("--non-interactive", "connections", "list");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, list.ExitCode);
        Assert.Contains("demo", list.StdOut);

        var delete = await RunAsync("--non-interactive", "connections", "delete", "demo");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, delete.ExitCode);
    }

    [Fact]
    public async Task Edit_PreservesMetadataOnServerLevelProfileThroughResolveAndResave()
    {
        var profile = new SqlServerConnectionProfile
        {
            Name = "automation-host",
            Server = "original-server",
            Database = null,
            Authentication = AuthenticationType.Integrated,
            Encrypt = EncryptOption.Mandatory
        };
        profile.SetMetadata("ittiger.tigerwrap.role", "automated-test-host");
        _temp.Store.Add(profile);

        var edit = await RunAsync(
            "--non-interactive",
            "connections",
            "edit",
            "automation-host",
            "--server",
            "updated-server");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, edit.ExitCode);
        var edited = _temp.Store.Find("automation-host")!;
        Assert.Null(edited.Database);
        Assert.Equal(
            "automated-test-host",
            edited.Metadata["ittiger.tigerwrap.role"]);

        var resolution = SqlServerConnectionResolver.Resolve(_temp.Store, "automation-host");
        Assert.True(resolution.IsSuccess, resolution.ErrorMessage);
        Assert.DoesNotContain("Initial Catalog", resolution.ConnectionString);
        Assert.DoesNotContain("ittiger.tigerwrap.role", resolution.ConnectionString);

        _temp.Store.AddOrUpdate(edited);
        var resaved = _temp.Store.Find("automation-host")!;
        Assert.Equal(
            "automated-test-host",
            resaved.Metadata["ittiger.tigerwrap.role"]);
    }

    [Fact]
    public async Task Add_WithOneMetadataEntry_PersistsIt()
    {
        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "add",
            "demo",
            "--server",
            "srv",
            "--metadata",
            "app.role=worker");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Equal("worker", _temp.Store.Find("demo")!.Metadata["app.role"]);
    }

    [Fact]
    public async Task Add_WithMultipleMetadataEntries_PersistsAll()
    {
        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "add",
            "demo",
            "--server",
            "srv",
            "--metadata",
            "app.role=worker",
            "--metadata",
            "app.region=west");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        var metadata = _temp.Store.Find("demo")!.Metadata;
        Assert.Equal("worker", metadata["app.role"]);
        Assert.Equal("west", metadata["app.region"]);
    }

    [Fact]
    public async Task Add_MetadataValueContainingEquals_SplitsOnlyAtFirstEquals()
    {
        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "add",
            "demo",
            "--server",
            "srv",
            "--metadata",
            "app.expression=left=middle=right");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Equal(
            "left=middle=right",
            _temp.Store.Find("demo")!.Metadata["app.expression"]);
    }

    [Fact]
    public async Task Add_MetadataAllowsEmptyValue()
    {
        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "add",
            "demo",
            "--server",
            "srv",
            "--metadata",
            "app.marker=");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Equal("", _temp.Store.Find("demo")!.Metadata["app.marker"]);
    }

    [Fact]
    public async Task Edit_MetadataReplacesExistingValue()
    {
        AddProfile("demo", ("app.role", "old"));

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "edit",
            "demo",
            "--metadata",
            "app.role=new");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Equal("new", _temp.Store.Find("demo")!.Metadata["app.role"]);
    }

    [Fact]
    public async Task Edit_MetadataAddsKeyAndPreservesUnrelatedKeys()
    {
        AddProfile("demo", ("app.existing", "preserved"));

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "edit",
            "demo",
            "--metadata",
            "app.added=value");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        var metadata = _temp.Store.Find("demo")!.Metadata;
        Assert.Equal("value", metadata["app.added"]);
        Assert.Equal("preserved", metadata["app.existing"]);
    }

    [Fact]
    public async Task Edit_RemoveMetadataRemovesOnlyRequestedKey()
    {
        AddProfile(
            "demo",
            ("app.remove", "value"),
            ("app.preserve", "value"));

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "edit",
            "demo",
            "--remove-metadata",
            "app.remove");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        var metadata = _temp.Store.Find("demo")!.Metadata;
        Assert.False(metadata.ContainsKey("app.remove"));
        Assert.Equal("value", metadata["app.preserve"]);
    }

    [Fact]
    public async Task Edit_RemoveMissingMetadataKeySucceeds()
    {
        AddProfile("demo", ("app.preserve", "value"));

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "edit",
            "demo",
            "--remove-metadata",
            "app.missing");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Equal("value", _temp.Store.Find("demo")!.Metadata["app.preserve"]);
    }

    [Fact]
    public async Task Edit_ConflictingMetadataSetAndRemove_ReturnsMappedValidationExitCode()
    {
        AddProfile("demo", ("app.role", "old"));

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "edit",
            "demo",
            "--metadata",
            "app.role=new",
            "--remove-metadata",
            "app.role");

        Assert.Equal(
            (int)TigerSqlCmdExitCode.ConnectionInvalidArguments,
            result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
        Assert.Equal("old", _temp.Store.Find("demo")!.Metadata["app.role"]);
    }

    [Fact]
    public async Task Add_DuplicateMetadataAssignmentFailsValidation()
    {
        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "add",
            "demo",
            "--server",
            "srv",
            "--metadata",
            "app.role=first",
            "--metadata",
            "app.role=second");

        Assert.Equal(
            (int)TigerSqlCmdExitCode.ConnectionInvalidArguments,
            result.ExitCode);
        Assert.Null(_temp.Store.Find("demo"));
    }

    [Fact]
    public async Task Add_MalformedMetadataAssignmentFailsValidation()
    {
        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "add",
            "demo",
            "--server",
            "srv",
            "--metadata",
            "missing-separator");

        Assert.Equal(
            (int)TigerSqlCmdExitCode.ConnectionInvalidArguments,
            result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
        Assert.Null(_temp.Store.Find("demo"));
    }

    [Fact]
    public async Task Show_DisplaysMetadataInOrdinalKeyOrder()
    {
        AddProfile(
            "demo",
            ("z.key", "last"),
            ("A.key", "first"),
            ("a.key", "middle"));

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "show",
            "demo");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("Metadata", result.StdOut);
        var upperIndex = result.StdOut.IndexOf("A.key", StringComparison.Ordinal);
        var lowerIndex = result.StdOut.IndexOf("a.key", StringComparison.Ordinal);
        var zetaIndex = result.StdOut.IndexOf("z.key", StringComparison.Ordinal);
        Assert.True(upperIndex >= 0);
        Assert.True(upperIndex < lowerIndex);
        Assert.True(lowerIndex < zetaIndex);
    }

    [Fact]
    public async Task List_MetadataEqualsFilterUsesStoreQuery()
    {
        AddProfile("worker", ("app.role", "worker"));
        AddProfile("reader", ("app.role", "reader"));
        AddProfile("missing");

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "list",
            "--metadata",
            "app.role=worker");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("worker", result.StdOut);
        Assert.DoesNotContain("reader", result.StdOut);
        Assert.DoesNotContain("missing", result.StdOut);
    }

    [Fact]
    public async Task List_MetadataSetFilterIncludesEmptyValue()
    {
        AddProfile("empty", ("app.marker", ""));
        AddProfile("missing");

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "list",
            "--metadata-set",
            "app.marker");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("empty", result.StdOut);
        Assert.DoesNotContain("missing", result.StdOut);
    }

    [Fact]
    public async Task List_MetadataNotSetFilterMatchesOnlyMissingKey()
    {
        AddProfile("set", ("app.marker", ""));
        AddProfile("missing");

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "list",
            "--metadata-not-set",
            "app.marker");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("missing", result.StdOut);
        Assert.DoesNotContain("set", result.StdOut);
    }

    [Fact]
    public async Task List_MultipleMetadataFiltersUseAndSemanticsNonInteractively()
    {
        AddProfile(
            "both",
            ("app.role", "worker"),
            ("app.region", "west"));
        AddProfile("role-only", ("app.role", "worker"));
        AddProfile("region-only", ("app.region", "west"));

        var result = await RunAsync(
            "--non-interactive",
            "connections",
            "list",
            "--metadata",
            "app.role=worker",
            "--metadata-set",
            "app.region");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("both", result.StdOut);
        Assert.DoesNotContain("role-only", result.StdOut);
        Assert.DoesNotContain("region-only", result.StdOut);
    }

    [Fact]
    public async Task Add_DuplicateName_ReturnsAlreadyExists()
    {
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, (await AddDemoAsync()).ExitCode);

        var duplicate = await AddDemoAsync();

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionAlreadyExists, duplicate.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(duplicate.StdErr));
    }

    [Fact]
    public async Task Show_MissingConnection_ReturnsNotFound()
    {
        var result = await RunAsync("--non-interactive", "connections", "show", "missing");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionNotFound, result.ExitCode);
        Assert.Contains("missing", result.StdErr);
    }

    [Fact]
    public async Task Delete_MissingConnection_ReturnsNotFound()
    {
        var result = await RunAsync("--non-interactive", "connections", "delete", "missing");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionNotFound, result.ExitCode);
    }

    [Fact]
    public async Task Add_SqlPasswordWithoutCredentials_ReturnsInvalidArguments()
    {
        // Non-interactive, so the username/password prompts are skipped and the command's
        // own domain validation reports the missing credentials.
        var result = await RunAsync(
            "--non-interactive", "connections", "add", "demo",
            "--server", "srv", "--authentication", "SqlPassword");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task Add_PoolSizeWithPoolingDisabled_FailsFrameworkValidation()
    {
        // settings.Validate() rejects this before the handler runs, so the outcome is the
        // same semantic kind as command-level domain validation. TigerCli's app-wide
        // policy therefore maps both paths to the host's connection-validation value.
        var result = await RunAsync(
            "--non-interactive", "connections", "add", "demo",
            "--server", "srv", "--pooling", "false", "--min-pool-size", "2");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task Show_Interactive_PromptsNameFromProvider()
    {
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, (await AddDemoAsync()).ExitCode);

        // No name supplied: the positional argument is prompted from the "connections"
        // provider; the first (only) choice is the seeded profile.
        var result = await CliTestRunner.RunAsync(
            _temp.Store,
            host => host.WithSelectIndex(0),
            "connections", "show");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("demo", result.StdOut);
    }

    [Fact]
    public void HostConnectionExitCodes_PreserveHistoricValues()
    {
        Assert.Equal(0, (int)TigerSqlCmdExitCode.Ok);
        Assert.Equal(2, (int)TigerSqlCmdExitCode.ConnectionInvalidArguments);
        Assert.Equal(4, (int)TigerSqlCmdExitCode.ConnectionNotFound);
        Assert.Equal(5, (int)TigerSqlCmdExitCode.ConnectionAlreadyExists);
    }

    private void AddProfile(
        string name,
        params (string Key, string Value)[] metadata)
    {
        var profile = new SqlServerConnectionProfile
        {
            Name = name,
            Server = $"{name}-server",
            Authentication = AuthenticationType.Integrated,
            Encrypt = EncryptOption.Mandatory
        };

        foreach (var (key, value) in metadata)
            profile.SetMetadata(key, value);

        _temp.Store.Add(profile);
    }
}
