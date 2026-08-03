using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.E2e;

namespace ItTiger.TigerQuery.Tests.Live;

public sealed class SqlServerE2eDatabaseLifecycleLiveTests
{
    private const string BootstrapName = "tiger-sqlcmd-e2e";

    [Fact]
    public async Task AuthorizedLifecycleCreatesUsesProfilesAndDropsItsDatabase()
    {
        var storePath = Environment.GetEnvironmentVariable(
            SqlServerConnectionStoreEnvironment.ConnectionStoreFile);
        if (string.IsNullOrWhiteSpace(storePath))
        {
            Assert.Skip(
                $"Set {SqlServerConnectionStoreEnvironment.ConnectionStoreFile} to an isolated "
                + "E2E store to run the database lifecycle test.");
        }

        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = storePath });
        var resolution = SqlServerE2eConnectionResolver.Resolve(
            store,
            new SqlServerE2eConnectionResolutionOptions
            {
                DefaultConnectionName = BootstrapName,
                RequireDatabaseCreationPermission = true
            });

        if (resolution.Status == SqlServerE2eResolutionStatus.NotConfigured)
            Assert.Skip(string.Join(" ", resolution.Errors));

        Assert.True(
            resolution.Status == SqlServerE2eResolutionStatus.Resolved,
            $"E2E bootstrap resolution was {resolution.Status}: {string.Join(" ", resolution.Errors)}");

        var lifecycle = new SqlServerE2eDatabaseLifecycle(store, resolution);
        string? databaseName = null;
        try
        {
            databaseName = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);
            var generated = lifecycle.AddDatabaseProfile();
            Assert.Equal(databaseName, generated.Name);
            Assert.Equal(databaseName, generated.Database);

            await lifecycle.RunSetupSqlAsync(
                "CREATE TABLE dbo.Phase7Probe (Id int NOT NULL);",
                TestContext.Current.CancellationToken);
            await lifecycle.RunTeardownSqlAsync(
                "DROP TABLE dbo.Phase7Probe;",
                TestContext.Current.CancellationToken);
        }
        finally
        {
            if (databaseName is not null && !lifecycle.DatabaseWasDropped)
                await lifecycle.CleanupAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(lifecycle.DatabaseWasDropped);
        Assert.Null(store.Find(databaseName));
    }
}
