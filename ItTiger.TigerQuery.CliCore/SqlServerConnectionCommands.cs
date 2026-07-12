using System.Resources;
using ItTiger.Core.Resources;
using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Enums;
using ItTiger.TigerCli.Primitives;
using ItTiger.TigerQuery.CliCore.Resources;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

public static class SqlServerConnectionCommands
{
    /// <summary>
    /// Builds the <see cref="ResourceManager"/> a consuming app registers with TigerCli's
    /// <c>UseAppResources(...)</c>. The app's own managers (if any) are consulted first, so
    /// the app can override library strings; the connection-command strings (en-US and
    /// pl-PL) act as the fallback for the metadata, enum text, and output owned here.
    /// </summary>
    public static ResourceManager CreateAppResources(params ResourceManager[] appResources)
    {
        ArgumentNullException.ThrowIfNull(appResources);
        return new ChainedResourceManager(
            [.. appResources, SqlServerConnectionCommandStrings.ResourceManager]);
    }

    public static void Configure(
        TigerCliCommandGroupBuilder group,
        Action<SqlServerConnectionCommandOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(group);

        var options = new SqlServerConnectionCommandOptions();
        configure?.Invoke(options);

        if (options.Store is null)
            throw new InvalidOperationException("A SQL Server connection store is required.");
        SqlServerConnectionStore store = options.Store!;

        var context = new SqlServerConnectionCommandContext(
            store,
            options.ValidationPolicy ?? throw new InvalidOperationException("A validation policy is required."));

        group.SetPromptMode(TigerCliPromptMode.Yes);

        group.AddProvider(
            "connections",
            ctx => store.GetConnectionNamesAsync(ctx.CancellationToken));

        // Database selection is provider-backed and prompted last: it depends on the
        // options needed to open a server connection so the effective server/security
        // settings are known before databases are enumerated.
        group.AddProvider<SqlServerConnectionSettings, string>(
            "databases",
            async (settings, ctx) =>
            {
                var probe = SqlServerConnectionSettingsMapper.ToProbeProfile(settings);
                var names = await SqlServerDatabaseLister.ListAsync(probe, ctx.CancellationToken)
                    .ConfigureAwait(false);
                return names.Select(name => new OptionItem<string>(name, name)).ToList();
            },
            dependsOn:
            [
                "--server",
                "--authentication",
                "--username",
                "--password",
                "--encrypt",
                "--trust-server-certificate",
                "--application-intent"
            ]);

        group.AddCommand(
            "list",
            () => new ListSqlServerConnectionsCommand(context),
            "List saved SQL Server connections.",
            descriptionResourceKey: "Cmd_Connections_List_Description");
        group.AddCommand(
            "show",
            () => new ShowSqlServerConnectionCommand(context),
            "Show a saved SQL Server connection.",
            descriptionResourceKey: "Cmd_Connections_Show_Description");
        group.AddCommand(
            "add",
            () => new AddSqlServerConnectionCommand(context),
            "Add a SQL Server connection.",
            descriptionResourceKey: "Cmd_Connections_Add_Description")
            .SetPromptMode(TigerCliPromptMode.RequiredOnly);
        group.AddCommand(
            "edit",
            () => new EditSqlServerConnectionCommand(context),
            command => command.AsEdit<SqlServerConnectionSettings>(
                settings => EditSqlServerConnectionCommand.Load(settings, context)),
            "Edit a SQL Server connection.",
            descriptionResourceKey: "Cmd_Connections_Edit_Description")
            .SetPromptMode(TigerCliPromptMode.RequiredOnly);
        group.AddCommand(
            "delete",
            () => new DeleteSqlServerConnectionCommand(context),
            "Delete a saved SQL Server connection.",
            descriptionResourceKey: "Cmd_Connections_Delete_Description");
    }
}
