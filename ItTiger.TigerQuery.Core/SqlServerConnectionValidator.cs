namespace ItTiger.TigerQuery.Core;

/// <summary>Validates connection profiles against an explicit policy.</summary>
public static class SqlServerConnectionValidator
{
    /// <summary>Collects validation errors for a profile.</summary>
    /// <param name="profile">The profile to validate.</param>
    /// <param name="policy">The required-field policy.</param>
    /// <returns>
    /// User-facing validation messages in name, server, then database order;
    /// an empty list indicates success.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> or <paramref name="policy"/> is
    /// <see langword="null"/>. The parameter name identifies the null argument.
    /// </exception>
    public static IReadOnlyList<string> Validate(
        SqlServerConnectionProfile profile,
        SqlServerConnectionValidationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(policy);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Name))
            errors.Add("Name is required.");

        if (string.IsNullOrWhiteSpace(profile.Server))
            errors.Add("Server is required.");

        if (policy.RequireDatabase && string.IsNullOrWhiteSpace(profile.Database))
            errors.Add("Database is required.");

        return errors;
    }

    /// <summary>
    /// Collects validation errors for a profile including credential presence and
    /// connection-string compatibility.
    /// </summary>
    /// <param name="profile">The profile to validate.</param>
    /// <param name="policy">The required-field policy.</param>
    /// <returns>
    /// User-facing validation messages in required-field, credential, then
    /// connection-string order; an empty list indicates success.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the validation applied by <see cref="SqlServerConnectionStore.Copy"/>
    /// and reused by the connection commands, so a stored profile and a copied
    /// profile are held to the same standard.
    /// </para>
    /// <para>
    /// SQL-password authentication is satisfied by either an in-memory
    /// <see cref="SqlServerConnectionProfile.PlainPassword"/> or a persisted
    /// <see cref="SqlServerConnectionProfile.EncryptedPassword"/>, so a profile whose
    /// protected password cannot be decrypted by the current user still validates.
    /// Connection-string compatibility is delegated to
    /// <see cref="Microsoft.Data.SqlClient.SqlConnectionStringBuilder"/> rather than
    /// reimplemented, and no SQL connection is opened.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> or <paramref name="policy"/> is
    /// <see langword="null"/>. The parameter name identifies the null argument.
    /// </exception>
    public static IReadOnlyList<string> ValidateComplete(
        SqlServerConnectionProfile profile,
        SqlServerConnectionValidationPolicy policy)
    {
        var errors = Validate(profile, policy).ToList();

        if (profile.Authentication == AuthenticationType.SqlPassword)
        {
            if (string.IsNullOrWhiteSpace(profile.Username))
                errors.Add("Username is required for SQL password authentication.");

            if (string.IsNullOrEmpty(profile.PlainPassword) &&
                string.IsNullOrEmpty(profile.EncryptedPassword))
            {
                errors.Add("Password is required for SQL password authentication.");
            }
        }

        // Let SqlConnectionStringBuilder validate the option surface (unknown --opt keys,
        // out-of-range pool sizes, malformed values, ...).
        try
        {
            _ = profile.BuildConnectionString();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            errors.Add(ex.Message);
        }

        return errors;
    }
}
