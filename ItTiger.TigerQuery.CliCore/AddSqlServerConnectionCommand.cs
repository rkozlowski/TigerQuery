using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class AddSqlServerConnectionCommand(SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<AddSqlServerConnectionSettings, TigerCliExitKind>
{
    public override Task<TigerCliExitKind> ExecuteAsync(AddSqlServerConnectionSettings settings) =>
        SqlServerConnectionCreator.ExecuteAsync(
            settings,
            settings.Name,
            context,
            authorizeE2e: settings.E2e,
            allowDatabaseCreation: settings.AllowDatabaseCreation);
}

/// <summary>Shared creation, validation, and persistence path for both add commands.</summary>
internal static class SqlServerConnectionCreator
{
    public static Task<TigerCliExitKind> ExecuteAsync(
        SqlServerConnectionInputSettings settings,
        string name,
        SqlServerConnectionCommandContext context,
        bool authorizeE2e,
        bool allowDatabaseCreation)
    {
        var metadataError = SqlServerConnectionMetadataOptions.ValidateMutations(
            settings.Metadata,
            settings.RemoveMetadata);
        if (metadataError is not null)
        {
            SqlServerConnectionWriter.TryReportErrors(settings, [metadataError]);
            return Task.FromResult(TigerCliExitKind.ValidationError);
        }

        if (context.Store.Exists(name))
        {
            TigerConsole.MarkupErrorLine(settings.E(
                "SQL Server connection [Value]{0}[/] already exists. Use [Value]edit[/] to change it.",
                name));

            return Task.FromResult(TigerCliExitKind.AlreadyExists);
        }

        var profile = SqlServerConnectionSettingsMapper.ToProfile(
            settings,
            name,
            existing: null);

        if (authorizeE2e)
        {
            SqlServerE2eMetadata.AuthorizeNewProfile(
                profile,
                allowDatabaseCreation);
        }

        var errors = SqlServerConnectionWriter.Validate(profile, context.ValidationPolicy);
        if (SqlServerConnectionWriter.TryReportErrors(settings, errors))
            return Task.FromResult(TigerCliExitKind.ValidationError);

        context.Store.Add(profile);
        TigerConsole.MarkupLine(settings.E(
            "Added SQL Server connection [Value]{0}[/].",
            profile.Name));

        return Task.FromResult(TigerCliExitKind.Success);
    }
}
