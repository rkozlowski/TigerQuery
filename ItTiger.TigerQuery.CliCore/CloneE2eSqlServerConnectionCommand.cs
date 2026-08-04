using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class CloneE2eSqlServerConnectionSettings : TigerCliSettings
{
    [TigerCliArgument(0, Name = "source-connection", Description = "Source connection name.",
        Provider = "connections")]
    public string SourceConnection { get; set; } = string.Empty;

    [TigerCliOption("--database", ValueName = "name", Description = "Existing database name.",
        Required = true)]
    public string Database { get; set; } = string.Empty;

    [TigerCliOption("--session-id", ValueName = "guid", Description = "E2E session correlation ID.",
        Required = true)]
    public string SessionId { get; set; } = string.Empty;

    [TigerCliOption("--name-part", ValueName = "text",
        Description = "Default generated connection name part.")]
    public string? NamePart { get; set; }

    [TigerCliOption("--connection-name-part", ValueName = "text",
        Description = "Generated connection name part, overriding --name-part.")]
    public string? ConnectionNamePart { get; set; }
}

internal sealed class CloneE2eSqlServerConnectionCommand(SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<CloneE2eSqlServerConnectionSettings, TigerCliExitKind>
{
    public override Task<TigerCliExitKind> ExecuteAsync(CloneE2eSqlServerConnectionSettings settings)
    {
        if (!TrySessionId(settings, out var sessionId))
            return Task.FromResult(TigerCliExitKind.ValidationError);

        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var connectionName = SqlServerE2eNames.Connection(
                settings.ConnectionNamePart ?? settings.NamePart,
                suffix);
            var profile = context.Store.CopyForE2eSession(
                settings.SourceConnection,
                connectionName,
                settings.Database,
                sessionId,
                allowDatabaseDrop: false);
            TigerConsole.MarkupLine(settings.E(
                "Created E2E connection [Value]{0}[/] for database [Value]{1}[/].",
                profile.Name,
                settings.Database));
            return Task.FromResult(TigerCliExitKind.Success);
        }
        catch (ArgumentException ex)
        {
            TigerConsole.MarkupErrorLine(CliMarkupParser.Escape(ex.Message));
            return Task.FromResult(TigerCliExitKind.ValidationError);
        }
        catch (InvalidOperationException ex)
        {
            TigerConsole.MarkupErrorLine(CliMarkupParser.Escape(ex.Message));
            return Task.FromResult(
                ex.Message.Contains("already exists", StringComparison.Ordinal)
                    ? TigerCliExitKind.AlreadyExists
                    : ex.Message.Contains("was not found", StringComparison.Ordinal)
                        ? TigerCliExitKind.NotFound
                        : TigerCliExitKind.ValidationError);
        }
    }

    private static bool TrySessionId(
        CloneE2eSqlServerConnectionSettings settings,
        out Guid sessionId)
    {
        if (!Guid.TryParse(settings.SessionId, out sessionId) || sessionId == Guid.Empty)
        {
            TigerConsole.MarkupErrorLine(
                "--session-id must be a non-empty valid GUID.");
            return false;
        }

        return true;
    }
}
