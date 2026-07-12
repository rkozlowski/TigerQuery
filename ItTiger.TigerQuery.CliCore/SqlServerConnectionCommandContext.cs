using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class SqlServerConnectionCommandContext(
    SqlServerConnectionStore store,
    SqlServerConnectionValidationPolicy validationPolicy)
{
    public SqlServerConnectionStore Store { get; } = store;
    public SqlServerConnectionValidationPolicy ValidationPolicy { get; } = validationPolicy;
}
