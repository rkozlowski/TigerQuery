using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Enums;
using ItTiger.TigerCli.Primitives;
using ItTiger.TigerCli.Rendering;
using ItTiger.TigerCli.Terminal;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class ListSqlServerConnectionsSettings : TigerCliSettings;

internal sealed class ListSqlServerConnectionsCommand(SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<ListSqlServerConnectionsSettings>
{
    public override Task<int> ExecuteAsync(ListSqlServerConnectionsSettings s)
    {
        var profiles = context.Store.Load().OrderBy(profile => profile.Name).ToList();
        if (profiles.Count == 0)
        {
            TigerConsole.MarkupErrorLine(s.T("No SQL Server connections."));
            return Task.FromResult(SqlServerConnectionCommandExitCodes.Ok);
        }

        var table = new CliTable()
            .ApplyPreset(CliTableStylePreset.Milano)
            .AddTitle(s.T("SQL Server connections"))
            .AddHeader(
                s.T("Name"),
                s.T("Server"),
                s.T("Authentication"),
                s.T("Database"));

        foreach (var profile in profiles)
        {
            table.AddRecord(
                profile.Name,
                profile.Server,
                profile.Authentication,
                profile.Database);
        }
        TigerConsole.Render(table);
        return Task.FromResult(SqlServerConnectionCommandExitCodes.Ok);
    }
}
