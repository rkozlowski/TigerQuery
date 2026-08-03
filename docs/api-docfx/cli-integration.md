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
        SqlServerConnectionStoreOptions.AppSpecific("YourVendor", "your-tool").FilePath,
    DefaultE2eBootstrapConnectionName = "your-tool-e2e"
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

`options.TigerQuery` is the only way to give the group a store. There is
deliberately no composition-time property that pins an already-constructed
`SqlServerConnectionStore`, because such a store would ignore whatever the run
selected; a group configured without it is rejected when the group is
configured. See
[Selecting one store](connection-profiles.md#selecting-one-store).

## Localization and metadata

`CreateAppResources(params ResourceManager[])` creates a chained resource
manager. Host resources are consulted first, followed by the built-in en-US and
pl-PL connection-command strings.

The add and edit commands accept repeatable metadata mutations. The list
command accepts repeatable value, key-present, and key-absent filters; every
filter must match. Metadata is application-owned, case-sensitive, excluded from
connection strings, and must not contain secrets. Generic set/remove operations
reject the lowercase `ittiger.*` namespace because it is reserved for
TigerQuery-owned metadata; list filters and reads remain forward-compatible with
unknown reserved keys.

## Creating E2E-authorized profiles

The regular add command can authorize a new profile for E2E use without making
it the host's bootstrap profile:

```console
your-tool connections add test-server --server sql01 --e2e
your-tool connections add test-creator --server sql01 --e2e --allow-database-create
```

`--e2e` writes exactly `ittiger.e2e.enabled=true`.
`--allow-database-create` additionally writes exactly
`ittiger.e2e.allow-database-create=true` and is rejected unless `--e2e` is also
present. Both are non-promptable switches. Authorization is not bootstrap
identity: a resolver never selects one of these profiles merely because it is
the only authorized profile.

The dedicated bootstrap creation command is:

```console
your-tool connections add-e2e-bootstrap [--name <name>] --server <server>
```

An explicit `--name` wins. Otherwise CliCore uses the host's
`DefaultE2eBootstrapConnectionName`. If neither supplies a usable name, the
command returns a validation error before opening or creating the selected
store, its directory, or a partial profile. The command always writes the E2E
authorization flag and accepts the same connection, prompting, validation, and
persistence options as regular add; `--allow-database-create` remains a
separate explicit permission.

## Configuring external values

The three connection-writing flows (`add`, `edit`, and
`add-e2e-bootstrap`) expose exactly five non-promptable reference options:

```text
--server-reference <json>
--database-reference <json>
--username-reference <json>
--password-reference <json>
--connection-string-reference <json>
```

Their JSON is Core's persisted reference object, not a value wrapper invented
by the CLI. Environment-variable, whole-text-file, and keyed-JSON-file forms are
documented under [External profile values](connection-profiles.md#external-profile-values).
Reference options reject JSON string literals, and sensitive connection-string
keys cannot be supplied through `--opt`, so the automation path does not expose
plaintext credentials in process arguments.

For a fully non-interactive SQL-auth bootstrap:

```console
your-tool connections add-e2e-bootstrap --non-interactive --authentication SqlPassword --server-reference {environment-reference-json} --username-reference {keyed-file-reference-json} --password-reference {text-file-reference-json}
```

Use `--connection-string-reference` by itself for full-string mode. It is
strictly mutually exclusive with field-based settings, including authentication,
encryption, pooling, and `--opt`. Validation failures occur before persistence.
Unrelated edits preserve reference objects, and inspection renders only source
descriptions without resolving external values.
