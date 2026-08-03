using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.E2e;
using ItTiger.TigerQuery.Tests.Cli;
using ItTiger.TigerSqlCmd;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Live;

public sealed class TigerSqlCmdE2eWorkflowLiveTests : IDisposable
{
    private const string BootstrapName = "phase8-external-bootstrap";
    private const string ServerVariable = "TQ_PHASE8_SERVER";
    private const string UsernameVariable = "TQ_PHASE8_USERNAME";
    private const string PasswordVariable = "TQ_PHASE8_PASSWORD";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "TigerSqlCmdE2eWorkflowLiveTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompleteExternalProcessWorkflowUsesExactOwnershipAndRedactsReferences()
    {
        var hostConfiguration = SqlServerTestEnvironment.RequireConfiguration(
            requireDatabaseCreation: true);
        var effectiveBootstrap = hostConfiguration.Profile.BuildConnectionStringBuilder();
        Directory.CreateDirectory(directory);

        var values = BuildExternalValues(effectiveBootstrap);
        var storePath = Path.Combine(directory, "connections.json");
        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = storePath },
            new NoOpConnectionPasswordProtector());
        var bootstrap = ExternalBootstrap(effectiveBootstrap);
        SqlServerE2eMetadata.AuthorizeNewProfile(bootstrap, allowDatabaseCreation: true);
        store.Add(bootstrap);

        var resolution = SqlServerE2eConnectionResolver.Resolve(
            store,
            new SqlServerE2eConnectionResolutionOptions
            {
                ConnectionName = BootstrapName,
                RequireDatabaseCreationPermission = true
            });
        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, resolution.Status);

        var lifecycleOptions = new SqlServerE2eDatabaseLifecycleOptions
        {
            ExternalValues = new SqlServerExternalValueResolutionOptions
            {
                EnvironmentReader = name => values.TryGetValue(name, out var value) ? value : null
            }
        };
        var lifecycle = new SqlServerE2eDatabaseLifecycle(store, resolution, lifecycleOptions);
        var orphanLifecycle = new SqlServerE2eDatabaseLifecycle(store, resolution, lifecycleOptions);
        string? databaseName = null;
        string? orphanName = null;

        try
        {
            databaseName = await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);
            var profileName = "phase8-generated-" + Guid.NewGuid().ToString("N");
            var generated = lifecycle.AddDatabaseProfile(profileName);
            Assert.Equal(databaseName, generated.Database);
            Assert.True(generated.ServerValue.IsReference);
            if (!effectiveBootstrap.IntegratedSecurity)
                Assert.True(generated.PasswordValue!.IsReference);

            var processEnvironment = values.ToDictionary(
                item => item.Key,
                item => (string?)item.Value,
                StringComparer.Ordinal);
            processEnvironment[SqlServerConnectionStoreEnvironment.ConnectionStoreFile] = storePath;

            var setup = await RunProcessAsync(
                processEnvironment,
                "run", "--non-interactive",
                "--connection", profileName,
                "--query",
                "CREATE TABLE dbo.Phase8Probe (Id int NOT NULL, Marker nvarchar(50) NOT NULL); "
                    + "INSERT INTO dbo.Phase8Probe VALUES (1, N'created-through-process');",
                "--verbosity", "Silent");
            AssertProcessSuccess(setup, SensitiveValues(values));

            var outputPath = Path.Combine(directory, "phase8.csv");
            var query = await RunProcessAsync(
                processEnvironment,
                "run", "--non-interactive",
                "--connection", profileName,
                "--query", "SELECT Marker FROM dbo.Phase8Probe ORDER BY Id;",
                "--output", outputPath,
                "--verbosity", "Silent");
            AssertProcessSuccess(query, SensitiveValues(values));
            Assert.Contains("created-through-process", File.ReadAllText(outputPath), StringComparison.Ordinal);

            orphanName = await orphanLifecycle.CreateDatabaseAsync(
                TestContext.Current.CancellationToken);
            var report = await lifecycle.DetectOrphansAsync(TestContext.Current.CancellationToken);
            Assert.Contains(orphanName, report.DatabaseNames);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => lifecycle.CleanupAsync(orphanName, TestContext.Current.CancellationToken));
            await orphanLifecycle.RunSetupSqlAsync(
                "SELECT 1;",
                TestContext.Current.CancellationToken);
            await orphanLifecycle.CleanupAsync(TestContext.Current.CancellationToken);
            orphanName = null;

            var teardown = await RunProcessAsync(
                processEnvironment,
                "run", "--non-interactive",
                "--connection", profileName,
                "--query", "DROP TABLE dbo.Phase8Probe;",
                "--verbosity", "Silent");
            AssertProcessSuccess(teardown, SensitiveValues(values));

            var databaseConnection = lifecycleOptions.ExternalValues is null
                ? generated.BuildConnectionStringBuilder()
                : generated.BuildConnectionStringBuilder(lifecycleOptions.ExternalValues);
            await using var blocker = new SqlConnection(databaseConnection.ConnectionString);
            await blocker.OpenAsync(TestContext.Current.CancellationToken);

            var cleanupFailure = await Assert.ThrowsAsync<SqlServerE2eDatabaseCleanupException>(
                () => lifecycle.CleanupAsync(TestContext.Current.CancellationToken));
            Assert.Equal(databaseName, cleanupFailure.DatabaseName);
            Assert.Contains(databaseName, cleanupFailure.Message, StringComparison.Ordinal);
            Assert.NotNull(store.Find(profileName));

            await blocker.CloseAsync();
            SqlConnection.ClearPool(blocker);
            await lifecycle.CleanupAsync(TestContext.Current.CancellationToken);
            Assert.True(lifecycle.DatabaseWasDropped);
            Assert.Null(store.Find(profileName));

            var persisted = File.ReadAllText(storePath);
            var persistedBootstrap = store.Find(BootstrapName)!;
            Assert.Equal(ServerVariable, persistedBootstrap.ServerValue.Reference!.Name);
            if (values.TryGetValue(PasswordVariable, out var password))
            {
                Assert.Equal(
                    PasswordVariable,
                    persistedBootstrap.PasswordValue!.Reference!.Name);
                Assert.DoesNotContain(password, persisted, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (orphanName is not null && !orphanLifecycle.DatabaseWasDropped)
                await orphanLifecycle.CleanupAsync(CancellationToken.None);
            if (databaseName is not null && !lifecycle.DatabaseWasDropped)
                await lifecycle.CleanupAsync(CancellationToken.None);
        }
    }

    private Task<TigerSqlCmdProcessResult> RunProcessAsync(
        IReadOnlyDictionary<string, string?> environment,
        params string[] arguments) =>
        TigerSqlCmdProcessRunner.RunAsync(environment, directory, arguments);

    private static void AssertProcessSuccess(
        TigerSqlCmdProcessResult result,
        IReadOnlyCollection<string> sensitiveValues)
    {
        Assert.True(
            result.ExitCode == (int)TigerSqlCmdExitCode.Ok,
            result.StdErr + Environment.NewLine + result.StdOut);

        var output = result.StdOut + result.StdErr;
        foreach (var value in sensitiveValues)
            Assert.DoesNotContain(value, output, StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<string> SensitiveValues(
        IReadOnlyDictionary<string, string> externalValues) =>
        externalValues.TryGetValue(PasswordVariable, out var password)
            ? [password]
            : [];

    private static Dictionary<string, string> BuildExternalValues(
        SqlConnectionStringBuilder builder)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ServerVariable] = builder.DataSource
        };
        if (!builder.IntegratedSecurity)
        {
            values[UsernameVariable] = builder.UserID;
            values[PasswordVariable] = builder.Password;
        }

        return values;
    }

    private static SqlServerConnectionProfile ExternalBootstrap(
        SqlConnectionStringBuilder builder)
    {
        var profile = new SqlServerConnectionProfile
        {
            Name = BootstrapName,
            ServerValue = EnvironmentReference(ServerVariable),
            Authentication = builder.IntegratedSecurity
                ? AuthenticationType.Integrated
                : AuthenticationType.SqlPassword,
            Encrypt = Enum.Parse<EncryptOption>(builder.Encrypt.ToString()),
            TrustServerCertificate = builder.TrustServerCertificate,
            ConnectTimeout = builder.ConnectTimeout,
            MultiSubnetFailover = builder.MultiSubnetFailover,
            PersistSecurityInfo = builder.PersistSecurityInfo,
            Pooling = builder.Pooling,
            MinPoolSize = builder.MinPoolSize,
            MaxPoolSize = builder.MaxPoolSize,
            ApplicationIntent = builder.ApplicationIntent == Microsoft.Data.SqlClient.ApplicationIntent.ReadOnly
                ? ApplicationIntentOption.ReadOnly
                : ApplicationIntentOption.ReadWrite
        };

        if (!builder.IntegratedSecurity)
        {
            profile.UsernameValue = EnvironmentReference(UsernameVariable);
            profile.PasswordValue = EnvironmentReference(PasswordVariable);
        }

        return profile;
    }

    private static SqlServerConnectionValue EnvironmentReference(string name) =>
        SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.EnvironmentVariable,
            Name = name
        });

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
