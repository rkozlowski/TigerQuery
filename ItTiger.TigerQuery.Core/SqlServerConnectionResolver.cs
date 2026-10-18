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
    /// reason for missing input, an absent profile, a stored password that is unavailable
    /// after loading, or connection-string conversion.
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

        // A login attempted without the password would fail as an authentication rejection,
        // which sends the user to check the server or the login instead of the local store.
        var unavailablePassword = DescribeUnavailableStoredPassword(name, profile);
        if (unavailablePassword is not null)
            return SqlServerConnectionResolution.Failure(unavailablePassword);

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

    /// <summary>
    /// Explains why a SQL-password profile that relies on its stored password has none in
    /// memory after loading, or returns null when the profile does not depend on one.
    /// </summary>
    /// <remarks>
    /// Only the at-rest facts are reported: whether a protected value exists and how it
    /// was protected. Which of the possible reasons applies — another Windows account,
    /// another computer, another platform, or a damaged value — cannot be told apart from
    /// here, so the message states the rule rather than guessing the cause.
    /// </remarks>
    private static string? DescribeUnavailableStoredPassword(
        string name,
        SqlServerConnectionProfile profile)
    {
        if (profile.UsesFullConnectionString
            || profile.Authentication != AuthenticationType.SqlPassword
            || profile.PasswordValue is not null
            || !string.IsNullOrEmpty(profile.PlainPassword))
        {
            return null;
        }

        if (string.IsNullOrEmpty(profile.EncryptedPassword))
        {
            return $"Saved connection '{name}' uses SQL password authentication but has no saved "
                + "password. Save the password again, or configure an external password reference.";
        }

        if (profile.PasswordEncryption == PasswordEncryptionType.DPAPI)
        {
            return $"Saved connection '{name}' has a DPAPI-protected password that could not be "
                + "decrypted here. A DPAPI-protected password can be decrypted only on Windows, by "
                + "the Windows account that saved it. Save the password again as the current user, "
                + "or configure an external password reference.";
        }

        return $"Saved connection '{name}' has a stored password protected as "
            + $"'{profile.PasswordEncryption}', which could not be decrypted here. Save the password "
            + "again, or configure an external password reference.";
    }
}
