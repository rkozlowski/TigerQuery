namespace ItTiger.TigerQuery.Core;

public sealed class SqlServerConnectionValidationPolicy
{
    public static SqlServerConnectionValidationPolicy DatabaseOptional { get; } = new()
    {
        RequireDatabase = false
    };

    public static SqlServerConnectionValidationPolicy DatabaseRequired { get; } = new()
    {
        RequireDatabase = true
    };

    public bool RequireDatabase { get; init; }
}
