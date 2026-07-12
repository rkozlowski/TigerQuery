namespace ItTiger.TigerQuery.Core;

public static class SqlServerConnectionValidator
{
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
