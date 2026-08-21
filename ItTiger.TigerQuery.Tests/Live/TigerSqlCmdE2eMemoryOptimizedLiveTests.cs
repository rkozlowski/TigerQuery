using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.Tests.Cli;
using ItTiger.TigerSqlCmd;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Proves <c>e2e create --memory-optimized</c> against a real SQL Server: what it adds to
/// the exact database it created, that the default creation path is unchanged, and that the
/// result is torn down by the ordinary owned teardown with no special caller action.
/// </summary>
/// <remarks>
/// Every assertion about provisioning reads SQL Server's own catalog rather than the
/// command's console text, and the two databases are created from one host so the enabled
/// and non-enabled paths are compared under identical configuration.
/// </remarks>
[Collection(LiveTestCollection.Name)]
public sealed partial class TigerSqlCmdE2eMemoryOptimizedLiveTests : IDisposable
{
    private readonly LiveTigerSqlCmdHost host = LiveTigerSqlCmdHost.Create();
    private readonly List<string> createdDatabases = [];

    public void Dispose()
    {
        // Never the product's teardown: a leftover database must not be hidden by the very
        // code these tests are checking.
        foreach (var databaseName in createdDatabases)
            ForceDropDirectly(databaseName);

        host.Dispose();
    }

    /// <summary>
    /// The whole contract in one pass over two databases created from the same host: only
    /// the requested one is provisioned, only it can hold a memory-optimized table, and both
    /// are removed by an ordinary <c>e2e cleanup</c>.
    /// </summary>
    [Fact]
    public async Task OnlyTheRequestedDatabaseIsProvisionedAndOrdinaryCleanupRemovesBoth()
    {
        var sessionId = Guid.NewGuid();
        var ordinary = await CreateAsync(sessionId, "mo-off", memoryOptimized: false);
        var enabled = await CreateAsync(sessionId, "mo-on", memoryOptimized: true);

        // The default remains the ordinary disposable database: no memory-optimized
        // filegroup, and no container file of any kind.
        Assert.Empty(await MemoryOptimizedFilegroupsAsync(ordinary.DatabaseName));
        Assert.Empty(await MemoryOptimizedContainersAsync(ordinary.DatabaseName));

        // The enabled one has exactly one MEMORY_OPTIMIZED_DATA filegroup ('FX') carrying
        // exactly one container, both named from the exact database they belong to.
        var filegroup = Assert.Single(await MemoryOptimizedFilegroupsAsync(enabled.DatabaseName));
        Assert.Equal(
            SqlServerE2eNames.MemoryOptimizedFilegroup(enabled.DatabaseName),
            filegroup);

        var container = Assert.Single(await MemoryOptimizedContainersAsync(enabled.DatabaseName));
        Assert.Equal(
            SqlServerE2eNames.MemoryOptimizedContainer(enabled.DatabaseName),
            container.LogicalName);

        // The container lives where SQL Server puts data files, not anywhere this test
        // process chose, and its directory is derived from the exact owned database.
        Assert.EndsWith(
            SqlServerE2eNames.MemoryOptimizedContainerDirectory(enabled.DatabaseName),
            container.PhysicalName,
            StringComparison.Ordinal);
        Assert.StartsWith(
            await DefaultDataDirectoryAsync(),
            container.PhysicalName,
            StringComparison.OrdinalIgnoreCase);

        // The capability is the point: a real memory-optimized table is created and used.
        var used = await host.RunAsync(
            "run", "--connection", enabled.ConnectionName,
            "--query",
            """
            CREATE TABLE dbo.MemoryOptimizedProbe
            (
                Id int NOT NULL PRIMARY KEY NONCLUSTERED,
                Marker nvarchar(50) NOT NULL
            )
            WITH (MEMORY_OPTIMIZED = ON, DURABILITY = SCHEMA_ONLY);
            GO
            INSERT INTO dbo.MemoryOptimizedProbe (Id, Marker) VALUES (1, N'in-memory');
            GO
            SELECT Marker FROM dbo.MemoryOptimizedProbe WHERE Id = 1;
            GO
            """);
        Assert.True(
            used.ExitCode == (int)TigerSqlCmdExitCode.Ok,
            used.StdErr + Environment.NewLine + used.StdOut);
        Assert.Contains("in-memory", used.StdOut, StringComparison.Ordinal);
        Assert.Equal([1], await MemoryOptimizedTableCountAsync(enabled.DatabaseName));

        // The same table definition is rejected by the database that was not provisioned,
        // which is what makes the difference a capability rather than cosmetic metadata.
        var rejected = await host.RunAsync(
            "run", "--connection", ordinary.ConnectionName,
            "--query",
            "CREATE TABLE dbo.MemoryOptimizedProbe (Id int NOT NULL PRIMARY KEY NONCLUSTERED) "
                + "WITH (MEMORY_OPTIMIZED = ON, DURABILITY = SCHEMA_ONLY);");
        Assert.Equal((int)TigerSqlCmdExitCode.BatchFailed, rejected.ExitCode);

        // Nothing special is asked of the caller: the ordinary session cleanup removes the
        // provisioned database, its container, and both connection records.
        var cleanup = await host.RunAsync("e2e", "cleanup", "--session-id", sessionId.ToString("D"));
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, cleanup.ExitCode);

        foreach (var created in new[] { ordinary, enabled })
        {
            Assert.Empty(await ExistingNamesAsync(created.DatabaseName));
            Assert.Null(host.Store.Find(created.ConnectionName));
            createdDatabases.Remove(created.DatabaseName);
        }
    }

    /// <summary>
    /// The provisioned database is owned exactly like any other: the paired connection still
    /// records the drop authorization, and the forced teardown removes it even while it is
    /// in use.
    /// </summary>
    [Fact]
    public async Task AProvisionedDatabaseKeepsOrdinaryOwnershipAndForcedTeardown()
    {
        var sessionId = Guid.NewGuid();
        var created = await CreateAsync(sessionId, "mo-owned", memoryOptimized: true);

        var profile = host.Store.Find(created.ConnectionName)!;
        Assert.Equal(
            SqlServerE2eFlagState.True,
            SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.AllowDatabaseDrop));
        Assert.Equal(created.DatabaseName, profile.Metadata[SqlServerE2eMetadata.DatabaseName]);
        // The capability is a property of the database, so it leaves no trace in the
        // bootstrap profile's protected metadata.
        var bootstrap = host.Store.Find(LiveTigerSqlCmdHost.BootstrapName)!;
        Assert.DoesNotContain(
            bootstrap.Metadata,
            entry => entry.Key.Contains("memory", StringComparison.OrdinalIgnoreCase));

        await using (var blocker = new SqlConnection(host.ConnectionStringFor(created.DatabaseName)))
        {
            await blocker.OpenAsync(TestContext.Current.CancellationToken);

            var drop = await host.RunAsync(
                "e2e", "drop",
                "--connection", created.ConnectionName,
                "--session-id", sessionId.ToString("D"));

            Assert.Equal((int)TigerSqlCmdExitCode.Ok, drop.ExitCode);
        }

        Assert.Empty(await ExistingNamesAsync(created.DatabaseName));
        Assert.Null(host.Store.Find(created.ConnectionName));
        createdDatabases.Remove(created.DatabaseName);
    }

    private async Task<CreatedResources> CreateAsync(
        Guid sessionId,
        string namePart,
        bool memoryOptimized)
    {
        string[] arguments =
        [
            "e2e", "create",
            "--session-id", sessionId.ToString("D"),
            "--name-part", namePart,
            .. memoryOptimized ? (string[])["--memory-optimized"] : []
        ];

        var result = await host.RunAsync(arguments);
        Assert.True(
            result.ExitCode == (int)TigerSqlCmdExitCode.Ok,
            result.StdErr + Environment.NewLine + result.StdOut);

        var databaseName = Match(DatabasePattern(), result.StdOut, "database");
        var connectionName = Match(ConnectionPattern(), result.StdOut, "connection");
        createdDatabases.Add(databaseName);
        return new CreatedResources(databaseName, connectionName);
    }

    /// <summary>Reads the memory-optimized ('FX') filegroups of one exact database.</summary>
    private Task<IReadOnlyList<string>> MemoryOptimizedFilegroupsAsync(string databaseName) =>
        QueryAsync(
            databaseName,
            "SELECT name FROM sys.filegroups WHERE [type] = 'FX' ORDER BY name;",
            reader => reader.GetString(0));

    /// <summary>Reads the container files sitting in a memory-optimized filegroup.</summary>
    private Task<IReadOnlyList<ContainerFile>> MemoryOptimizedContainersAsync(string databaseName) =>
        QueryAsync(
            databaseName,
            """
            SELECT f.name, f.physical_name
            FROM sys.database_files AS f
            INNER JOIN sys.filegroups AS g ON g.data_space_id = f.data_space_id
            WHERE g.[type] = 'FX'
            ORDER BY f.name;
            """,
            reader => new ContainerFile(reader.GetString(0), reader.GetString(1)));

    private Task<IReadOnlyList<int>> MemoryOptimizedTableCountAsync(string databaseName) =>
        QueryAsync(
            databaseName,
            "SELECT COUNT(*) FROM sys.tables WHERE is_memory_optimized = 1;",
            reader => reader.GetInt32(0));

    /// <summary>Asks the server where it puts data files, the way provisioning does.</summary>
    private async Task<string> DefaultDataDirectoryAsync()
    {
        var reported = await QueryAsync(
            "master",
            """
            SELECT COALESCE(
                CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultDataPath')),
                (
                    SELECT TOP (1) LEFT(
                        physical_name,
                        LEN(physical_name) - CHARINDEX(N'\', REVERSE(physical_name)) + 1)
                    FROM sys.master_files
                    WHERE database_id = 1 AND [type] = 0
                    ORDER BY file_id
                ));
            """,
            reader => reader.GetString(0));

        return Assert.Single(reported);
    }

    private Task<IReadOnlyList<string>> ExistingNamesAsync(string exactName) =>
        LiveTigerSqlCmdHost.DatabaseNamesAsync(host.ConnectionStringFor("master"), exactName);

    private async Task<IReadOnlyList<T>> QueryAsync<T>(
        string databaseName,
        string sql,
        Func<SqlDataReader, T> read)
    {
        await using var connection = new SqlConnection(host.ConnectionStringFor(databaseName));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            values.Add(read(reader));

        return values;
    }

    /// <summary>
    /// Removes a database without using the product's teardown, so test cleanup can never
    /// make a real teardown defect look like a pass.
    /// </summary>
    private void ForceDropDirectly(string databaseName)
    {
        try
        {
            using var connection = new SqlConnection(host.ConnectionStringFor("master"));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(@name) IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END
                """;
            command.Parameters.AddWithValue("@name", databaseName);
            command.ExecuteNonQuery();
        }
        catch (SqlException)
        {
            // Best effort: an unremovable leftover is reported by the assertions above.
        }
    }

    private static string Match(Regex pattern, string output, string what)
    {
        var match = pattern.Match(output);
        Assert.True(match.Success, $"tiger-sqlcmd did not report the created E2E {what}: {output}");
        return match.Groups["name"].Value;
    }

    [GeneratedRegex(@"Created E2E database (?<name>_TQ_E2E_[A-Za-z0-9_-]+)\.")]
    private static partial Regex DatabasePattern();

    [GeneratedRegex(@"Created E2E connection (?<name>E2E-[A-Za-z0-9_-]+)\.")]
    private static partial Regex ConnectionPattern();

    private sealed record CreatedResources(string DatabaseName, string ConnectionName);

    private sealed record ContainerFile(string LogicalName, string PhysicalName);
}
