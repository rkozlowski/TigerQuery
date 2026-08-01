using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Resolves the SQL Server instance used by the execution tests that cannot be proven
/// with a mocked provider, and lets those tests skip cleanly when no instance is
/// reachable.
/// </summary>
/// <remarks>
/// The instance is taken from the <c>TIGERQUERY_TEST_SQLSERVER</c> environment
/// variable when it is set; otherwise the local default instance is probed with
/// integrated authentication. Detection runs once per test process.
/// </remarks>
internal static class SqlServerTestEnvironment
{
    private const string ConnectionStringVariable = "TIGERQUERY_TEST_SQLSERVER";

    private static readonly Lazy<Detection> Probe = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the connection string of a reachable instance, or null.</summary>
    public static string? ConnectionString => Probe.Value.ConnectionString;

    /// <summary>Skips the calling test when no SQL Server instance is reachable.</summary>
    public static string RequireConnectionString()
    {
        var detection = Probe.Value;
        if (detection.ConnectionString is null)
            Assert.Skip(detection.SkipReason!);

        return detection.ConnectionString;
    }

    private static Detection Detect()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        var candidate = string.IsNullOrWhiteSpace(configured)
            ? "Server=(local);Integrated Security=True;TrustServerCertificate=True;Database=tempdb;Connect Timeout=5"
            : configured;

        try
        {
            using var connection = new SqlConnection(candidate);
            connection.Open();
            return new Detection(candidate, null);
        }
        catch (Exception ex)
        {
            var source = string.IsNullOrWhiteSpace(configured)
                ? "the local default instance"
                : $"the instance configured through {ConnectionStringVariable}";
            return new Detection(
                null,
                $"No SQL Server instance is reachable: opening {source} failed with '{ex.Message}'. " +
                $"Set {ConnectionStringVariable} to run the SQL Server-backed tests.");
        }
    }

    private sealed record Detection(string? ConnectionString, string? SkipReason);
}
