using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.E2e;

namespace ItTiger.TigerSqlCmd;

internal abstract class TigerSqlCmdE2eSessionSettings : TigerCliSettings
{
    [TigerCliOption("--session-id", ValueName = "guid",
        Description = "Required E2E safety-correlation session ID.", Required = true)]
    public string SessionId { get; set; } = string.Empty;

    public bool TryGetSessionId(out Guid sessionId)
    {
        if (Guid.TryParse(SessionId, out sessionId) && sessionId != Guid.Empty)
            return true;

        TigerConsole.MarkupErrorLine("--session-id must be a non-empty valid GUID.");
        return false;
    }
}

internal sealed class TigerSqlCmdE2eCreateSettings : TigerSqlCmdE2eSessionSettings
{
    [TigerCliOption("--name-part", ValueName = "text",
        Description = "Default part for both generated names.")]
    public string? NamePart { get; set; }

    [TigerCliOption("--database-name-part", ValueName = "text",
        Description = "Database name part, overriding --name-part.")]
    public string? DatabaseNamePart { get; set; }

    [TigerCliOption("--connection-name-part", ValueName = "text",
        Description = "Connection name part, overriding --name-part.")]
    public string? ConnectionNamePart { get; set; }
}

internal sealed class TigerSqlCmdE2eDropSettings : TigerSqlCmdE2eSessionSettings
{
    [TigerCliOption("--connection", ValueName = "name",
        Description = "Exact saved E2E connection name.", Required = true, Provider = "connections")]
    public string Connection { get; set; } = string.Empty;
}

internal sealed class TigerSqlCmdE2eCleanupSettings : TigerSqlCmdE2eSessionSettings;

internal sealed class TigerSqlCmdE2eCreateCommand(TigerQueryCliOptions tigerQuery)
    : TigerCliAsyncCommandHandler<TigerSqlCmdE2eCreateSettings, TigerCliExitKind>
{
    public override async Task<TigerCliExitKind> ExecuteAsync(TigerSqlCmdE2eCreateSettings settings)
    {
        if (!settings.TryGetSessionId(out var sessionId))
            return TigerCliExitKind.ValidationError;

        try
        {
            var lifecycle = CreateLifecycle(tigerQuery);
            var result = await lifecycle.CreateAsync(
                sessionId,
                settings.DatabaseNamePart ?? settings.NamePart,
                settings.ConnectionNamePart ?? settings.NamePart,
                CancellationToken.None).ConfigureAwait(false);
            TigerConsole.MarkupLine(
                $"Created E2E database [Value]{CliMarkupParser.Escape(result.DatabaseName)}[/].");
            TigerConsole.MarkupLine(
                $"Created E2E connection [Value]{CliMarkupParser.Escape(result.ConnectionName)}[/].");
            return TigerCliExitKind.Success;
        }
        catch (Exception ex) when (
            ex is ArgumentException or InvalidOperationException or SqlServerE2eCreateException)
        {
            TigerConsole.MarkupErrorLine(CliMarkupParser.Escape(ex.Message));
            return TigerCliExitKind.ValidationError;
        }
    }

    internal static SqlServerE2eSessionLifecycle CreateLifecycle(TigerQueryCliOptions tigerQuery) =>
        new(
            tigerQuery.Store,
            tigerQuery.DefaultE2eBootstrapConnectionName
                ?? throw new InvalidOperationException("An E2E bootstrap connection name is required."));
}

internal sealed class TigerSqlCmdE2eDropCommand(TigerQueryCliOptions tigerQuery)
    : TigerCliAsyncCommandHandler<TigerSqlCmdE2eDropSettings, TigerCliExitKind>
{
    public override async Task<TigerCliExitKind> ExecuteAsync(TigerSqlCmdE2eDropSettings settings)
    {
        if (!settings.TryGetSessionId(out var sessionId))
            return TigerCliExitKind.ValidationError;

        try
        {
            var result = await TigerSqlCmdE2eCreateCommand.CreateLifecycle(tigerQuery)
                .DropAsync(settings.Connection, sessionId, CancellationToken.None)
                .ConfigureAwait(false);
            Report(settings, result);
            return TigerCliExitKind.Success;
        }
        catch (InvalidOperationException ex)
        {
            TigerConsole.MarkupErrorLine(CliMarkupParser.Escape(ex.Message));
            return ex.Message.Contains("was not found", StringComparison.Ordinal)
                ? TigerCliExitKind.NotFound
                : TigerCliExitKind.ValidationError;
        }
    }

    internal static void Report(TigerCliSettings settings, SqlServerE2eDropResult result)
    {
        var message = result.Disposition switch
        {
            SqlServerE2eDropDisposition.ConnectionRemoved =>
                $"Removed non-owning E2E connection '{result.ConnectionName}'; database '{result.DatabaseName}' was not changed.",
            SqlServerE2eDropDisposition.DatabaseDroppedAndConnectionRemoved =>
                $"Dropped E2E database '{result.DatabaseName}' and removed connection '{result.ConnectionName}'.",
            _ =>
                $"E2E database '{result.DatabaseName}' was already absent; removed connection '{result.ConnectionName}'."
        };
        TigerConsole.MarkupLine(CliMarkupParser.Escape(message));
    }
}

internal sealed class TigerSqlCmdE2eCleanupCommand(TigerQueryCliOptions tigerQuery)
    : TigerCliAsyncCommandHandler<TigerSqlCmdE2eCleanupSettings, TigerCliExitKind>
{
    public override async Task<TigerCliExitKind> ExecuteAsync(TigerSqlCmdE2eCleanupSettings settings)
    {
        if (!settings.TryGetSessionId(out var sessionId))
            return TigerCliExitKind.ValidationError;

        var result = await TigerSqlCmdE2eCreateCommand.CreateLifecycle(tigerQuery)
            .CleanupAsync(sessionId, CancellationToken.None)
            .ConfigureAwait(false);
        if (result.Items.Count == 0)
            TigerConsole.MarkupLine("No matching E2E connections were found.");

        foreach (var item in result.Items)
        {
            if (item.IsSuccess)
            {
                TigerSqlCmdE2eDropCommand.Report(
                    settings,
                    new SqlServerE2eDropResult(
                        item.ConnectionName,
                        item.DatabaseName!,
                        item.Disposition!.Value));
            }
            else
            {
                TigerConsole.MarkupErrorLine(CliMarkupParser.Escape(
                    $"Cleanup failed for connection '{item.ConnectionName}': {item.Error}"));
            }
        }

        return result.IsComplete ? TigerCliExitKind.Success : TigerCliExitKind.ValidationError;
    }
}
