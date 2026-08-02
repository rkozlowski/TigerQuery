using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Enums;
using ItTiger.TigerCli.Testing;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.Tests.Cli;

namespace ItTiger.TigerQuery.Tests.CliCore;

/// <summary>
/// The exit codes the contribution test app maps TigerCli's semantic outcomes to, chosen
/// as distinct values so a test can tell a usage error from a validation error rather than
/// settling for "non-zero".
/// </summary>
internal enum ContributionExitCode
{
    Ok = 0,
    Usage = 11,
    Validation = 12,
    Unhandled = 13
}

/// <summary>
/// A minimal TigerCli application that registers <see cref="TigerQueryCliContribution"/>
/// and mounts the connection command group over the same options instance — the wiring
/// shape the guide requires of a host, without depending on tiger-sqlcmd, which has not
/// adopted the contribution yet.
/// </summary>
/// <remarks>
/// The <c>probe</c> command stands in for a host's own commands: it reads the store
/// through the shared options and records what it saw, so a test can prove that connection
/// commands, providers, and host commands all observe the one store the run selected.
/// </remarks>
internal sealed class ContributionTestApp
{
    private ContributionTestApp(TigerQueryCliOptions options, TigerCliApp app)
    {
        Options = options;
        App = app;
    }

    public TigerQueryCliOptions Options { get; }

    public TigerCliApp App { get; }

    /// <summary>The store paths the <c>probe</c> command observed, one entry per run.</summary>
    public List<string> ProbedStorePaths { get; } = [];

    /// <summary>The store instances the <c>probe</c> command observed, one entry per run.</summary>
    public List<SqlServerConnectionStore> ProbedStores { get; } = [];

    public static ContributionTestApp Create(
        string defaultStoreFile,
        Func<string, string?>? environmentReader = null,
        Func<IConnectionPasswordProtector>? passwordProtectorFactory = null)
    {
        var options = new TigerQueryCliOptions
        {
            DefaultConnectionStoreFile = defaultStoreFile,
            EnvironmentReader = environmentReader ?? Unset,
            PasswordProtectorFactory = passwordProtectorFactory
        };

        ContributionTestApp? app = null;
        var contribution = new TigerQueryCliContribution(options);

        var built = Builder(contribution)
            .AddCommand(
                "probe",
                () =>
                {
                    var store = options.Store;
                    app!.ProbedStorePaths.Add(store.FilePath);
                    app.ProbedStores.Add(store);
                    return Task.FromResult(TigerCliExitKind.Success);
                },
                "Reports the store the run selected.")
            .Build();

        app = new ContributionTestApp(options, built);
        return app;
    }

    /// <summary>
    /// Builds an app the same way <see cref="Create"/> does, but stopping at the builder so
    /// a test can add a second registration and assert that <c>Build()</c> rejects it.
    /// </summary>
    public static TigerCliAppBuilder Builder(TigerQueryCliContribution contribution) =>
        TigerCliApp.CreateBuilder()
            .SetApplicationName("tq-contrib-test")
            .SetDefaultCulture("en-US")
            .SetSupportedCultures("en-US", "pl-PL")
            .UseAppResources(SqlServerConnectionCommands.CreateAppResources())
            .UseExitCodes(ContributionExitCode.Ok, ContributionExitCode.Unhandled)
            .ExitCategory(TigerCliExitCategory.Usage, ContributionExitCode.Usage)
            .ExitKind(TigerCliExitKind.ValidationError, ContributionExitCode.Validation)
            .AddContribution(contribution)
            .AddCommandGroup("connections", group =>
                SqlServerConnectionCommands.Configure(group, options =>
                {
                    options.TigerQuery = contribution.Options;
                    options.ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional;
                }));

    /// <summary>
    /// Runs the app once. The app is reused across calls on purpose — a host builds one app
    /// and TigerCli re-invokes the contribution callback per run — while the test host is
    /// single-use and therefore created fresh each time.
    /// </summary>
    public async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args)
    {
        var result = await TigerCliAppTestHost.For(App).WithArgs(args).RunAsync();
        return (result.ExitCode, result.StdOut, result.StdErr);
    }

    /// <summary>An environment in which the TigerQuery variable is not set.</summary>
    public static string? Unset(string name) => null;
}

/// <summary>A temp-file path that no test creates unless the run under test writes to it.</summary>
internal sealed class TempStorePath : IDisposable
{
    public TempStorePath(string fileName = "store.json")
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(), "TigerQueryContributionTests", Guid.NewGuid().ToString("N"));
        FilePath = Path.Combine(DirectoryPath, fileName);
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public string ReadJson() => File.ReadAllText(FilePath);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a unique leftover temp directory is harmless.
        }
    }
}
