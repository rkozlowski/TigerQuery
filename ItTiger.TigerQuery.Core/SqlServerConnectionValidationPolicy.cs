namespace ItTiger.TigerQuery.Core;

/// <summary>Controls which profile fields are required by connection validation.</summary>
public sealed class SqlServerConnectionValidationPolicy
{
    /// <summary>Gets the reusable policy that permits a server-level connection.</summary>
    public static SqlServerConnectionValidationPolicy DatabaseOptional { get; } = new()
    {
        RequireDatabase = false
    };

    /// <summary>Gets the reusable policy that requires an initial database.</summary>
    public static SqlServerConnectionValidationPolicy DatabaseRequired { get; } = new()
    {
        RequireDatabase = true
    };

    /// <summary>Gets whether a nonblank database name is required.</summary>
    public bool RequireDatabase { get; init; }
}
