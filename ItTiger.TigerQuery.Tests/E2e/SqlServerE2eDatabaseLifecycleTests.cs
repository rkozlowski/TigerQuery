using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.E2e;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.E2e;

public sealed class SqlServerE2eDatabaseLifecycleTests
{
    [Fact]
    public async Task CreateUsesDefaultPrefixUniqueNameAndMasterConnection()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor, "0123456789abcdef0123456789abcdef");

        var name = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);

        Assert.Equal("_TQ_E2E_0123456789abcdef0123456789abcdef", name);
        Assert.Equal(name, lifecycle.CreatedDatabaseName);
        Assert.False(lifecycle.DatabaseWasDropped);
        var operation = Assert.Single(executor.Executions);
        Assert.Equal($"CREATE DATABASE [{name}];", operation.Script);
        Assert.Equal("master", new SqlConnectionStringBuilder(operation.ConnectionString).InitialCatalog);
    }

    [Fact]
    public async Task HostPrefixOverrideIsUsedAndIdentifierIsQuoted()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor, "suffix", "host]");

        var name = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);

        Assert.Equal("host]suffix", name);
        Assert.Equal("CREATE DATABASE [host]]suffix];", Assert.Single(executor.Executions).Script);
    }

    [Fact]
    public async Task CreateRequiresResolvedE2eAndDatabaseCreationAuthorization()
    {
        using var temp = new TempStore();
        var ordinary = Profile("ordinary");
        temp.Store.Add(ordinary);
        var unresolved = SqlServerE2eConnectionResolver.Resolve(
            temp.Store,
            new SqlServerE2eConnectionResolutionOptions
            {
                ConnectionName = ordinary.Name,
                RequireDatabaseCreationPermission = true
            });
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, unresolved, executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken));

        Assert.Empty(executor.Executions);
        Assert.Null(lifecycle.CreatedDatabaseName);
    }

    [Fact]
    public async Task E2eAuthorizationDoesNotImplyDatabaseCreationPermission()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: false);
        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, resolution.Status);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken));

        Assert.Empty(executor.Executions);
    }

    [Fact]
    public async Task FailedCreateRecordsNoOwnership()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var executor = new RecordingExecutor { ExecuteFailure = new IOException("create failed") };
        var lifecycle = Lifecycle(temp, resolution, executor);

        await Assert.ThrowsAsync<IOException>(
            () => lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken));

        Assert.Null(lifecycle.CreatedDatabaseName);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.CleanupAsync(TestContext.Current.CancellationToken));
        Assert.Single(executor.Executions);
    }

    [Fact]
    public async Task PrefixMatchAloneNeverAuthorizesCleanup()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.CleanupAsync(
                "_TQ_E2E_someone_elses_database",
                TestContext.Current.CancellationToken));

        Assert.Empty(executor.Executions);
    }

    [Fact]
    public async Task CleanupRejectsUnrecordedAndDifferentLifecycleNamesBeforeSql()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var firstExecutor = new RecordingExecutor();
        var secondExecutor = new RecordingExecutor();
        var first = Lifecycle(temp, resolution, firstExecutor, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var second = Lifecycle(temp, resolution, secondExecutor, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var firstName = await first.CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var secondName = await second.CreateDatabaseAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => first.CleanupAsync(secondName, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => second.CleanupAsync(firstName, TestContext.Current.CancellationToken));

        Assert.Single(firstExecutor.Executions);
        Assert.Single(secondExecutor.Executions);
    }

    [Fact]
    public async Task CleanupDropsOnlyRecordedNameAndRetainsOwnershipHistory()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var name = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);

        await lifecycle.CleanupAsync(name, TestContext.Current.CancellationToken);

        Assert.True(lifecycle.DatabaseWasDropped);
        Assert.Equal(name, lifecycle.CreatedDatabaseName);
        Assert.Equal($"DROP DATABASE [{name}];", executor.Executions[1].Script);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.CleanupAsync(name, TestContext.Current.CancellationToken));
        Assert.Equal(2, executor.Executions.Count);
    }

    [Fact]
    public async Task CleanupFailureNamesExactDatabaseAndRetainsRetryState()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var name = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);
        executor.ExecuteFailure = new IOException("drop failed");

        var failure = await Assert.ThrowsAsync<SqlServerE2eDatabaseCleanupException>(
            () => lifecycle.CleanupAsync(TestContext.Current.CancellationToken));

        Assert.Equal(name, failure.DatabaseName);
        Assert.Contains(name, failure.Message, StringComparison.Ordinal);
        Assert.False(lifecycle.DatabaseWasDropped);
        Assert.Equal(name, lifecycle.CreatedDatabaseName);

        executor.ExecuteFailure = null;
        await lifecycle.CleanupAsync(TestContext.Current.CancellationToken);
        Assert.True(lifecycle.DatabaseWasDropped);
    }

    [Fact]
    public async Task CleanupUsesTheProfileAuthorizedAtCreationIfTheStoreIsEdited()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var name = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);
        Assert.True(temp.Store.Delete("bootstrap"));

        await lifecycle.CleanupAsync(TestContext.Current.CancellationToken);

        Assert.True(lifecycle.DatabaseWasDropped);
        Assert.Equal($"DROP DATABASE [{name}];", executor.Executions[1].Script);
    }

    [Fact]
    public async Task ProfileCopyUsesStorePathAndIsRemovedOnlyAfterSuccessfulDrop()
    {
        using var temp = new TempStore();
        var bootstrap = Profile("bootstrap");
        bootstrap.SetMetadata("suite.owner", "tests");
        var resolution = temp.SeedAndResolve(bootstrap, allowCreate: true);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var name = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);

        var copy = lifecycle.AddDatabaseProfile("generated-profile");

        Assert.Equal("generated-profile", lifecycle.CreatedProfileName);
        Assert.Equal(name, copy.Database);
        Assert.Equal("tests", copy.Metadata["suite.owner"]);
        Assert.NotNull(temp.Store.Find("generated-profile"));

        executor.ExecuteFailure = new IOException("still in use");
        await Assert.ThrowsAsync<SqlServerE2eDatabaseCleanupException>(
            () => lifecycle.CleanupAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(temp.Store.Find("generated-profile"));

        executor.ExecuteFailure = null;
        await lifecycle.CleanupAsync(TestContext.Current.CancellationToken);
        Assert.Null(temp.Store.Find("generated-profile"));
        Assert.Null(lifecycle.CreatedProfileName);
    }

    [Fact]
    public async Task SetupAndTeardownUseRecordedDatabaseAndExistingExecutionPath()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var name = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);

        await lifecycle.RunSetupSqlAsync(
            "CREATE TABLE dbo.Items(Id int);",
            TestContext.Current.CancellationToken);
        await lifecycle.RunTeardownSqlAsync(
            "DROP TABLE dbo.Items;",
            TestContext.Current.CancellationToken);

        Assert.Equal("CREATE TABLE dbo.Items(Id int);", executor.Executions[1].Script);
        Assert.Equal("DROP TABLE dbo.Items;", executor.Executions[2].Script);
        Assert.All(
            executor.Executions.Skip(1),
            operation => Assert.Equal(
                name,
                new SqlConnectionStringBuilder(operation.ConnectionString).InitialCatalog));
    }

    [Fact]
    public async Task FullConnectionStringReferenceResolvesLazilyWithoutWriteBack()
    {
        using var temp = new TempStore();
        var profile = new SqlServerConnectionProfile
        {
            Name = "bootstrap",
            ConnectionStringValue = SqlServerConnectionValue.External(
                new SqlServerExternalValueReference
                {
                    Source = SqlServerExternalValueSource.EnvironmentVariable,
                    Name = "TQ_BOOTSTRAP"
                })
        };
        SqlServerE2eMetadata.AuthorizeNewProfile(profile, allowDatabaseCreation: true);
        temp.Store.Add(profile);
        var resolution = Resolve(temp, profile.Name, requireCreate: true);
        var reads = 0;
        var executor = new RecordingExecutor();
        var options = new SqlServerE2eDatabaseLifecycleOptions
        {
            ExternalValues = new SqlServerExternalValueResolutionOptions
            {
                EnvironmentReader = name =>
                {
                    reads++;
                    Assert.Equal("TQ_BOOTSTRAP", name);
                    return "Server=sql01;Database=ignored;Integrated Security=true;Encrypt=false";
                }
            }
        };
        var lifecycle = new SqlServerE2eDatabaseLifecycle(
            temp.Store,
            resolution,
            options,
            executor,
            () => "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.Equal(0, reads);
        var name = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, reads);
        await lifecycle.RunSetupSqlAsync("SELECT 1;", TestContext.Current.CancellationToken);
        Assert.Equal(2, reads);
        Assert.Equal(name, new SqlConnectionStringBuilder(executor.Executions[1].ConnectionString).InitialCatalog);

        Assert.Throws<InvalidOperationException>(() => lifecycle.AddDatabaseProfile("full-mode-copy"));
        Assert.Null(temp.Store.Find("full-mode-copy"));

        var persisted = temp.Store.Find("bootstrap")!;
        Assert.True(persisted.ConnectionStringValue!.IsReference);
        Assert.Equal("TQ_BOOTSTRAP", persisted.ConnectionStringValue.Reference!.Name);
    }

    [Fact]
    public async Task OrphanDetectionReportsCandidatesButNeverDeletesThem()
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, resolution, executor, "ownedownedownedownedownedowned12");
        var owned = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);
        executor.QueryResult = ["ordinary", owned, "_TQ_E2E_orphan_b", "_TQ_E2E_orphan_a", "_TQ_E2E_orphan_a"];

        var report = await lifecycle.DetectOrphansAsync(TestContext.Current.CancellationToken);

        Assert.Equal("_TQ_E2E_", report.DatabasePrefix);
        Assert.Equal(["_TQ_E2E_orphan_a", "_TQ_E2E_orphan_b"], report.DatabaseNames);
        Assert.Single(executor.Executions);
        Assert.Single(executor.Queries);
        Assert.DoesNotContain("DROP DATABASE", executor.Queries[0].Script, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidPrefixFailsBeforeAnyOperation(string prefix)
    {
        using var temp = new TempStore();
        var resolution = temp.SeedAndResolve(Profile("bootstrap"), allowCreate: true);

        Assert.Throws<ArgumentException>(() => new SqlServerE2eDatabaseLifecycle(
            temp.Store,
            resolution,
            new SqlServerE2eDatabaseLifecycleOptions { DatabasePrefix = prefix }));
    }

    private static SqlServerE2eDatabaseLifecycle Lifecycle(
        TempStore temp,
        SqlServerE2eConnectionResolution resolution,
        RecordingExecutor executor,
        string suffix = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        string prefix = SqlServerE2eDatabaseLifecycleOptions.DefaultDatabasePrefix) =>
        new(
            temp.Store,
            resolution,
            new SqlServerE2eDatabaseLifecycleOptions { DatabasePrefix = prefix },
            executor,
            () => suffix);

    private static SqlServerE2eConnectionResolution Resolve(
        TempStore temp,
        string name,
        bool requireCreate) =>
        SqlServerE2eConnectionResolver.Resolve(
            temp.Store,
            new SqlServerE2eConnectionResolutionOptions
            {
                ConnectionName = name,
                RequireDatabaseCreationPermission = requireCreate
            });

    private static SqlServerConnectionProfile Profile(string name) => new()
    {
        Name = name,
        Server = "sql01",
        Authentication = AuthenticationType.Integrated,
        Encrypt = EncryptOption.Optional
    };

    private sealed class RecordingExecutor : ISqlServerE2eDatabaseExecutor
    {
        public List<Operation> Executions { get; } = [];

        public List<Operation> Queries { get; } = [];

        public IReadOnlyList<string> QueryResult { get; set; } = [];

        public Exception? ExecuteFailure { get; set; }

        public void ClearPool(string connectionString)
        {
        }

        public Task ExecuteAsync(
            string connectionString,
            string script,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Executions.Add(new Operation(connectionString, script));
            return ExecuteFailure is null
                ? Task.CompletedTask
                : Task.FromException(ExecuteFailure);
        }

        public Task<IReadOnlyList<string>> QueryNamesAsync(
            string connectionString,
            string script,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(new Operation(connectionString, script));
            return Task.FromResult(QueryResult);
        }
    }

    private sealed record Operation(string ConnectionString, string Script);

    private sealed class TempStore : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "TigerQueryE2eDatabaseLifecycleTests",
            Guid.NewGuid().ToString("N"));

        public TempStore()
        {
            Directory.CreateDirectory(directory);
            Store = new SqlServerConnectionStore(
                new SqlServerConnectionStoreOptions
                {
                    FilePath = Path.Combine(directory, "connections.json")
                },
                new NoOpConnectionPasswordProtector());
        }

        public SqlServerConnectionStore Store { get; }

        public SqlServerE2eConnectionResolution SeedAndResolve(
            SqlServerConnectionProfile profile,
            bool allowCreate)
        {
            SqlServerE2eMetadata.AuthorizeNewProfile(profile, allowCreate);
            Store.Add(profile);
            return Resolve(this, profile.Name, requireCreate: false);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
