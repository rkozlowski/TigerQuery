using ItTiger.TigerCli.Testing;
using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// TigerCliAppTestHost redirects the process-global console streams, so every test that
/// runs the app through the host lives in this non-parallel collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class TigerCliAppCollection
{
    public const string Name = "TigerCliApp";
}

/// <summary>
/// A <see cref="SqlServerConnectionStore"/> backed by a unique temp file so CLI tests
/// never touch (or depend on) the machine-wide shared store.
/// </summary>
internal sealed class TempConnectionStore : IDisposable
{
    public string FilePath { get; }
    public SqlServerConnectionStore Store { get; }

    public TempConnectionStore()
    {
        FilePath = Path.Combine(
            Path.GetTempPath(), "TigerQueryCliTests", $"{Guid.NewGuid():N}.json");
        Store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = FilePath });
    }

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

internal static class CliTestRunner
{
    /// <summary>
    /// Runs the real tiger-sqlcmd app composition (via <see cref="TigerSqlCmdApp.Build"/>)
    /// with <paramref name="store"/>'s file as the *application default*. A fresh app and
    /// single-use host per run, exactly like the production factory path.
    /// </summary>
    /// <remarks>
    /// Injection deliberately replaces the application default rather than pinning a store
    /// above everything, so <c>--tq-connection-store-file</c> and the environment variable
    /// keep the precedence they have in a shipped run. The environment is read through
    /// <see cref="NoEnvironmentOverride"/> so a machine that happens to have
    /// <c>TIGERQUERY_CONNECTION_STORE_FILE</c> set cannot pull a test onto a real store;
    /// use <see cref="RunWithEnvironmentAsync"/> to supply one on purpose.
    /// </remarks>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        SqlServerConnectionStore store,
        Func<TigerCliAppTestHost, TigerCliAppTestHost>? configure,
        params string[] args)
        => await RunAsync(store, NoEnvironmentOverride, configure, args);

    public static Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        SqlServerConnectionStore store,
        params string[] args)
        => RunAsync(store, configure: null, args);

    /// <summary>
    /// Runs the app with a substitute environment, for tests that need
    /// <c>TIGERQUERY_CONNECTION_STORE_FILE</c> to have a value without mutating the
    /// process environment that the parallel test suites share.
    /// </summary>
    public static Task<(int ExitCode, string StdOut, string StdErr)> RunWithEnvironmentAsync(
        SqlServerConnectionStore store,
        Func<string, string?> environmentReader,
        params string[] args)
        => RunAsync(store, environmentReader, configure: null, args);

    /// <summary>An environment in which the TigerQuery store variable is not set.</summary>
    public static string? NoEnvironmentOverride(string name) => null;

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        SqlServerConnectionStore store,
        Func<string, string?> environmentReader,
        Func<TigerCliAppTestHost, TigerCliAppTestHost>? configure,
        params string[] args)
    {
        var app = TigerSqlCmdApp.Build(store.FilePath, environmentReader);
        var host = TigerCliAppTestHost.For(app).WithArgs(args);
        if (configure is not null)
            host = configure(host);

        var result = await host.RunAsync();
        return (result.ExitCode, result.StdOut, result.StdErr);
    }
}
