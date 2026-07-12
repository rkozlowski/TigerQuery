using ItTiger.TigerQuery.CliCore;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Application-level tests for the reusable <c>connections</c> command group hosted by
/// tiger-sqlcmd, asserting the CliCore exit-code contract and both interaction modes
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
        Assert.Equal((int)SqlServerConnectionExitCode.Ok, add.ExitCode);
        Assert.Contains("demo", add.StdOut);

        var show = await RunAsync("--non-interactive", "connections", "show", "demo");
        Assert.Equal((int)SqlServerConnectionExitCode.Ok, show.ExitCode);
        Assert.Contains("demo", show.StdOut);
        Assert.Contains("srv", show.StdOut);

        var list = await RunAsync("--non-interactive", "connections", "list");
        Assert.Equal((int)SqlServerConnectionExitCode.Ok, list.ExitCode);
        Assert.Contains("demo", list.StdOut);

        var delete = await RunAsync("--non-interactive", "connections", "delete", "demo");
        Assert.Equal((int)SqlServerConnectionExitCode.Ok, delete.ExitCode);
    }

    [Fact]
    public async Task Add_DuplicateName_ReturnsAlreadyExists()
    {
        Assert.Equal((int)SqlServerConnectionExitCode.Ok, (await AddDemoAsync()).ExitCode);

        var duplicate = await AddDemoAsync();

        Assert.Equal((int)SqlServerConnectionExitCode.AlreadyExists, duplicate.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(duplicate.StdErr));
    }

    [Fact]
    public async Task Show_MissingConnection_ReturnsNotFound()
    {
        var result = await RunAsync("--non-interactive", "connections", "show", "missing");

        Assert.Equal((int)SqlServerConnectionExitCode.NotFound, result.ExitCode);
        Assert.Contains("missing", result.StdErr);
    }

    [Fact]
    public async Task Delete_MissingConnection_ReturnsNotFound()
    {
        var result = await RunAsync("--non-interactive", "connections", "delete", "missing");

        Assert.Equal((int)SqlServerConnectionExitCode.NotFound, result.ExitCode);
    }

    [Fact]
    public async Task Add_SqlPasswordWithoutCredentials_ReturnsInvalidArguments()
    {
        // Non-interactive, so the username/password prompts are skipped and the command's
        // own domain validation reports the missing credentials.
        var result = await RunAsync(
            "--non-interactive", "connections", "add", "demo",
            "--server", "srv", "--authentication", "SqlPassword");

        Assert.Equal((int)SqlServerConnectionExitCode.InvalidArguments, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task Add_PoolSizeWithPoolingDisabled_FailsFrameworkValidation()
    {
        // settings.Validate() rejects this before the handler runs, so the outcome is the
        // app-mapped framework validation code, not a CliCore code.
        var result = await RunAsync(
            "--non-interactive", "connections", "add", "demo",
            "--server", "srv", "--pooling", "false", "--min-pool-size", "2");

        Assert.Equal((int)ItTiger.TigerSqlCmd.TigerSqlCmdExitCode.ValidationError, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task Show_Interactive_PromptsNameFromProvider()
    {
        Assert.Equal((int)SqlServerConnectionExitCode.Ok, (await AddDemoAsync()).ExitCode);

        // No name supplied: the positional argument is prompted from the "connections"
        // provider; the first (only) choice is the seeded profile.
        var result = await CliTestRunner.RunAsync(
            _temp.Store,
            host => host.WithSelectIndex(0),
            "connections", "show");

        Assert.Equal((int)SqlServerConnectionExitCode.Ok, result.ExitCode);
        Assert.Contains("demo", result.StdOut);
    }

    [Fact]
    public void ConnectionExitCodes_PreserveHistoricValues()
    {
        Assert.Equal(0, (int)SqlServerConnectionExitCode.Ok);
        Assert.Equal(2, (int)SqlServerConnectionExitCode.InvalidArguments);
        Assert.Equal(4, (int)SqlServerConnectionExitCode.NotFound);
        Assert.Equal(5, (int)SqlServerConnectionExitCode.AlreadyExists);
    }
}
