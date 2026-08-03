using System.Diagnostics.Tracing;
using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.Tests.Live;
using Xunit.Sdk;

namespace ItTiger.TigerQuery.Tests.E2e;

public sealed class SqlServerE2eTestEnvironmentTests
{
    [Fact]
    public void AnUnconfiguredRunIsNotConfiguredWithoutStoreOrSqlActivity()
    {
        var activity = Guid.NewGuid();
        using var sqlClient = new SqlClientEventProbe(activity);
        EventSource.SetCurrentThreadActivityId(activity, out var previousActivity);
        var storeFactoryCalls = 0;
        var environmentReads = new List<string>();

        try
        {
            var result = SqlServerTestEnvironment.Resolve(
                name =>
                {
                    environmentReads.Add(name);
                    return null;
                },
                _ =>
                {
                    storeFactoryCalls++;
                    throw new InvalidOperationException("An unconfigured run must not construct a store.");
                });

            Assert.Equal(SqlServerE2eResolutionStatus.NotConfigured, result.Resolution.Status);
            Assert.Null(result.Store);
        }
        finally
        {
            EventSource.SetCurrentThreadActivityId(previousActivity);
        }

        Assert.Equal(
            [SqlServerConnectionStoreEnvironment.ConnectionStoreFile],
            environmentReads);
        Assert.Equal(0, storeFactoryCalls);
        Assert.Equal(0, sqlClient.Count);
    }

    [Fact]
    public void AReachableLegacyEndpointCannotTriggerDiscoveryOrFallback()
    {
        var requestedVariables = new List<string>();

        var result = SqlServerTestEnvironment.Resolve(
            name =>
            {
                requestedVariables.Add(name);
                return name == "TIGERQUERY_TEST_SQLSERVER"
                    ? "Server=127.0.0.1,1433;Integrated Security=true"
                    : null;
            },
            _ => throw new InvalidOperationException("No store should be constructed."));

        Assert.Equal(SqlServerE2eResolutionStatus.NotConfigured, result.Resolution.Status);
        Assert.Equal(
            [SqlServerConnectionStoreEnvironment.ConnectionStoreFile],
            requestedVariables);
    }

    [Theory]
    [InlineData(SqlServerE2eResolutionStatus.Invalid)]
    [InlineData(SqlServerE2eResolutionStatus.Ambiguous)]
    public void InvalidAndAmbiguousMappingsAreTestFailures(
        SqlServerE2eResolutionStatus status)
    {
        using var temp = new TempStore();
        var resolution = new SqlServerE2eConnectionResolution
        {
            Status = status,
            Errors = [$"Deliberate {status} configuration."]
        };

        var exception = Assert.Throws<FailException>(
            () => SqlServerTestEnvironment.RequireResolved(temp.Store, resolution));

        Assert.Contains(status.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NotConfiguredMapsToAnXunitRuntimeSkip()
    {
        using var temp = new TempStore();
        SqlServerTestEnvironment.RequireResolved(
            temp.Store,
            new SqlServerE2eConnectionResolution
            {
                Status = SqlServerE2eResolutionStatus.NotConfigured,
                Errors = ["Deliberately unconfigured for skip mapping."]
            });
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
