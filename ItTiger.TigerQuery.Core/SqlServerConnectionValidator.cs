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
}
