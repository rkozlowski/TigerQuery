using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Lists the databases available on the server described by a connection profile.
/// Used to back interactive database selection. Failures (unreachable server,
/// bad credentials, etc.) are swallowed and surface as an empty list so an
/// optional prompt is simply skipped rather than aborting the command.
/// </summary>
public static class SqlServerDatabaseLister
{
    private const int DefaultProbeTimeoutSeconds = 5;

    public static async Task<IReadOnlyList<string>> ListAsync(
        SqlServerConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            var builder = profile.BuildConnectionStringBuilder();

            // Enumerate from the server default; the target database may not exist yet.
            builder.InitialCatalog = string.Empty;
            if (profile.ConnectTimeout is null)
                builder.ConnectTimeout = DefaultProbeTimeoutSeconds;

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name;";

            var names = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                names.Add(reader.GetString(0));

            return names;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }
}
