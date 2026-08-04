using System.Resources;
using ItTiger.Core.Resources;
using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Enums;
using ItTiger.TigerCli.Primitives;
using ItTiger.TigerQuery.CliCore.Resources;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Composes reusable SQL Server connection-management commands into a TigerCli
/// command group.
/// </summary>
/// <remarks>
/// Host applications supply the shared <see cref="TigerQueryCliOptions"/> the run selects
/// its store through, register resources, and own final numeric exit-code mapping. The
/// mounted commands return semantic <see cref="TigerCliExitKind"/> outcomes such as
/// success, validation error, not found, and already exists.
/// </remarks>
public static class SqlServerConnectionCommands
{
    /// <summary>
    /// Builds the <see cref="ResourceManager"/> a consuming app registers with TigerCli's
    /// <c>UseAppResources(...)</c>. The app's own managers (if any) are consulted first, so
    /// the app can override library strings; the connection-command strings (en-US and
    /// pl-PL) act as the fallback for the metadata, enum text, and output owned here.
    /// </summary>
    /// <param name="appResources">
    /// Zero or more host resource managers, in lookup precedence order.
    /// </param>
    /// <returns>
    /// A chained manager ending with the connection-command resources.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="appResources"/> is <see langword="null"/>.
    /// </exception>
    public static ResourceManager CreateAppResources(params ResourceManager[] appResources)
    {
        ArgumentNullException.ThrowIfNull(appResources);
        return new ChainedResourceManager(
            [.. appResources, SqlServerConnectionCommandStrings.ResourceManager]);
    }

    /// <summary>
    /// Mounts the connection providers and list/show/add/add-e2e-bootstrap/clone-e2e/edit/delete
    /// commands.
    /// </summary>
    /// <param name="group">The TigerCli command group to configure.</param>
    /// <param name="configure">
    /// A callback that supplies the shared
    /// <see cref="SqlServerConnectionCommandOptions.TigerQuery"/> state and the optional
    /// validation policy.
    /// </param>
    /// <remarks>
    /// <para>
    /// Configuration enables prompting for the group, registers saved-connection
    /// and live-database providers, and adds the seven command handlers. Add and edit
    /// expose repeatable generic metadata mutations; list exposes ordinal,
    /// case-sensitive metadata filters combined with AND semantics. Generic mutations
    /// reject TigerQuery's reserved namespace, while the add-only E2E switches and
    /// bootstrap command use the TigerQuery-owned authorization path.
    /// </para>
    /// <para>
    /// Edit begins with the existing profile and preserves application-owned
    /// metadata unless explicit metadata set/remove options change it. Built-in
    /// output and command metadata are localized through the resource manager
    /// returned by <see cref="CreateAppResources"/>.
    /// </para>
    /// <para>
    /// The handlers return semantic <see cref="TigerCliExitKind"/> values. The host
    /// must map those values to its own numeric exit-code policy.
    /// </para>
    /// <para>
    /// Nothing here reads the store: the handlers, the two providers, and the edit loader
    /// all reach it when they run, which is after the contribution callback has selected
    /// it. The commands therefore mount identically whether or not the run overrides the
    /// path.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="group"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="SqlServerConnectionCommandOptions.TigerQuery"/> was not configured, or
    /// <see cref="SqlServerConnectionCommandOptions.ValidationPolicy"/> is null.
    /// </exception>
    public static void Configure(
        TigerCliCommandGroupBuilder group,
        Action<SqlServerConnectionCommandOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(group);

        var options = new SqlServerConnectionCommandOptions();
        configure?.Invoke(options);

        var context = new SqlServerConnectionCommandContext(
            CreateStoreAccessor(options),
            options.ValidationPolicy ?? throw new InvalidOperationException("A validation policy is required."),
            options.TigerQuery!.DefaultE2eBootstrapConnectionName);

        group.SetPromptMode(TigerCliPromptMode.Yes);

        group.AddProvider(
            "connections",
            ctx => context.Store.GetConnectionNamesAsync(ctx.CancellationToken));

        // Database selection is provider-backed and prompted last: it depends on the
        // options needed to open a server connection so the effective server/security
        // settings are known before databases are enumerated.
        group.AddProvider<SqlServerConnectionInputSettings, string>(
            "databases",
            async (settings, ctx) =>
            {
                var probe = SqlServerConnectionSettingsMapper.ToProbeProfile(settings);
                var names = await SqlServerDatabaseLister.ListAsync(probe, ctx.CancellationToken)
                    .ConfigureAwait(false);
                var items = names.Select(name => new OptionItem<string>(name, name)).ToList();
                return items;
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
            "add-e2e-bootstrap",
            () => new AddE2eBootstrapSqlServerConnectionCommand(context),
            "Add an E2E-authorized bootstrap SQL Server connection.",
            descriptionResourceKey: "Cmd_Connections_AddE2eBootstrap_Description")
            .SetPromptMode(TigerCliPromptMode.RequiredOnly);
        group.AddCommand(
            "clone-e2e",
            () => new CloneE2eSqlServerConnectionCommand(context),
            "Clone a connection for an existing E2E database without taking database ownership.")
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

    /// <summary>
    /// Turns the configured store selection into the accessor the commands call at
    /// execution time, rejecting a host that configured no selection at all.
    /// </summary>
    private static Func<SqlServerConnectionStore> CreateStoreAccessor(
        SqlServerConnectionCommandOptions options)
    {
        var tigerQuery = options.TigerQuery
            ?? throw new InvalidOperationException(
                "A SQL Server connection store is required. Set "
                + $"{nameof(SqlServerConnectionCommandOptions.TigerQuery)} to the same "
                + $"{nameof(TigerQueryCliOptions)} instance the host registered its "
                + $"{nameof(TigerQueryCliContribution)} with.");

        return () => tigerQuery.Store;
    }
}
