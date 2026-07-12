using System.Resources;

namespace ItTiger.TigerQuery.CliCore.Resources;

/// <summary>
/// Resource access for the reusable SQL Server connection commands. The resx uses
/// TigerCli source-text keys for command output and enum text (the English source is
/// both key and fallback) and identifier keys (<c>Cmd_*</c>, <c>Arg_*</c>, <c>Opt_*</c>)
/// for command/option metadata. A consuming app merges these strings into its own
/// app resources via <see cref="SqlServerConnectionCommands.CreateAppResources"/>.
/// </summary>
internal static class SqlServerConnectionCommandStrings
{
    private static readonly ResourceManager _resourceManager = new(
        "ItTiger.TigerQuery.CliCore.Resources.SqlServerConnectionCommandStrings",
        typeof(SqlServerConnectionCommandStrings).Assembly);

    public static ResourceManager ResourceManager => _resourceManager;
}
