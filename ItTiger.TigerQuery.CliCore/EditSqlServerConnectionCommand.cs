using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Terminal;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class EditSqlServerConnectionCommand(SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<SqlServerConnectionSettings>
{
    /// <summary>
    /// Edit loader for <c>.AsEdit()</c>. Seeds the command settings from the existing
    /// profile so unsupplied options are preserved, or reports the connection missing.
    /// </summary>
    public static TigerCliEditLoad<SqlServerConnectionSettings> Load(
        SqlServerConnectionSettings settings,
        SqlServerConnectionCommandContext context)
    {
        var existing = context.Store.Find(settings.Name);
        return existing is null
            ? TigerCliEditLoad<SqlServerConnectionSettings>.NotFound()
            : TigerCliEditLoad<SqlServerConnectionSettings>.Found(
                SqlServerConnectionSettingsMapper.FromProfile(existing));
    }

    public override Task<int> ExecuteAsync(SqlServerConnectionSettings settings)
    {
        // The framework only reaches the handler when the loader returned Found, but
        // re-read to carry the stored password metadata forward.
        var existing = context.Store.Find(settings.Name);
        if (existing is null)
        {
            TigerConsole.MarkupErrorLine(settings.E(
                "SQL Server connection [White]{0}[/] was not found.",
                settings.Name));

            return Task.FromResult(SqlServerConnectionCommandExitCodes.NotFound);
        }

        var profile = SqlServerConnectionSettingsMapper.ToProfile(settings, existing);

        var errors = SqlServerConnectionWriter.Validate(profile, context.ValidationPolicy);
        if (SqlServerConnectionWriter.TryReportErrors(settings, errors))
            return Task.FromResult(SqlServerConnectionCommandExitCodes.InvalidArguments);

        context.Store.AddOrUpdate(profile);
        TigerConsole.MarkupLine(settings.E(
            "Updated SQL Server connection [White]{0}[/].",
            profile.Name));

        return Task.FromResult(SqlServerConnectionCommandExitCodes.Ok);
    }
}
