namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Resolves a saved SQL Server connection profile by name to a usable connection string.
/// This keeps profile lookup and profile-to-connection-string behavior with the shared
/// connection model rather than with CLI command handling.
/// </summary>
public static class SqlServerConnectionResolver
{
    /// <summary>Resolves a saved profile name to a normalized SqlClient connection string.</summary>
    /// <param name="store">The profile store to search.</param>
    /// <param name="name">The case-sensitive saved name, or null when unspecified.</param>
    /// <param name="externalValues">
    /// Optional injected readers used only when the selected profile contains external
    /// references.
    /// </param>
    /// <returns>
    /// A success containing the connection string, or a failure containing a clean
    /// reason for missing input, an absent profile, or connection-string conversion.
    /// </returns>
    /// <remarks>
    /// Profile loading, file I/O, and JSON errors are not converted to failure
    /// results. Errors thrown while building a found profile's connection string are.
    /// A profile with no database produces a valid server-level connection string.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/> is <see langword="null"/>.
    /// </exception>
    public static SqlServerConnectionResolution Resolve(
        SqlServerConnectionStore store,
        string? name,
        SqlServerExternalValueResolutionOptions? externalValues = null)
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
            connectionString = profile.BuildConnectionString(externalValues);
        }
        catch (SqlServerExternalValueException ex)
        {
            return SqlServerConnectionResolution.Failure(
                $"Saved connection '{name}' could not resolve its external values: {ex.Message}");
        }
        catch (Exception ex)
        {
            return SqlServerConnectionResolution.Failure(
                $"Saved connection '{name}' could not be turned into a usable connection string: "
                + profile.RedactSensitiveValues(ex.Message));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            return SqlServerConnectionResolution.Failure(
                $"Saved connection '{name}' produced an empty connection string.");

        return SqlServerConnectionResolution.Success(connectionString);
    }
}
