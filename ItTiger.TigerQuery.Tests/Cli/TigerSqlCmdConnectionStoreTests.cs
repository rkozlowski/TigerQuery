using System.Reflection;
using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Host-integration tests for tiger-sqlcmd's adoption of the TigerQuery connection-store
/// contribution: that it opts in rather than reimplementing the option, that CLI beats
/// environment beats application default, and that the connection commands and the query
/// commands read the one store the run selected.
/// </summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class TigerSqlCmdConnectionStoreTests : IDisposable
{
    // The injected store is the *application default* for these runs; the alternate store
    // stands in for whatever an override points at.
    private readonly TempConnectionStore _default = new();
    private readonly TempConnectionStore _alternate = new();

    public void Dispose()
    {
        _default.Dispose();
        _alternate.Dispose();
    }

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args)
        => CliTestRunner.RunAsync(_default.Store, args);

    private Task<(int ExitCode, string StdOut, string StdErr)> RunWithEnvironmentAsync(
        string? environmentValue, params string[] args)
        => CliTestRunner.RunWithEnvironmentAsync(
            _default.Store,
            name => name == SqlServerConnectionStoreEnvironment.ConnectionStoreFile
                ? environmentValue
                : null,
            args);

    // ── The host opts in rather than reimplementing ──────────────────

    [Fact]
    public async Task Help_AdvertisesTheContributedStoreOptionExactlyOnce()
    {
        var result = await RunAsync("--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Equal(
            1,
            Occurrences(result.StdOut, TigerQueryCliContribution.ConnectionStoreFileOption));
    }

    [Fact]
    public async Task HelpEnv_AdvertisesTheTigerQueryStoreVariable()
    {
        var result = await RunAsync("--help-env");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Equal(
            1,
            Occurrences(result.StdOut, SqlServerConnectionStoreEnvironment.ConnectionStoreFile));
    }

    [Fact]
    public void NoHostSettingsTypeDeclaresTheStoreOption()
    {
        var declared = typeof(TigerSqlCmdApp).Assembly
            .GetTypes()
            .Where(type => typeof(TigerCliSettings).IsAssignableFrom(type))
            .SelectMany(type => type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(property => property.GetCustomAttributes<TigerCliOptionAttribute>())
            .SelectMany(attribute => attribute.Aliases)
            .ToList();

        Assert.NotEmpty(declared);
        Assert.DoesNotContain(
            TigerQueryCliContribution.ConnectionStoreFileOption,
            declared,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheShippedApplicationDefaultIsStillTheSharedTigerStore()
    {
        var shared = SqlServerConnectionStoreOptions.Shared("ItTiger.net");

        Assert.Equal(shared.FilePath, TigerSqlCmdApp.DefaultConnectionStoreFile);
        Assert.NotNull(TigerSqlCmdApp.Build());
    }

    // ── CLI > environment > application default ──────────────────────

    [Fact]
    public async Task WithNoOverride_TheApplicationDefaultStoreIsUsed()
    {
        var add = await RunAsync(
            "connection", "add", "in-default", "--non-interactive", "--server", "srv");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        Assert.NotNull(_default.Store.Find("in-default"));
        Assert.Null(_alternate.Store.Find("in-default"));
    }

    [Fact]
    public async Task TheCliOptionSelectsAnAlternateStore()
    {
        var add = await RunAsync(
            "connection", "add", "in-alternate",
            "--non-interactive",
            "--server", "srv",
            "--tq-connection-store-file", _alternate.FilePath);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        Assert.NotNull(_alternate.Store.Find("in-alternate"));
        Assert.Null(_default.Store.Find("in-alternate"));

        var list = await RunAsync(
            "connection", "list",
            "--non-interactive",
            "--tq-connection-store-file", _alternate.FilePath);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, list.ExitCode);
        Assert.Contains("in-alternate", list.StdOut);
    }

    [Fact]
    public async Task TheEnvironmentVariableSelectsAnAlternateStore()
    {
        var add = await RunWithEnvironmentAsync(
            _alternate.FilePath,
            "connection", "add", "from-environment", "--non-interactive", "--server", "srv");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        Assert.NotNull(_alternate.Store.Find("from-environment"));
        Assert.Null(_default.Store.Find("from-environment"));
    }

    [Fact]
    public async Task TheCliOptionWinsOverTheEnvironmentVariable()
    {
        using var third = new TempConnectionStore();

        // The environment names the alternate store; the option names a third one.
        var add = await RunWithEnvironmentAsync(
            _alternate.FilePath,
            "connection", "add", "from-cli",
            "--non-interactive",
            "--server", "srv",
            "--tq-connection-store-file", third.FilePath);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        Assert.NotNull(third.Store.Find("from-cli"));
        Assert.Null(_alternate.Store.Find("from-cli"));
        Assert.Null(_default.Store.Find("from-cli"));
    }

    [Fact]
    public async Task AnInjectedStoreIsOnlyTheDefaultAndLosesToBothOverrides()
    {
        // Documents the test-injection contract: CliTestRunner replaces the application
        // default, so a test cannot accidentally prove the opposite of shipping precedence.
        var viaEnvironment = await RunWithEnvironmentAsync(
            _alternate.FilePath,
            "connection", "add", "env-wins", "--non-interactive", "--server", "srv");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, viaEnvironment.ExitCode);

        var viaCli = await RunAsync(
            "connection", "add", "cli-wins",
            "--non-interactive",
            "--server", "srv",
            "--tq-connection-store-file", _alternate.FilePath);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, viaCli.ExitCode);

        Assert.Equal(["env-wins", "cli-wins"], _alternate.Store.Load().Select(p => p.Name));
        Assert.Empty(_default.Store.Load());
    }

    [Fact]
    public async Task AnUnusableEnvironmentValueFailsTheRunInsteadOfFallingBack()
    {
        var result = await RunWithEnvironmentAsync(
            "   ", "connection", "list", "--non-interactive");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains(SqlServerConnectionStoreEnvironment.ConnectionStoreFile, result.StdErr);
        Assert.False(File.Exists(_default.FilePath));
    }

    [Fact]
    public async Task AnUnusableCliValueFailsTheRunInsteadOfFallingBack()
    {
        var result = await RunAsync(
            "connection", "list", "--non-interactive", "--tq-connection-store-file", "   ");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains(TigerQueryCliContribution.ConnectionStoreFileOption, result.StdErr);
        Assert.False(File.Exists(_default.FilePath));
    }

    // ── One store per run, shared by every command ───────────────────

    [Fact]
    public async Task TheRunCommandResolvesAgainstTheSameStoreTheConnectionCommandsWrote()
    {
        // Each profile is created through `connection add` against a different store.
        var addDefault = await RunAsync(
            "connection", "add", "in-default", "--non-interactive", "--server", "srv");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, addDefault.ExitCode);

        var addAlternate = await RunAsync(
            "connection", "add", "in-alternate",
            "--non-interactive",
            "--server", "srv",
            "--tq-connection-store-file", _alternate.FilePath);
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, addAlternate.ExitCode);

        // Both stores now have exactly one profile, so the app-level `connection` provider
        // has choices and TigerCli validates -c against them before the handler runs. With
        // no override the provider enumerates the default store, so the alternate's name is
        // not an available choice.
        var missing = await RunAsync(
            "run", "-c", "in-alternate", "-q", "select 1", "--non-interactive");
        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, missing.ExitCode);
        Assert.Contains("in-alternate", missing.StdErr);

        // With the override the same provider enumerates the alternate store, so now it is
        // the default store's name that is rejected. One provider, two runs, two stores.
        var swapped = await RunAsync(
            "run", "-c", "in-default", "-q", "select 1",
            "--non-interactive",
            "--tq-connection-store-file", _alternate.FilePath);
        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, swapped.ExitCode);
        Assert.Contains("in-default", swapped.StdErr);
    }

    [Fact]
    public async Task TheRunCommandHandlerResolvesAgainstTheRunSelectedStore()
    {
        var add = await RunAsync(
            "connection", "add", "in-default", "--non-interactive", "--server", "srv");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        // The alternate store stays empty, so the provider offers no choices and its
        // validation is skipped — the failure comes from the handler's own resolver, which
        // is the path that reads TigerQueryCliOptions.Store. No connection is attempted.
        var result = await RunAsync(
            "run", "-c", "in-default", "-q", "select 1",
            "--non-interactive",
            "--tq-connection-store-file", _alternate.FilePath);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("in-default", result.StdErr);
    }

    [Fact]
    public async Task TheDefaultQueryCommandAlsoResolvesAgainstTheRunSelectedStore()
    {
        var add = await RunAsync(
            "connection", "add", "in-default", "--non-interactive", "--server", "srv");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        // Same check for the default command, whose handler reads the same shared options.
        var result = await RunAsync(
            "-c", "in-default", "-q", "select 1",
            "--non-interactive",
            "--tq-connection-store-file", _alternate.FilePath);

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("in-default", result.StdErr);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
