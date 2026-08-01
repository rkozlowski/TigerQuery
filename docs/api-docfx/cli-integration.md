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
[SqlServerConnectionCommands](xref:ItTiger.TigerQuery.CliCore.SqlServerConnectionCommands)
and
[SqlServerConnectionCommandOptions](xref:ItTiger.TigerQuery.CliCore.SqlServerConnectionCommandOptions).
Individual command handlers, settings models, writers, and resource
implementations are internal and are not part of this API reference.

```csharp
using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;

var store = new SqlServerConnectionStore(
    SqlServerConnectionStoreOptions.AppSpecific("YourVendor", "your-tool"));

var app = TigerCliApp.CreateBuilder()
    .UseAssemblyMetadata(typeof(Program).Assembly)
    .UseAppResources(SqlServerConnectionCommands.CreateAppResources())
    .AddCommandGroup("connections", group =>
    {
        group.SetDescription("Manage saved connections");
        SqlServerConnectionCommands.Configure(group, options =>
        {
            options.Store = store;
            options.ValidationPolicy =
                SqlServerConnectionValidationPolicy.DatabaseOptional;
        });
    })
    .Build();

return await app.RunAsync(args);
```

The host application retains control of themes, cultures, other commands, and
exit-code mapping. Connection commands return portable semantic TigerCli exit
kinds such as success, validation error, not found, and already exists.

## One selected store

`options.Store` is the store-selection injection point, and deliberately the
only one — TigerQuery defines no default store of its own. Resolve the
application's choice (a `Shared`/`AppSpecific` per-user location, or an explicit
`FilePath` for isolation, CI, or a test run) once, construct a single
[SqlServerConnectionStore](xref:ItTiger.TigerQuery.Core.SqlServerConnectionStore),
and pass that same instance to `SqlServerConnectionCommands.Configure` and to
every other consumer in the application. The store never probes another
location, so lookup, filtering, copy, add, update, save, and delete all stay on
the selected file; `store.FilePath` reports the normalized path it uses. See
[Selecting one store](connection-profiles.md#selecting-one-store).

## Localization and metadata

`CreateAppResources(params ResourceManager[])` creates a chained resource
manager. Host resources are consulted first, followed by the built-in en-US and
pl-PL connection-command strings.

The add and edit commands accept repeatable metadata mutations. The list
command accepts repeatable value, key-present, and key-absent filters; every
filter must match. Metadata is application-owned, case-sensitive, excluded from
connection strings, and must not contain secrets.
