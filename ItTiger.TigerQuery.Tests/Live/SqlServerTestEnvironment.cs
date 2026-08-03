using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.E2e;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Resolves the explicitly configured TigerQuery E2E store and bootstrap profile for
/// SQL-backed tests. It never discovers, probes, or falls back to a SQL Server instance.
/// </summary>
internal static class SqlServerTestEnvironment
{
    internal const string DefaultBootstrapConnectionName = "tiger-sqlcmd-e2e";

    /// <summary>Gets an authorized bootstrap profile or applies the test-only status mapping.</summary>
    public static SqlServerE2eTestConfiguration RequireConfiguration(
        bool requireDatabaseCreation = false)
    {
        var result = Resolve(
            Environment.GetEnvironmentVariable,
            path => new SqlServerConnectionStore(
                new SqlServerConnectionStoreOptions { FilePath = path }),
            requireDatabaseCreation);

        if (result.Store is null)
        {
            if (result.Resolution.Status == SqlServerE2eResolutionStatus.NotConfigured)
                Assert.Skip(string.Join(" ", result.Resolution.Errors));

            Assert.Fail(
                $"TigerQuery E2E configuration is {result.Resolution.Status}: "
                + string.Join(" ", result.Resolution.Errors));
        }

        return RequireResolved(result.Store, result.Resolution);
    }

    /// <summary>Builds the bootstrap connection string without probing it first.</summary>
    public static string RequireConnectionString() =>
        RequireConfiguration().Profile.BuildConnectionString();

    /// <summary>Creates one exactly owned database for a SQL-backed test.</summary>
    public static async Task<SqlServerE2eTestDatabase> CreateDatabaseAsync()
    {
        var configuration = RequireConfiguration(requireDatabaseCreation: true);
        var lifecycle = new SqlServerE2eDatabaseLifecycle(
            configuration.Store,
            configuration.Resolution);

        try
        {
            var databaseName = await lifecycle.CreateDatabaseAsync(
                TestContext.Current.CancellationToken);
            var builder = configuration.Profile.BuildConnectionStringBuilder();
            builder.InitialCatalog = databaseName;
            return new SqlServerE2eTestDatabase(lifecycle, builder.ConnectionString);
        }
        catch
        {
            if (lifecycle.CreatedDatabaseName is not null && !lifecycle.DatabaseWasDropped)
                await lifecycle.CleanupAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Maps Core's neutral result onto the current xUnit suite policy.</summary>
    internal static SqlServerE2eTestConfiguration RequireResolved(
        SqlServerConnectionStore store,
        SqlServerE2eConnectionResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resolution);

        if (resolution.Status == SqlServerE2eResolutionStatus.NotConfigured)
            Assert.Skip(string.Join(" ", resolution.Errors));

        if (resolution.Status is SqlServerE2eResolutionStatus.Invalid
            or SqlServerE2eResolutionStatus.Ambiguous)
        {
            Assert.Fail(
                $"TigerQuery E2E configuration is {resolution.Status}: "
                + string.Join(" ", resolution.Errors));
        }

        if (resolution.Status != SqlServerE2eResolutionStatus.Resolved
            || resolution.Profile is null)
        {
            Assert.Fail($"Unexpected TigerQuery E2E resolution status: {resolution.Status}.");
        }

        return new SqlServerE2eTestConfiguration(store, resolution);
    }

    /// <summary>Resolves test configuration without applying any test-framework outcome.</summary>
    internal static SqlServerE2eTestResolution Resolve(
        Func<string, string?> environmentReader,
        Func<string, SqlServerConnectionStore> storeFactory,
        bool requireDatabaseCreation = false)
    {
        ArgumentNullException.ThrowIfNull(environmentReader);
        ArgumentNullException.ThrowIfNull(storeFactory);

        var configuredStorePath = environmentReader(
            SqlServerConnectionStoreEnvironment.ConnectionStoreFile);
        if (configuredStorePath is null)
        {
            return new SqlServerE2eTestResolution(
                null,
                new SqlServerE2eConnectionResolution
                {
                    Status = SqlServerE2eResolutionStatus.NotConfigured,
                    Errors =
                    [
                        $"Set {SqlServerConnectionStoreEnvironment.ConnectionStoreFile} to an isolated "
                        + "TigerQuery E2E store to run SQL-backed tests."
                    ]
                });
        }

        var path = SqlServerConnectionStorePathResolver.Resolve(
            new SqlServerConnectionStorePathOptions
            {
                DefaultFilePath = Path.Combine(
                    Path.GetTempPath(),
                    "TigerQueryE2eTests",
                    "unselected-default.json"),
                EnvironmentReader = name =>
                    name == SqlServerConnectionStoreEnvironment.ConnectionStoreFile
                        ? configuredStorePath
                        : null
            });
        if (!path.IsSuccess)
        {
            return new SqlServerE2eTestResolution(
                null,
                new SqlServerE2eConnectionResolution
                {
                    Status = SqlServerE2eResolutionStatus.Invalid,
                    Errors = [path.ErrorMessage ?? "The E2E connection-store path is invalid."]
                });
        }

        var store = storeFactory(path.FilePath!);
        var resolution = SqlServerE2eConnectionResolver.Resolve(
            store,
            new SqlServerE2eConnectionResolutionOptions
            {
                DefaultConnectionName = DefaultBootstrapConnectionName,
                RequireDatabaseCreationPermission = requireDatabaseCreation,
                ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional
            });
        return new SqlServerE2eTestResolution(store, resolution);
    }
}

/// <summary>One resolved test-only E2E configuration.</summary>
internal sealed record SqlServerE2eTestConfiguration(
    SqlServerConnectionStore Store,
    SqlServerE2eConnectionResolution Resolution)
{
    public SqlServerConnectionProfile Profile => Resolution.Profile!;
}

/// <summary>A neutral pre-mapping E2E test resolution and its selected store, if any.</summary>
internal sealed record SqlServerE2eTestResolution(
    SqlServerConnectionStore? Store,
    SqlServerE2eConnectionResolution Resolution);

/// <summary>One database owned by one Phase 7 lifecycle instance.</summary>
internal sealed class SqlServerE2eTestDatabase : IAsyncDisposable
{
    public SqlServerE2eTestDatabase(
        SqlServerE2eDatabaseLifecycle lifecycle,
        string connectionString)
    {
        Lifecycle = lifecycle;
        ConnectionString = connectionString;
    }

    public string DatabaseName => Lifecycle.CreatedDatabaseName!;

    public string ConnectionString { get; }

    public SqlServerE2eDatabaseLifecycle Lifecycle { get; }

    public async ValueTask DisposeAsync()
    {
        if (!Lifecycle.DatabaseWasDropped)
            await Lifecycle.CleanupAsync(CancellationToken.None);
    }
}
