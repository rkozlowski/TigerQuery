using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.Tests.Cli;
using ItTiger.TigerSqlCmd;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Proves that owned E2E teardown is deterministic against a real SQL Server even when
/// the owned database is still in use, and that the stronger path stays confined to
/// session-owned databases.
/// </summary>
/// <remarks>
/// The reported failure was a session-owned database that could not be dropped because an
/// interrupted script had left work open inside it. These tests recreate that state with a
/// real open transaction, confirm an ordinary drop still fails against it, and then require
/// <c>e2e drop</c> and <c>e2e cleanup</c> to succeed anyway.
/// </remarks>
[Collection(LiveTestCollection.Name)]
public sealed partial class TigerSqlCmdE2eForcedCleanupLiveTests : IDisposable
{
    private readonly LiveTigerSqlCmdHost host = LiveTigerSqlCmdHost.Create();
    private readonly List<string> createdDatabases = [];

    public void Dispose()
    {
        // Nothing here may run the product's teardown: a leftover database would otherwise
        // be hidden by the very code under test.
        foreach (var databaseName in createdDatabases)
            ForceDropDirectly(databaseName);

        host.Dispose();
    }

    [Fact]
    public async Task ForcedDropRemovesAnOwnedDatabaseThatAnOpenTransactionIsBlocking()
    {
        var sessionId = Guid.NewGuid();
        var created = await CreateAsync(sessionId, "forced-drop");

        await using var blocker = await BlockAsync(created.DatabaseName);

        // The plain drop the lifecycle used to issue still fails against this state, so
        // the forced path below is what makes the difference.
        await AssertOrdinaryDropFailsAsync(created.DatabaseName);
        Assert.NotEmpty(await ExistingNamesAsync(created.DatabaseName));

        var drop = await host.RunAsync(
            "e2e", "drop",
            "--connection", created.ConnectionName,
            "--session-id", sessionId.ToString("D"));

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, drop.ExitCode);
        Assert.Empty(await ExistingNamesAsync(created.DatabaseName));
        Assert.Null(host.Store.Find(created.ConnectionName));
        createdDatabases.Remove(created.DatabaseName);
    }

    [Fact]
    public async Task ForcedCleanupRemovesABlockedOwnedDatabaseAndItsOwningConnection()
    {
        var sessionId = Guid.NewGuid();
        var created = await CreateAsync(sessionId, "forced-cleanup");

        await using var blocker = await BlockAsync(created.DatabaseName);

        var cleanup = await host.RunAsync(
            "e2e", "cleanup", "--session-id", sessionId.ToString("D"));

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, cleanup.ExitCode);
        Assert.Empty(await ExistingNamesAsync(created.DatabaseName));
        Assert.Null(host.Store.Find(created.ConnectionName));
        createdDatabases.Remove(created.DatabaseName);
    }

    /// <summary>
    /// The forced teardown names one database. Another session's owned database — even one
    /// a non-owning clone in the cleaned session points at — is left completely alone.
    /// </summary>
    [Fact]
    public async Task CleanupTouchesNeitherAnotherSessionsDatabaseNorANonOwningTarget()
    {
        var keeperSession = Guid.NewGuid();
        var keeper = await CreateAsync(keeperSession, "keeper");

        var cleanedSession = Guid.NewGuid();
        var owned = await CreateAsync(cleanedSession, "owned");

        var clone = await host.RunAsync(
            "connection", "clone-e2e", LiveTigerSqlCmdHost.BootstrapName,
            "--database", keeper.DatabaseName,
            "--session-id", cleanedSession.ToString("D"),
            "--connection-name-part", "nonowning");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, clone.ExitCode);
        var cloneName = Assert.Single(
            host.Store.Load(),
            profile => profile.Name.StartsWith("E2E-nonowning-", StringComparison.Ordinal)).Name;

        await using (var blocker = await BlockAsync(owned.DatabaseName))
        {
            var cleanup = await host.RunAsync(
                "e2e", "cleanup", "--session-id", cleanedSession.ToString("D"));
            Assert.Equal((int)TigerSqlCmdExitCode.Ok, cleanup.ExitCode);
        }

        // The owned database of the cleaned session is gone, with its connection.
        Assert.Empty(await ExistingNamesAsync(owned.DatabaseName));
        Assert.Null(host.Store.Find(owned.ConnectionName));
        createdDatabases.Remove(owned.DatabaseName);

        // The non-owning clone was detached and its target survives untouched, as does
        // the other session's own connection record.
        Assert.Null(host.Store.Find(cloneName));
        Assert.Equal([keeper.DatabaseName], await ExistingNamesAsync(keeper.DatabaseName));
        Assert.NotNull(host.Store.Find(keeper.ConnectionName));
    }

    /// <summary>
    /// A non-owning clone never authorizes teardown, so cleaning it up must not disturb a
    /// database that is in use — no drop, and no forced single-user mode either.
    /// </summary>
    [Fact]
    public async Task NonOwningCleanupLeavesABusyTargetDatabaseAndItsSessionsAlone()
    {
        var ownerSession = Guid.NewGuid();
        var owned = await CreateAsync(ownerSession, "busy-target");

        var cloneSession = Guid.NewGuid();
        var clone = await host.RunAsync(
            "connection", "clone-e2e", LiveTigerSqlCmdHost.BootstrapName,
            "--database", owned.DatabaseName,
            "--session-id", cloneSession.ToString("D"),
            "--connection-name-part", "readonly");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, clone.ExitCode);

        await using var blocker = await BlockAsync(owned.DatabaseName);

        var cleanup = await host.RunAsync(
            "e2e", "cleanup", "--session-id", cloneSession.ToString("D"));

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, cleanup.ExitCode);
        Assert.Equal([owned.DatabaseName], await ExistingNamesAsync(owned.DatabaseName));

        // The blocking session was never rolled back, so its uncommitted work is intact.
        Assert.Equal(1, await blocker.CountMarkersAsync());
    }

    /// <summary>
    /// Dropping the same owned resource twice is safe: the second call finds the database
    /// absent, reports that, and completes.
    /// </summary>
    [Fact]
    public async Task DroppingAnAlreadyAbsentOwnedDatabaseStillCompletes()
    {
        var sessionId = Guid.NewGuid();
        var created = await CreateAsync(sessionId, "absent");

        ForceDropDirectly(created.DatabaseName);
        createdDatabases.Remove(created.DatabaseName);

        var drop = await host.RunAsync(
            "e2e", "drop",
            "--connection", created.ConnectionName,
            "--session-id", sessionId.ToString("D"));

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, drop.ExitCode);
        Assert.Contains("already absent", drop.StdOut, StringComparison.Ordinal);
        Assert.Null(host.Store.Find(created.ConnectionName));
    }

    /// <summary>
    /// Cleanup keeps going after one record fails and reports the run as incomplete. The
    /// failing record is a malformed owner, which no forced drop can rescue.
    /// </summary>
    [Fact]
    public async Task CleanupContinuesPastAFailingRecordAndReturnsNonZero()
    {
        var sessionId = Guid.NewGuid();
        var healthy = await CreateAsync(sessionId, "healthy");

        var broken = new SqlServerConnectionProfile
        {
            Name = "E2E-broken-" + Guid.NewGuid().ToString("N"),
            Server = host.BootstrapBuilder.DataSource,
            Database = "_TQ_E2E_broken_owner"
        };
        SqlServerE2eMetadata.ConfigureSessionProfile(
            broken, sessionId, broken.Database!, allowDatabaseDrop: true);
        broken.SetReservedMetadata(SqlServerE2eMetadata.AllowDatabaseDrop, "True");
        host.Store.Add(broken);

        var cleanup = await host.RunAsync(
            "e2e", "cleanup", "--session-id", sessionId.ToString("D"));

        Assert.NotEqual((int)TigerSqlCmdExitCode.Ok, cleanup.ExitCode);
        Assert.Contains("Cleanup failed", cleanup.StdErr, StringComparison.Ordinal);

        // The healthy record was still cleaned, and the failing one was preserved for a
        // retry rather than discarded.
        Assert.Empty(await ExistingNamesAsync(healthy.DatabaseName));
        Assert.Null(host.Store.Find(healthy.ConnectionName));
        Assert.NotNull(host.Store.Find(broken.Name));
        createdDatabases.Remove(healthy.DatabaseName);
    }

    private async Task<CreatedResources> CreateAsync(Guid sessionId, string namePart)
    {
        var result = await host.RunAsync(
            "e2e", "create",
            "--session-id", sessionId.ToString("D"),
            "--name-part", namePart);
        Assert.True(
            result.ExitCode == (int)TigerSqlCmdExitCode.Ok,
            result.StdErr + Environment.NewLine + result.StdOut);

        var databaseName = Match(DatabasePattern(), result.StdOut, "database");
        var connectionName = Match(ConnectionPattern(), result.StdOut, "connection");
        createdDatabases.Add(databaseName);
        return new CreatedResources(databaseName, connectionName);
    }

    /// <summary>
    /// Confirms that the unforced statement really is defeated by the blocking session,
    /// so a passing forced teardown cannot be explained by the state having cleared.
    /// </summary>
    /// <remarks>
    /// An unforced <c>DROP DATABASE</c> against a database with an open session does not
    /// simply return 3702 here: it waits for the database to become free, so the probe is
    /// given a short command timeout and accepts either answer. The waiting form is the
    /// stronger evidence — without the forced rollback, the statement never completes.
    /// </remarks>
    private async Task AssertOrdinaryDropFailsAsync(string databaseName)
    {
        await using var connection = new SqlConnection(host.ConnectionStringFor("master"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = $"DROP DATABASE [{databaseName}];";

        var failure = await Assert.ThrowsAsync<SqlException>(
            () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        // 3702: the database is in use. 1222/-2: the statement was still waiting for it.
        Assert.Contains(failure.Number, (int[])[3702, 1222, -2]);
    }

    private Task<IReadOnlyList<string>> ExistingNamesAsync(string exactName) =>
        LiveTigerSqlCmdHost.DatabaseNamesAsync(host.ConnectionStringFor("master"), exactName);

    private async Task<BlockingSession> BlockAsync(string databaseName) =>
        await BlockingSession.OpenAsync(host.ConnectionStringFor(databaseName));

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

    /// <summary>
    /// One session holding an uncommitted transaction inside a database, which is what
    /// makes an ordinary <c>DROP DATABASE</c> fail with "currently in use".
    /// </summary>
    private sealed class BlockingSession : IAsyncDisposable
    {
        private readonly SqlConnection connection;
        private readonly SqlTransaction transaction;

        private BlockingSession(SqlConnection connection, SqlTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
        }

        public static async Task<BlockingSession> OpenAsync(string connectionString)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "CREATE TABLE dbo.BlockingWork (Id int NOT NULL); "
                + "INSERT INTO dbo.BlockingWork (Id) VALUES (1);";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            return new BlockingSession(connection, transaction);
        }

        /// <summary>Reads the uncommitted row count through the same open transaction.</summary>
        public async Task<int> CountMarkersAsync()
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM dbo.BlockingWork;";
            return (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }

        public async ValueTask DisposeAsync()
        {
            // A forced teardown has already rolled this session back and disconnected it,
            // so every step here is expected to be able to fail.
            try { await transaction.DisposeAsync(); } catch (SqlException) { }
            catch (InvalidOperationException) { }
            try { await connection.DisposeAsync(); } catch (SqlException) { }
            SqlConnection.ClearPool(new SqlConnection(connection.ConnectionString));
        }
    }
}
