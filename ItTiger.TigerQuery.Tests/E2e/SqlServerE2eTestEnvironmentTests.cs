using System.Diagnostics.Tracing;
using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.Tests.Live;
using ItTiger.TigerSqlCmd;
using Xunit.Sdk;

namespace ItTiger.TigerQuery.Tests.E2e;

public sealed class SqlServerE2eTestEnvironmentTests
{
    [Fact]
    public void RegularApplicationDefaultStoreIsUsedWithoutAnEnvironmentOverride()
    {
        using var applicationDefault = new TempStore();
        applicationDefault.Store.Add(Bootstrap("default-server"));

        var result = SqlServerTestEnvironment.Resolve(
            applicationDefault.Store.FilePath,
            _ => null,
            OpenStore,
            requireDatabaseCreation: true);

        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, result.Resolution.Status);
        Assert.Equal("default-server", result.Resolution.Profile!.Server);
        Assert.Equal(applicationDefault.Store.FilePath, result.Store!.FilePath);
        Assert.Equal(
            TigerSqlCmdApp.DefaultE2eBootstrapConnectionName,
            result.Resolution.RequestedName);
    }

    [Fact]
    public void EnvironmentSelectedAlternateStoreIsUsed()
    {
        using var applicationDefault = new TempStore();
        using var alternate = new TempStore();
        alternate.Store.Add(Bootstrap("alternate-server"));

        var result = SqlServerTestEnvironment.Resolve(
            applicationDefault.Store.FilePath,
            name => name == SqlServerConnectionStoreEnvironment.ConnectionStoreFile
                ? alternate.Store.FilePath
                : null,
            OpenStore,
            requireDatabaseCreation: true);

        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, result.Resolution.Status);
        Assert.Equal("alternate-server", result.Resolution.Profile!.Server);
        Assert.Equal(alternate.Store.FilePath, result.Store!.FilePath);
    }

    [Fact]
    public void EnvironmentOverrideWinsOverTheConfiguredApplicationDefault()
    {
        using var applicationDefault = new TempStore();
        using var alternate = new TempStore();
        applicationDefault.Store.Add(Bootstrap("default-server"));
        alternate.Store.Add(Bootstrap("alternate-server"));

        var result = SqlServerTestEnvironment.Resolve(
            applicationDefault.Store.FilePath,
            name => name == SqlServerConnectionStoreEnvironment.ConnectionStoreFile
                ? alternate.Store.FilePath
                : null,
            OpenStore,
            requireDatabaseCreation: true);

        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, result.Resolution.Status);
        Assert.Equal("alternate-server", result.Resolution.Profile!.Server);
        Assert.Equal(alternate.Store.FilePath, result.Store!.FilePath);
    }

    [Fact]
    public void MissingBootstrapIsNotConfiguredWithoutSqlActivity()
    {
        using var applicationDefault = new TempStore();
        var activity = Guid.NewGuid();
        using var sqlClient = new SqlClientEventProbe(activity);
        EventSource.SetCurrentThreadActivityId(activity, out var previousActivity);
        var storeFactoryCalls = 0;
        var environmentReads = new List<string>();

        try
        {
            var result = SqlServerTestEnvironment.Resolve(
                applicationDefault.Store.FilePath,
                name =>
                {
                    environmentReads.Add(name);
                    return null;
                },
                _ =>
                {
                    storeFactoryCalls++;
                    return OpenStore(applicationDefault.Store.FilePath);
                });

            Assert.Equal(SqlServerE2eResolutionStatus.NotConfigured, result.Resolution.Status);
            Assert.NotNull(result.Store);
        }
        finally
        {
            EventSource.SetCurrentThreadActivityId(previousActivity);
        }

        Assert.Equal(
            [SqlServerConnectionStoreEnvironment.ConnectionStoreFile],
            environmentReads);
        Assert.Equal(1, storeFactoryCalls);
        Assert.Equal(0, sqlClient.Count);
        Assert.False(File.Exists(applicationDefault.Store.FilePath));
    }

    [Fact]
    public void AReachableLegacyEndpointCannotTriggerDiscoveryOrFallback()
    {
        using var applicationDefault = new TempStore();
        var requestedVariables = new List<string>();

        var result = SqlServerTestEnvironment.Resolve(
            applicationDefault.Store.FilePath,
            name =>
            {
                requestedVariables.Add(name);
                return name == "TIGERQUERY_TEST_SQLSERVER"
                    ? "Server=127.0.0.1,1433;Integrated Security=true"
                    : null;
            },
            OpenStore);

        Assert.Equal(SqlServerE2eResolutionStatus.NotConfigured, result.Resolution.Status);
        Assert.Equal(
            [SqlServerConnectionStoreEnvironment.ConnectionStoreFile],
            requestedVariables);
    }

    [Fact]
    public void BootstrapWithoutDatabaseCreationPermissionIsAnInvalidFailureRatherThanASkip()
    {
        using var applicationDefault = new TempStore();
        var bootstrap = new SqlServerConnectionProfile
        {
            Name = TigerSqlCmdApp.DefaultE2eBootstrapConnectionName,
            Server = "ordinary-server",
            Authentication = AuthenticationType.Integrated
        };
        SqlServerE2eMetadata.AuthorizeNewBootstrapProfile(
            bootstrap,
            allowDatabaseCreation: false);
        applicationDefault.Store.Add(bootstrap);
        var result = SqlServerTestEnvironment.Resolve(
            applicationDefault.Store.FilePath,
            _ => null,
            OpenStore,
            requireDatabaseCreation: true);

        var exception = Assert.Throws<FailException>(
            () => SqlServerTestEnvironment.RequireResolved(result.Store!, result.Resolution));

        Assert.Contains("Invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedNameWithoutBootstrapFlagIsAnInvalidFailureRatherThanASkip()
    {
        using var applicationDefault = new TempStore();
        var profile = new SqlServerConnectionProfile
        {
            Name = TigerSqlCmdApp.DefaultE2eBootstrapConnectionName,
            Server = "ordinary-server",
            Authentication = AuthenticationType.Integrated
        };
        SqlServerE2eMetadata.AuthorizeNewProfile(profile, allowDatabaseCreation: true);
        applicationDefault.Store.Add(profile);

        var result = SqlServerTestEnvironment.Resolve(
            applicationDefault.Store.FilePath,
            _ => null,
            OpenStore,
            requireDatabaseCreation: true);

        Assert.Equal(SqlServerE2eResolutionStatus.Invalid, result.Resolution.Status);
        Assert.Contains(
            result.Resolution.Errors,
            error => error.Contains(SqlServerE2eMetadata.Bootstrap, StringComparison.Ordinal));
        var exception = Assert.Throws<FailException>(
            () => SqlServerTestEnvironment.RequireResolved(result.Store!, result.Resolution));
        Assert.Contains("Invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateDefaultBootstrapIsAnAmbiguousFailureRatherThanASkip()
    {
        using var applicationDefault = new TempStore();
        applicationDefault.Store.Save([Bootstrap("first-server"), Bootstrap("second-server")]);
        var result = SqlServerTestEnvironment.Resolve(
            applicationDefault.Store.FilePath,
            _ => null,
            OpenStore,
            requireDatabaseCreation: true);

        var exception = Assert.Throws<FailException>(
            () => SqlServerTestEnvironment.RequireResolved(result.Store!, result.Resolution));

        Assert.Contains("Ambiguous", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDefaultBootstrapMapsToRuntimeSkipBeforeSqlActivity()
    {
        using var applicationDefault = new TempStore();
        var activity = Guid.NewGuid();
        using var sqlClient = new SqlClientEventProbe(activity);
        EventSource.SetCurrentThreadActivityId(activity, out var previousActivity);
        SqlServerE2eTestResolution result;

        try
        {
            result = SqlServerTestEnvironment.Resolve(
                applicationDefault.Store.FilePath,
                _ => null,
                OpenStore,
                requireDatabaseCreation: true);
        }
        finally
        {
            EventSource.SetCurrentThreadActivityId(previousActivity);
        }

        Assert.Equal(0, sqlClient.Count);
        SqlServerTestEnvironment.RequireResolved(
            result.Store!,
            result.Resolution);
    }

    [Fact]
    public void LiveTestSourceContainsNoSqlServerDiscoveryFallbacks()
    {
        var source = File.ReadAllText(FindSourceFile("SqlServerTestEnvironment.cs"));

        Assert.DoesNotContain("Server=(local)", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalDB", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TIGERQUERY_TEST_SQLSERVER", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TcpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlConnection", source, StringComparison.Ordinal);
    }

    private static string FindSourceFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "ItTiger.TigerQuery.Tests",
                "Live",
                fileName);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName}.");
    }

    private static SqlServerConnectionStore OpenStore(string path) =>
        new(
            new SqlServerConnectionStoreOptions { FilePath = path },
            new NoOpConnectionPasswordProtector());

    private static SqlServerConnectionProfile Bootstrap(string server)
    {
        var profile = new SqlServerConnectionProfile
        {
            Name = TigerSqlCmdApp.DefaultE2eBootstrapConnectionName,
            Server = server,
            Authentication = AuthenticationType.Integrated
        };
        SqlServerE2eMetadata.AuthorizeNewBootstrapProfile(profile, allowDatabaseCreation: true);
        return profile;
    }

    private sealed class SqlClientEventProbe(Guid activity) : EventListener
    {
        private const string SourceName = "Microsoft.Data.SqlClient.EventSource";
        private int count;

        public int Count => Volatile.Read(ref count);

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == SourceName)
                EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name == SourceName && eventData.ActivityId == activity)
                Interlocked.Increment(ref count);
        }
    }

    private sealed class TempStore : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "TigerQueryE2eTestMapping",
            Guid.NewGuid().ToString("N"));

        public TempStore()
        {
            Store = new SqlServerConnectionStore(
                new SqlServerConnectionStoreOptions
                {
                    FilePath = Path.Combine(directory, "connections.json")
                },
                new NoOpConnectionPasswordProtector());
        }

        public SqlServerConnectionStore Store { get; }

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
}
