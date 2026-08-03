using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Terminal;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class AddE2eBootstrapSqlServerConnectionCommand(
    SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<AddE2eBootstrapSqlServerConnectionSettings, TigerCliExitKind>
{
    public override Task<TigerCliExitKind> ExecuteAsync(
        AddE2eBootstrapSqlServerConnectionSettings settings)
    {
        var name = settings.Name ?? context.DefaultE2eBootstrapConnectionName;
        if (string.IsNullOrWhiteSpace(name))
        {
            TigerConsole.MarkupErrorLine(settings.E(
                "A bootstrap connection name is required. Use [Value]--name[/] or configure a host default."));
            return Task.FromResult(TigerCliExitKind.ValidationError);
        }

        return SqlServerConnectionCreator.ExecuteAsync(
            settings,
            name,
            context,
            authorizeE2e: true,
            allowDatabaseCreation: settings.AllowDatabaseCreation);
    }
}
