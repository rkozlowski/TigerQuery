using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

public sealed class SqlServerConnectionCommandOptions
{
    public SqlServerConnectionStore? Store { get; set; }

    public SqlServerConnectionValidationPolicy ValidationPolicy { get; set; } =
        SqlServerConnectionValidationPolicy.DatabaseOptional;
}
