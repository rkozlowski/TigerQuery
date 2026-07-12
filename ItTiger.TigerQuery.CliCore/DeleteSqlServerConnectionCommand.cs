using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Terminal;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class DeleteSqlServerConnectionSettings : TigerCliSettings
{
    [TigerCliArgument(0, Name = "name", Description = "Connection name.",
        DescriptionResourceKey = "Arg_Connection_Name_Description", Provider = "connections")]
    public string Name { get; set; } = string.Empty;
}

internal sealed class DeleteSqlServerConnectionCommand(SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<DeleteSqlServerConnectionSettings>
{
    public override Task<int> ExecuteAsync(DeleteSqlServerConnectionSettings settings)
    {
        if (!context.Store.Delete(settings.Name))
        {
            TigerConsole.MarkupErrorLine(settings.E(
                "SQL Server connection [White]{0}[/] was not found.",
                settings.Name));
            return Task.FromResult(SqlServerConnectionCommandExitCodes.NotFound);
        }

        TigerConsole.MarkupLine(settings.E(
            "Deleted SQL Server connection [White]{0}[/].",
            settings.Name));

        return Task.FromResult(SqlServerConnectionCommandExitCodes.Ok);
    }
}
