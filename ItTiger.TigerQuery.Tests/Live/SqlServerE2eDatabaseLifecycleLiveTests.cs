using ItTiger.TigerQuery.E2e;

namespace ItTiger.TigerQuery.Tests.Live;

[Collection(LiveTestCollection.Name)]
public sealed class SqlServerE2eDatabaseLifecycleLiveTests
{
    [Fact]
    public async Task AuthorizedLifecycleCreatesUsesProfilesAndDropsItsDatabase()
    {
        var configuration = SqlServerTestEnvironment.RequireConfiguration(
            requireDatabaseCreation: true);
        var lifecycle = new SqlServerE2eDatabaseLifecycle(
            configuration.Store,
            configuration.Resolution);
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
        Assert.Null(configuration.Store.Find(databaseName));
    }
}
