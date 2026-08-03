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

        if (profile.ConnectionStringValue is not null)
        {
            if (profile.HasIndividualConnectionSettings())
            {
                errors.Add(
                    "A full connection string cannot be combined with individual connection fields.");
            }

            AddValueDefinitionErrors(
                errors,
                "Connection string",
                profile.ConnectionStringValue,
                required: true);
            return errors;
        }

        AddValueDefinitionErrors(errors, "Server", profile.ServerValue, required: true);

        if (profile.DatabaseValue is not null)
            AddValueDefinitionErrors(errors, "Database", profile.DatabaseValue, policy.RequireDatabase);
        else if (policy.RequireDatabase)
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

        if (profile.ConnectionStringValue is null
            && profile.Authentication == AuthenticationType.SqlPassword)
        {
            if (profile.UsernameValue is null)
                errors.Add("Username is required for SQL password authentication.");
            else
                AddValueDefinitionErrors(errors, "Username", profile.UsernameValue, required: true);

            if (string.IsNullOrEmpty(profile.PlainPassword) &&
                string.IsNullOrEmpty(profile.EncryptedPassword) &&
                profile.PasswordValue is null)
            {
                errors.Add("Password is required for SQL password authentication.");
            }
            else if (profile.PasswordValue is not null)
            {
                AddValueDefinitionErrors(errors, "Password", profile.PasswordValue, required: true);
                if (!string.IsNullOrEmpty(profile.PlainPassword)
                    || !string.IsNullOrEmpty(profile.EncryptedPassword))
                {
                    errors.Add(
                        "A password value cannot be combined with a protected or in-memory password.");
                }
            }
        }
        else if (profile.ConnectionStringValue is null
                 && (profile.UsernameValue?.IsReference == true
                     || profile.PasswordValue?.IsReference == true))
        {
            errors.Add(
                "External username and password references require SQL password authentication.");
        }

        // Let SqlConnectionStringBuilder validate the option surface (unknown --opt keys,
        // out-of-range pool sizes, malformed values, ...).
        try
        {
            profile.ValidateConnectionStringCompatibility();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            errors.Add(profile.RedactSensitiveValues(ex.Message));
        }
        catch (SqlServerExternalValueException ex)
        {
            errors.Add(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(profile.RedactSensitiveValues(ex.Message));
        }

        return errors;
    }

    private static void AddValueDefinitionErrors(
        List<string> errors,
        string fieldName,
        SqlServerConnectionValue value,
        bool required)
    {
        if (value.Reference is not null)
        {
            foreach (var error in value.Reference.Validate())
                errors.Add($"{fieldName} reference is invalid: {error}");
            return;
        }

        if (required && string.IsNullOrWhiteSpace(value.LiteralValue))
            errors.Add($"{fieldName} is required.");
    }
}
