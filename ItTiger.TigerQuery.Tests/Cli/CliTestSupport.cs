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
    /// against the given store. A fresh app and single-use host per run, exactly like the
    /// production factory path.
    /// </summary>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        SqlServerConnectionStore store,
        Func<TigerCliAppTestHost, TigerCliAppTestHost>? configure,
        params string[] args)
    {
        var host = TigerCliAppTestHost.For(TigerSqlCmdApp.Build(store)).WithArgs(args);
        if (configure is not null)
            host = configure(host);

        var result = await host.RunAsync();
        return (result.ExitCode, result.StdOut, result.StdErr);
    }

    public static Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        SqlServerConnectionStore store,
        params string[] args)
        => RunAsync(store, configure: null, args);
}
