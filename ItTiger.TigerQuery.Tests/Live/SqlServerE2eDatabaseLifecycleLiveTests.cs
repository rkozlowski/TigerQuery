using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.E2e;

namespace ItTiger.TigerQuery.Tests.Live;

[Collection(LiveTestCollection.Name)]
public sealed class SqlServerE2eDatabaseLifecycleLiveTests
{
    [Fact]
    public async Task SessionLifecycleCreatesPairedOwnerAndCleansMixedOwnership()
    {
        var configuration = SqlServerTestEnvironment.RequireConfiguration(
            requireDatabaseCreation: true);
        var sessionId = Guid.NewGuid();
        var lifecycle = new SqlServerE2eSessionLifecycle(
            configuration.Store,
            configuration.Profile.Name);
        SqlServerE2eCreateResult? created = null;
        SqlServerConnectionProfile? clone = null;

        try
        {
            created = await lifecycle.CreateAsync(
                sessionId,
                "live-session",
                "live-session",
                TestContext.Current.CancellationToken);
            clone = lifecycle.CloneForExistingDatabase(
                configuration.Profile.Name,
                "master",
                sessionId,
                "live-existing");

            var owner = configuration.Store.Find(created.ConnectionName)!;
            Assert.Equal(created.DatabaseName, owner.Database);
            Assert.Equal(
                SqlServerE2eMetadata.True,
                owner.Metadata[SqlServerE2eMetadata.AllowDatabaseDrop]);
            Assert.Equal(
                SqlServerE2eMetadata.False,
                clone.Metadata[SqlServerE2eMetadata.AllowDatabaseDrop]);

            var cleanup = await lifecycle.CleanupAsync(
                sessionId,
                TestContext.Current.CancellationToken);
            Assert.True(cleanup.IsComplete);
            Assert.Contains(
                cleanup.Items,
                item => item.Disposition
                    == SqlServerE2eDropDisposition.DatabaseDroppedAndConnectionRemoved);
            Assert.Contains(
                cleanup.Items,
                item => item.Disposition == SqlServerE2eDropDisposition.ConnectionRemoved);
        }
        finally
        {
            if ((created is not null && configuration.Store.Find(created.ConnectionName) is not null)
                || (clone is not null && configuration.Store.Find(clone.Name) is not null))
            {
                await lifecycle.CleanupAsync(sessionId, CancellationToken.None);
            }
        }

        Assert.Null(configuration.Store.Find(created!.ConnectionName));
        Assert.Null(configuration.Store.Find(clone!.Name));
    }

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
