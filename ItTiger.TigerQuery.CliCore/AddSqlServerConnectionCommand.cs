using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Terminal;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class AddSqlServerConnectionCommand(SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<SqlServerConnectionSettings>
{
    public override Task<int> ExecuteAsync(SqlServerConnectionSettings settings)
    {
        if (context.Store.Exists(settings.Name))
        {
            TigerConsole.MarkupErrorLine(settings.E(
                "SQL Server connection [White]{0}[/] already exists. Use [White]edit[/] to change it.",
                settings.Name));

            return Task.FromResult(SqlServerConnectionCommandExitCodes.AlreadyExists);
        }

        var profile = SqlServerConnectionSettingsMapper.ToProfile(settings, existing: null);

        var errors = SqlServerConnectionWriter.Validate(profile, context.ValidationPolicy);
        if (SqlServerConnectionWriter.TryReportErrors(settings, errors))
            return Task.FromResult(SqlServerConnectionCommandExitCodes.InvalidArguments);

        context.Store.Add(profile);
        TigerConsole.MarkupLine(settings.E(
            "Added SQL Server connection [White]{0}[/].",
            profile.Name));

        return Task.FromResult(SqlServerConnectionCommandExitCodes.Ok);
    }
}
