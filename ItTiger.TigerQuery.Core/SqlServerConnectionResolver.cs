namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Resolves a saved SQL Server connection profile by name to a usable connection string.
/// This keeps profile lookup and profile-to-connection-string behavior with the shared
/// connection model rather than with CLI command handling.
/// </summary>
public static class SqlServerConnectionResolver
{
    public static SqlServerConnectionResolution Resolve(SqlServerConnectionStore store, string? name)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (string.IsNullOrWhiteSpace(name))
            return SqlServerConnectionResolution.Failure("No saved connection was specified.");

        var profile = store.Find(name);
        if (profile is null)
            return SqlServerConnectionResolution.Failure($"Saved connection '{name}' was not found.");

        string connectionString;
        try
        {
            connectionString = profile.BuildConnectionString();
        }
        catch (Exception ex)
        {
            return SqlServerConnectionResolution.Failure(
                $"Saved connection '{name}' could not be turned into a usable connection string: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            return SqlServerConnectionResolution.Failure(
                $"Saved connection '{name}' produced an empty connection string.");

        return SqlServerConnectionResolution.Success(connectionString);
    }
}
