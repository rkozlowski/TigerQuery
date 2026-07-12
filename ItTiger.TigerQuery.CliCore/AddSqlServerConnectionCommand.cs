using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Terminal;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class AddSqlServerConnectionCommand(SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<SqlServerConnectionSettings, SqlServerConnectionExitCode>
{
    public override Task<SqlServerConnectionExitCode> ExecuteAsync(SqlServerConnectionSettings settings)
    {
        if (context.Store.Exists(settings.Name))
        {
            TigerConsole.MarkupErrorLine(settings.E(
                "SQL Server connection [Value]{0}[/] already exists. Use [Value]edit[/] to change it.",
                settings.Name));

            return Task.FromResult(SqlServerConnectionExitCode.AlreadyExists);
        }

        var profile = SqlServerConnectionSettingsMapper.ToProfile(settings, existing: null);

        var errors = SqlServerConnectionWriter.Validate(profile, context.ValidationPolicy);
        if (SqlServerConnectionWriter.TryReportErrors(settings, errors))
            return Task.FromResult(SqlServerConnectionExitCode.InvalidArguments);

        context.Store.Add(profile);
        TigerConsole.MarkupLine(settings.E(
            "Added SQL Server connection [Value]{0}[/].",
            profile.Name));

        return Task.FromResult(SqlServerConnectionExitCode.Ok);
    }
}
