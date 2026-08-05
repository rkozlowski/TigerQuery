using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.E2e;

namespace ItTiger.TigerQuery.Tests.E2e;

public sealed class SqlServerE2eSessionLifecycleTests
{
    private static readonly Guid Session = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task CreateUsesSharedNamePartFixedPrefixesAndPairedMetadata()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, executor, "abcdef0123456789abcdef0123456789");

        var result = await lifecycle.CreateAsync(
            Session, "suite one", "suite one", TestContext.Current.CancellationToken);

        Assert.Equal("_TQ_E2E_suite-one_abcdef0123456789abcdef0123456789", result.DatabaseName);
        Assert.Equal("E2E-suite-one-abcdef0123456789abcdef0123456789", result.ConnectionName);
        Assert.Equal($"CREATE DATABASE [{result.DatabaseName}];", Assert.Single(executor.Executions));
        var profile = temp.Store.Find(result.ConnectionName)!;
        Assert.Equal(result.DatabaseName, profile.Database);
        AssertSessionMetadata(profile, result.DatabaseName, allowDrop: true);
    }

    [Fact]
    public async Task CreateUsesSeparateNamePartOverrides()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        var lifecycle = Lifecycle(temp, new RecordingExecutor(), "suffix");

        var result = await lifecycle.CreateAsync(
            Session, "database part", "connection part", TestContext.Current.CancellationToken);

        Assert.Equal("_TQ_E2E_database-part_suffix", result.DatabaseName);
        Assert.Equal("E2E-connection-part-suffix", result.ConnectionName);
    }

    [Fact]
    public async Task PersistenceFailureRollsBackOnlyTheExactCreatedDatabase()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, executor, "rollback");
        temp.Store.WriteFaultHook = _ => throw new IOException("store unavailable");

        var failure = await Assert.ThrowsAsync<SqlServerE2eCreateException>(
            () => lifecycle.CreateAsync(
                Session, "db", "connection", TestContext.Current.CancellationToken));

        Assert.True(failure.RollbackSucceeded);
        Assert.Equal("_TQ_E2E_db_rollback", failure.DatabaseName);
        Assert.Equal(
            ["CREATE DATABASE [_TQ_E2E_db_rollback];", "DROP DATABASE [_TQ_E2E_db_rollback];"],
            executor.Executions);
        Assert.Null(temp.Store.Find("E2E-connection-rollback"));
    }

    [Fact]
    public void CloneTargetsExistingDatabasePreservesReferencesAndDoesNotOwnDatabase()
    {
        using var temp = new TempStore();
        var source = new SqlServerConnectionProfile
        {
            Name = "source",
            ServerValue = EnvironmentReference("SQL_SERVER"),
            Database = "old",
            Authentication = AuthenticationType.SqlPassword,
            UsernameValue = EnvironmentReference("SQL_USER"),
            PasswordValue = EnvironmentReference("SQL_PASSWORD")
        };
        temp.Store.Add(source);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, executor, "clone");

        var clone = lifecycle.CloneForExistingDatabase("source", "ReadOnlyDb", Session, "readonly");

        Assert.Equal("E2E-readonly-clone", clone.Name);
        Assert.Equal("ReadOnlyDb", clone.Database);
        Assert.Equal("SQL_SERVER", clone.ServerValue.Reference!.Name);
        Assert.Equal("SQL_USER", clone.UsernameValue!.Reference!.Name);
        Assert.Equal("SQL_PASSWORD", clone.PasswordValue!.Reference!.Name);
        AssertSessionMetadata(clone, "ReadOnlyDb", allowDrop: false);
        Assert.Empty(executor.Executions);
        Assert.Empty(executor.Queries);
    }

    [Fact]
    public void CloneRejectsAnExistingGeneratedDestinationName()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "E2E-clone-collision",
            Server = "sql01"
        });

        var error = Assert.Throws<InvalidOperationException>(() =>
            Lifecycle(temp, new RecordingExecutor(), "collision")
                .CloneForExistingDatabase("bootstrap", "ExistingDb", Session, "clone"));

        Assert.Contains("already exists", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CloneRetargetsAnUnresolvedFullConnectionStringReferenceWithoutWritingItBack()
    {
        using var temp = new TempStore();
        temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "full-source",
            ConnectionStringValue = EnvironmentReference("SQL_CONNECTION_STRING")
        });

        var clone = Lifecycle(temp, new RecordingExecutor(), "full")
            .CloneForExistingDatabase("full-source", "ExistingDb", Session, "clone");
        var reloaded = temp.Store.Find(clone.Name)!;
        var builder = reloaded.BuildConnectionStringBuilder(
            new SqlServerExternalValueResolutionOptions
            {
                EnvironmentReader = _ =>
                    "Server=sql01;Database=OldDb;Integrated Security=true;Encrypt=false"
            });

        Assert.True(reloaded.ConnectionStringValue!.IsReference);
        Assert.Equal("SQL_CONNECTION_STRING", reloaded.ConnectionStringValue.Reference!.Name);
        Assert.Equal("ExistingDb", builder.InitialCatalog);
    }

    [Fact]
    public async Task OwningDropDropsExactRecordedDatabaseThenRemovesConnection()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.CopyForE2eSession(
            "bootstrap", "owning", "_TQ_E2E_owned_suffix", Session, true);
        var executor = new RecordingExecutor();
        executor.ExistingDatabases.Add("_TQ_E2E_owned_suffix");

        var result = await Lifecycle(temp, executor).DropAsync(
            "owning", Session, TestContext.Current.CancellationToken);

        Assert.Equal(SqlServerE2eDropDisposition.DatabaseDroppedAndConnectionRemoved, result.Disposition);
        Assert.Equal(ForcedDropScript("_TQ_E2E_owned_suffix"), Assert.Single(executor.Executions));
        Assert.Equal(
            new Dictionary<string, string> { ["@databaseName"] = "_TQ_E2E_owned_suffix" },
            Assert.Single(executor.Parameters));
        Assert.Null(temp.Store.Find("owning"));
    }

    /// <summary>
    /// The teardown is one batch on one connection so that no other session can take the
    /// single-user slot between the forced rollback and the drop, the existence test is a
    /// bound parameter, and only the exact owned name is ever quoted into an identifier.
    /// </summary>
    [Fact]
    public async Task OwningDropForcesSingleUserAndDropsInOneGuardedBatch()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.CopyForE2eSession(
            "bootstrap", "owning", "_TQ_E2E_owned_suffix", Session, true);
        var executor = new RecordingExecutor();
        executor.ExistingDatabases.Add("_TQ_E2E_owned_suffix");

        await Lifecycle(temp, executor).DropAsync(
            "owning", Session, TestContext.Current.CancellationToken);

        var script = Assert.Single(executor.Executions);
        Assert.Equal("""
            USE [master];

            IF DB_ID(@databaseName) IS NOT NULL
            BEGIN
                ALTER DATABASE [_TQ_E2E_owned_suffix]
                    SET SINGLE_USER
                    WITH ROLLBACK IMMEDIATE;

                DROP DATABASE [_TQ_E2E_owned_suffix];
            END
            """,
            script);
        Assert.DoesNotContain("GO", script, StringComparison.Ordinal);
        Assert.Equal("_TQ_E2E_owned_suffix", Assert.Single(executor.Parameters)["@databaseName"]);

        // The pool is returned before SQL Server is asked to disconnect everybody else.
        Assert.Single(executor.ClearedPools);
    }

    [Fact]
    public async Task OwningDropOfAnAbsentDatabaseStillRemovesTheOwningConnection()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.CopyForE2eSession(
            "bootstrap", "owning", "_TQ_E2E_gone_suffix", Session, true);
        var executor = new RecordingExecutor();

        var result = await Lifecycle(temp, executor).DropAsync(
            "owning", Session, TestContext.Current.CancellationToken);

        Assert.Equal(SqlServerE2eDropDisposition.DatabaseAbsentAndConnectionRemoved, result.Disposition);
        Assert.Empty(executor.Executions);
        Assert.Null(temp.Store.Find("owning"));
    }

    [Fact]
    public async Task AFailedForcedDropPreservesTheOwningConnectionRecord()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.CopyForE2eSession(
            "bootstrap", "owning", "_TQ_E2E_stuck_suffix", Session, true);
        var executor = new RecordingExecutor { FailDropContaining = "_TQ_E2E_stuck_suffix" };
        executor.ExistingDatabases.Add("_TQ_E2E_stuck_suffix");

        await Assert.ThrowsAsync<IOException>(() => Lifecycle(temp, executor).DropAsync(
            "owning", Session, TestContext.Current.CancellationToken));

        var owning = temp.Store.Find("owning");
        Assert.NotNull(owning);
        Assert.Equal(
            "_TQ_E2E_stuck_suffix",
            owning.Metadata[SqlServerE2eMetadata.DatabaseName]);
        Assert.Equal(
            SqlServerE2eMetadata.True,
            owning.Metadata[SqlServerE2eMetadata.AllowDatabaseDrop]);
    }

    /// <summary>
    /// The forced path is reserved for owned databases. A clone records
    /// <c>allow-drop=false</c>, and cleaning it up must not emit any SQL at all — not a
    /// drop, and not the mode change that would disconnect that database's real users.
    /// </summary>
    [Fact]
    public async Task NonOwningCleanupNeverAltersOrDropsItsTargetDatabase()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.CopyForE2eSession("bootstrap", "clone", "SharedProductionish", Session, false);
        var executor = new RecordingExecutor();
        executor.ExistingDatabases.Add("SharedProductionish");

        var result = await Lifecycle(temp, executor).DropAsync(
            "clone", Session, TestContext.Current.CancellationToken);

        Assert.Equal(SqlServerE2eDropDisposition.ConnectionRemoved, result.Disposition);
        Assert.Empty(executor.Executions);
        Assert.Empty(executor.Queries);
        Assert.Empty(executor.ClearedPools);
        Assert.Null(temp.Store.Find("clone"));
    }

    [Fact]
    public async Task DropRequiresExactSessionAndNeverDropsNonOwningDatabase()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.CopyForE2eSession("bootstrap", "E2E-existing-clone", "ExistingDb", Session, false);
        var executor = new RecordingExecutor();
        var lifecycle = Lifecycle(temp, executor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.DropAsync(
            "E2E-existing-clone",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            TestContext.Current.CancellationToken));
        Assert.NotNull(temp.Store.Find("E2E-existing-clone"));

        var result = await lifecycle.DropAsync(
            "E2E-existing-clone", Session, TestContext.Current.CancellationToken);

        Assert.Equal(SqlServerE2eDropDisposition.ConnectionRemoved, result.Disposition);
        Assert.Null(temp.Store.Find("E2E-existing-clone"));
        Assert.Empty(executor.Executions);
        Assert.Empty(executor.Queries);
    }

    [Fact]
    public async Task CleanupDropsOnlyOwningDatabaseAndRemovesNonOwningConnection()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.CopyForE2eSession("bootstrap", "owning", "_TQ_E2E_owned_suffix", Session, true);
        temp.Store.CopyForE2eSession("bootstrap", "non-owning", "ExistingDb", Session, false);
        temp.Store.CopyForE2eSession(
            "bootstrap",
            "other-session",
            "_TQ_E2E_other_suffix",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            true);
        var executor = new RecordingExecutor();
        executor.ExistingDatabases.Add("_TQ_E2E_owned_suffix");
        executor.ExistingDatabases.Add("ExistingDb");
        executor.ExistingDatabases.Add("_TQ_E2E_other_suffix");
        var result = await Lifecycle(temp, executor).CleanupAsync(
            Session, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(ForcedDropScript("_TQ_E2E_owned_suffix"), executor.Executions);
        Assert.DoesNotContain(executor.Executions, sql => sql.Contains("ExistingDb", StringComparison.Ordinal));
        Assert.DoesNotContain(executor.Executions, sql => sql.Contains("other", StringComparison.Ordinal));
        Assert.Null(temp.Store.Find("owning"));
        Assert.Null(temp.Store.Find("non-owning"));
        Assert.NotNull(temp.Store.Find("other-session"));
    }

    [Fact]
    public async Task CleanupContinuesAfterFailureAndReportsIncomplete()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.CopyForE2eSession("bootstrap", "fails", "_TQ_E2E_fails_suffix", Session, true);
        temp.Store.CopyForE2eSession("bootstrap", "succeeds", "ExistingDb", Session, false);
        var executor = new RecordingExecutor { FailDropContaining = "fails" };
        executor.ExistingDatabases.Add("_TQ_E2E_fails_suffix");

        var result = await Lifecycle(temp, executor).CleanupAsync(
            Session, TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        var failure = Assert.Single(result.Items, item => !item.IsSuccess);
        Assert.Equal("fails", failure.ConnectionName);
        Assert.Equal("drop failed", failure.Error);

        // The later candidate was still attempted after the earlier one failed.
        Assert.Contains(result.Items, item => item.ConnectionName == "succeeds" && item.IsSuccess);
        Assert.NotNull(temp.Store.Find("fails"));
        Assert.Null(temp.Store.Find("succeeds"));
    }

    [Fact]
    public void RegularDeleteRejectsOwningButPermitsNonOwningConnections()
    {
        using var temp = new TempStore();
        temp.AddBootstrap();
        temp.Store.CopyForE2eSession("bootstrap", "owning", "_TQ_E2E_owned_suffix", Session, true);
        temp.Store.CopyForE2eSession("bootstrap", "non-owning", "ExistingDb", Session, false);

        var error = Assert.Throws<InvalidOperationException>(() => temp.Store.Delete("owning"));

        Assert.Contains("tiger-sqlcmd e2e drop", error.Message, StringComparison.Ordinal);
        Assert.Contains("tiger-sqlcmd e2e cleanup", error.Message, StringComparison.Ordinal);
        Assert.True(temp.Store.Delete("non-owning"));
        Assert.NotNull(temp.Store.Find("owning"));
    }

    /// <summary>The exact guarded teardown batch the lifecycle is expected to submit.</summary>
    private static string ForcedDropScript(string databaseName) => $"""
        USE [master];

        IF DB_ID(@databaseName) IS NOT NULL
        BEGIN
            ALTER DATABASE [{databaseName}]
                SET SINGLE_USER
                WITH ROLLBACK IMMEDIATE;

            DROP DATABASE [{databaseName}];
        END
        """;

    private static SqlServerE2eSessionLifecycle Lifecycle(
        TempStore temp,
        RecordingExecutor executor,
        string suffix = "suffix") =>
        new(
            temp.Store,
            "bootstrap",
            new SqlServerE2eDatabaseLifecycleOptions(),
            executor,
            () => suffix);

    private static void AssertSessionMetadata(
        SqlServerConnectionProfile profile,
        string databaseName,
        bool allowDrop)
    {
        Assert.Equal(SqlServerE2eMetadata.True, profile.Metadata[SqlServerE2eMetadata.Enabled]);
        Assert.Equal(SqlServerE2eMetadata.False, profile.Metadata[SqlServerE2eMetadata.Bootstrap]);
        Assert.Equal(SqlServerE2eMetadata.False, profile.Metadata[SqlServerE2eMetadata.AllowDatabaseCreation]);
        Assert.Equal(Session.ToString("D"), profile.Metadata[SqlServerE2eMetadata.SessionId]);
        Assert.Equal(databaseName, profile.Metadata[SqlServerE2eMetadata.DatabaseName]);
        Assert.Equal(
            allowDrop ? SqlServerE2eMetadata.True : SqlServerE2eMetadata.False,
            profile.Metadata[SqlServerE2eMetadata.AllowDatabaseDrop]);
    }

    private static SqlServerConnectionValue EnvironmentReference(string name) =>
        SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.EnvironmentVariable,
            Name = name
        });

    private sealed class RecordingExecutor : ISqlServerE2eDatabaseExecutor
    {
        private static readonly IReadOnlyDictionary<string, string> NoParameters =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public List<string> Executions { get; } = [];
        public List<IReadOnlyDictionary<string, string>> Parameters { get; } = [];
        public List<string> ClearedPools { get; } = [];
        public List<string> Queries { get; } = [];
        public HashSet<string> ExistingDatabases { get; } = new(StringComparer.Ordinal);
        public string? FailDropContaining { get; init; }

        public void ClearPool(string connectionString) => ClearedPools.Add(connectionString);

        public Task ExecuteAsync(string connectionString, string script, CancellationToken cancellationToken) =>
            ExecuteAsync(connectionString, script, NoParameters, cancellationToken);

        public Task ExecuteAsync(
            string connectionString,
            string script,
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken)
        {
            Executions.Add(script);
            Parameters.Add(parameters);
            if (script.Contains("DROP DATABASE", StringComparison.Ordinal)
                && FailDropContaining is not null
                && script.Contains(FailDropContaining, StringComparison.Ordinal))
                return Task.FromException(new IOException("drop failed"));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> QueryNamesAsync(
            string connectionString,
            string script,
            CancellationToken cancellationToken)
        {
            Queries.Add(script);
            IReadOnlyList<string> result = ExistingDatabases
                .Where(name => script.Contains(name.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal))
                .ToList();
            return Task.FromResult(result);
        }
    }

    private sealed class TempStore : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            nameof(SqlServerE2eSessionLifecycleTests),
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

        public void AddBootstrap()
        {
            var profile = new SqlServerConnectionProfile
            {
                Name = "bootstrap",
                Server = "sql01",
                Authentication = AuthenticationType.Integrated
            };
            SqlServerE2eMetadata.AuthorizeNewBootstrapProfile(profile, allowDatabaseCreation: true);
            Store.Add(profile);
        }

        public void Dispose()
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
        }
    }
}
