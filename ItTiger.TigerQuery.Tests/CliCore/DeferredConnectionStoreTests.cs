using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.Tests.Cli;

namespace ItTiger.TigerQuery.Tests.CliCore;

/// <summary>
/// Covers store selection deferred to run time: that every consumer of the command group
/// reads the store the run selected, that they all read the same instance, and that a host
/// which wires the group wrongly finds out immediately.
/// </summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class DeferredConnectionStoreTests : IDisposable
{
    private readonly TempStorePath _default = new("default.json");
    private readonly TempStorePath _explicitPath = new("explicit.json");
    private readonly TempStorePath _second = new("second.json");

    public void Dispose()
    {
        _default.Dispose();
        _explicitPath.Dispose();
        _second.Dispose();
    }

    // ---- The run-selected store is the one every consumer uses ----

    [Fact]
    public async Task ConnectionCommandsWriteAndReadTheRunSelectedStore()
    {
        var app = ContributionTestApp.Create(_default.FilePath);

        var add = await app.RunAsync(
            "connections", "add", "demo",
            "--non-interactive",
            "--server", "srv",
            "--tq-connection-store-file", _explicitPath.FilePath);
        Assert.Equal((int)ContributionExitCode.Ok, add.ExitCode);

        var list = await app.RunAsync(
            "connections", "list",
            "--non-interactive",
            "--tq-connection-store-file", _explicitPath.FilePath);

        Assert.Equal((int)ContributionExitCode.Ok, list.ExitCode);
        Assert.Contains("demo", list.StdOut);
        Assert.True(_explicitPath.Exists);
        Assert.False(_default.Exists);
    }

    [Fact]
    public async Task TheEditLoaderSeedsFromTheRunSelectedStore()
    {
        var app = ContributionTestApp.Create(_default.FilePath);
        Seed(_explicitPath.FilePath, "demo", "original-server");

        var edit = await app.RunAsync(
            "connections", "edit", "demo",
            "--non-interactive",
            "--database", "AdventureWorks",
            "--tq-connection-store-file", _explicitPath.FilePath);

        Assert.Equal((int)ContributionExitCode.Ok, edit.ExitCode);

        // The loader supplied the server, so an edit that never mentioned it kept it.
        var edited = Open(_explicitPath.FilePath).Find("demo")!;
        Assert.Equal("original-server", edited.Server);
        Assert.Equal("AdventureWorks", edited.Database);
        Assert.False(_default.Exists);
    }

    [Fact]
    public async Task AHostCommandAndTheConnectionCommandsShareOneStoreInstance()
    {
        var app = ContributionTestApp.Create(_default.FilePath);

        var probe = await app.RunAsync(
            "probe", "--non-interactive", "--tq-connection-store-file", _explicitPath.FilePath);

        Assert.Equal((int)ContributionExitCode.Ok, probe.ExitCode);
        Assert.Same(app.Options.Store, app.ProbedStores.Single());
        Assert.Same(app.Options.Store, app.Options.Store);
    }

    [Fact]
    public async Task TwoRunsOfOneAppEachWriteOnlyTheirOwnFile()
    {
        var app = ContributionTestApp.Create(_default.FilePath);

        var first = await app.RunAsync(
            "connections", "add", "first",
            "--non-interactive",
            "--server", "srv-one",
            "--tq-connection-store-file", _explicitPath.FilePath);
        Assert.Equal((int)ContributionExitCode.Ok, first.ExitCode);

        var second = await app.RunAsync(
            "connections", "add", "second",
            "--non-interactive",
            "--server", "srv-two",
            "--tq-connection-store-file", _second.FilePath);
        Assert.Equal((int)ContributionExitCode.Ok, second.ExitCode);

        Assert.Equal(["first"], Names(_explicitPath.FilePath));
        Assert.Equal(["second"], Names(_second.FilePath));
        Assert.False(_default.Exists);

        // The second run replaced the first run's state rather than adding to it.
        Assert.Equal(_second.FilePath, app.Options.ResolvedStorePath!.FilePath);
        Assert.Equal(_second.FilePath, app.Options.Store.FilePath);
    }

    [Fact]
    public async Task TheHostSuppliedPasswordProtectorReachesTheConstructedStore()
    {
        var invocations = 0;
        var app = ContributionTestApp.Create(
            _default.FilePath,
            passwordProtectorFactory: () =>
            {
                invocations++;
                return new MarkerPasswordProtector();
            });

        var probe = await app.RunAsync(
            "probe", "--non-interactive", "--tq-connection-store-file", _explicitPath.FilePath);
        Assert.Equal((int)ContributionExitCode.Ok, probe.ExitCode);

        app.Options.Store.Add(new SqlServerConnectionProfile
        {
            Name = "demo",
            Server = "srv",
            Authentication = AuthenticationType.SqlPassword,
            Username = "sa",
            PlainPassword = "fixture-password"
        });

        // Only the host's protector writes this marker, so finding it proves the factory
        // reached the store the run selected — and it ran once, with the store.
        Assert.Contains(MarkerPasswordProtector.Marker, _explicitPath.ReadJson());
        Assert.DoesNotContain("fixture-password", _explicitPath.ReadJson());
        Assert.Equal(1, invocations);
    }

    /// <summary>
    /// A protector whose output is unmistakable, so a test need not infer which platform
    /// default it is looking at.
    /// </summary>
    private sealed class MarkerPasswordProtector : IConnectionPasswordProtector
    {
        public const string Marker = "protected-by-the-host-factory";

        public void ProtectForSave(SqlServerConnectionProfile profile)
        {
            if (!string.IsNullOrEmpty(profile.PlainPassword))
                profile.EncryptedPassword = Marker;

            profile.PlainPassword = null;
        }

        public void UnprotectAfterLoad(SqlServerConnectionProfile profile)
        {
        }
    }

    // ---- Wiring errors surface as wiring errors ----

    [Fact]
    public void ReachingTheStoreBeforeTheCallbackIsAWiringError()
    {
        var options = new TigerQueryCliOptions { DefaultConnectionStoreFile = _default.FilePath };

        var error = Assert.Throws<InvalidOperationException>(() => options.Store);

        Assert.Contains(TigerQueryCliContribution.ConnectionStoreFileOption, error.Message);
        Assert.Contains(nameof(TigerQueryCliContribution), error.Message);
    }

    [Fact]
    public void ConfiguringNoStoreSelectionIsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildGroup(_ => { }));

        Assert.Contains(nameof(SqlServerConnectionCommandOptions.TigerQuery), error.Message);
    }

    [Fact]
    public void TheSharedOptionsAreTheOnlyStoreInjectionPath()
    {
        // The eager `options.Store` form is gone, so a host cannot pin a store that would
        // ignore whatever the run selected. What remains is one settable store source.
        var storeProperties = typeof(SqlServerConnectionCommandOptions)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(SqlServerConnectionStore))
            .ToList();

        Assert.Empty(storeProperties);
        Assert.NotNull(typeof(SqlServerConnectionCommandOptions).GetProperty(
            nameof(SqlServerConnectionCommandOptions.TigerQuery)));
    }

    // ---- Helpers ----

    private static void BuildGroup(Action<SqlServerConnectionCommandOptions> configure) =>
        TigerCliApp.CreateBuilder()
            .SetApplicationName("tq-wiring-test")
            .UseAppResources(SqlServerConnectionCommands.CreateAppResources())
            .AddCommandGroup("connections", group =>
                SqlServerConnectionCommands.Configure(group, configure))
            .Build();

    private static SqlServerConnectionStore Open(string filePath) =>
        new(new SqlServerConnectionStoreOptions { FilePath = filePath });

    private static void Seed(string filePath, string name, string server) =>
        Open(filePath).Add(new SqlServerConnectionProfile
        {
            Name = name,
            Server = server,
            Authentication = AuthenticationType.Integrated
        });

    private static IEnumerable<string> Names(string filePath) =>
        Open(filePath).Load().Select(profile => profile.Name);
}
