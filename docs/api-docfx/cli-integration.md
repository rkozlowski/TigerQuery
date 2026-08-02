# CLI integration

The [ItTiger.TigerQuery.CliCore](https://www.nuget.org/packages/ItTiger.TigerQuery.CliCore/)
package adds a reusable `connections` command group to applications built with
[TigerCli](https://www.nuget.org/packages/ItTiger.TigerCli/). It provides
list, show, add, edit, and delete commands backed by
[ItTiger.TigerQuery.Core](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/).

Install it with:

```console
dotnet add package ItTiger.TigerQuery.CliCore
```

The public composition surface is deliberately small:
[SqlServerConnectionCommands](xref:ItTiger.TigerQuery.CliCore.SqlServerConnectionCommands),
[SqlServerConnectionCommandOptions](xref:ItTiger.TigerQuery.CliCore.SqlServerConnectionCommandOptions),
[TigerQueryCliContribution](xref:ItTiger.TigerQuery.CliCore.TigerQueryCliContribution),
and
[TigerQueryCliOptions](xref:ItTiger.TigerQuery.CliCore.TigerQueryCliOptions).
Individual command handlers, settings models, writers, and resource
implementations are internal and are not part of this API reference.

```csharp
using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;

// One instance, shared by the contribution and every consumer of connections.
var tigerQuery = new TigerQueryCliContribution(new TigerQueryCliOptions
{
    DefaultConnectionStoreFile =
        SqlServerConnectionStoreOptions.AppSpecific("YourVendor", "your-tool").FilePath
});

var app = TigerCliApp.CreateBuilder()
    .UseAssemblyMetadata(typeof(Program).Assembly)
    .UseAppResources(SqlServerConnectionCommands.CreateAppResources())
    .AddContribution(tigerQuery)
    .AddCommandGroup("connections", group =>
    {
        group.SetDescription("Manage saved connections");
        SqlServerConnectionCommands.Configure(group, options =>
        {
            options.TigerQuery = tigerQuery.Options;
            options.ValidationPolicy =
                SqlServerConnectionValidationPolicy.DatabaseOptional;
        });
    })
    .AddCommand("run", () => new RunCommand(tigerQuery.Options), "Run a script.")
    .Build();

return await app.RunAsync(args);
```

The host application retains control of themes, cultures, other commands, and
exit-code mapping. Connection commands return portable semantic TigerCli exit
kinds such as success, validation error, not found, and already exists.

## Letting a run select the store

Registering the contribution gives the application a standard
`--tq-connection-store-file <path>` option and `TIGERQUERY_CONNECTION_STORE_FILE`
support, so one run can work against a different store without a per-tool
option name or precedence rule. Precedence and validation belong to
[ItTiger.TigerQuery.Core](connection-profiles.md#letting-a-run-choose-the-path);
this package only carries the value across and reports failures as TigerCli
validation errors.

Registering the contribution and mounting the `connections` group are separate
opt-ins — either works without the other. The rule that matters is to create the
`TigerQueryCliOptions` **once** and give that same instance to
`AddContribution`, to `options.TigerQuery`, and to the application's own command
factories and services. Two instances leave the run resolving a path that
nothing reads.

TigerCli applies the option once per run, before command settings are bound, so
a bad path ends the run cleanly rather than failing inside a handler. Help
rendering invokes no callback, so help text can use
`DefaultConnectionStoreFile` but never the resolved path. The store is built
lazily on first use of `TigerQueryCliOptions.Store` and then reused for the rest
of the run, so the commands, the providers, the `edit` loader, and host services
all share one instance — one file, one lock, one mutation gate.

`options.Store` remains available for an application that selects one fixed
store itself and offers no store-path option. It is mutually exclusive with
`options.TigerQuery`; configuring both is rejected when the group is configured,
because a fixed store would ignore whatever the run selected. See
[Selecting one store](connection-profiles.md#selecting-one-store).

## Localization and metadata

`CreateAppResources(params ResourceManager[])` creates a chained resource
manager. Host resources are consulted first, followed by the built-in en-US and
pl-PL connection-command strings.

The add and edit commands accept repeatable metadata mutations. The list
command accepts repeatable value, key-present, and key-absent filters; every
filter must match. Metadata is application-owned, case-sensitive, excluded from
connection strings, and must not contain secrets.
