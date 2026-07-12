using System.Resources;

namespace ItTiger.TigerSqlCmd.Resources;

/// <summary>
/// App-owned strings for tiger-sqlcmd. Identifier keys (<c>App_*</c>, <c>Grp_*</c>,
/// <c>Cmd_*</c>, <c>Opt_*</c>) back builder/attribute metadata; source-text keys back
/// <c>settings.T/F/E</c> command output. Registered with TigerCli chained in front of
/// the reusable CliCore connection-command strings (see <c>TigerSqlCmdApp.Build</c>).
/// </summary>
internal static class TigerSqlCmdStrings
{
    private static readonly ResourceManager _resourceManager = new(
        "ItTiger.TigerSqlCmd.Resources.TigerSqlCmdStrings",
        typeof(TigerSqlCmdStrings).Assembly);

    public static ResourceManager ResourceManager => _resourceManager;
}
