# ItTiger.TigerQuery.CliCore

Reusable **TigerCli command group for SQL Server connection management**, used by `tiger-sqlcmd` and other TigerQuery-family command-line tools.

This package is for **developers building [TigerCli](https://www.nuget.org/packages/ItTiger.TigerCli/) applications**, not for end users. Mount it in your app and you get a complete `connections` command group:

- `list` / `show` — structured table and details output, including metadata filters and a separate metadata section
- `add` / `edit` — parser-driven prompting, provider-backed selection (including live database enumeration), shared add/edit option surface, TigerCli `.AsEdit()` merge semantics, and repeatable metadata mutations
- `delete`
- Domain validation with clear errors and portable `TigerCliExitKind` outcomes
- en-US and pl-PL resources, merged behind your app's own resources so you can override any string

It also ships a TigerCli app contribution that gives your application a standard `--tq-connection-store-file <path>` global option and `TIGERQUERY_CONNECTION_STORE_FILE` support, so a run can select its connection store without every TigerQuery-family tool inventing its own option name and precedence rules.

Profiles are stored through [ItTiger.TigerQuery.Core](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/), which this package depends on (along with `ItTiger.TigerCli` and `ItTiger.Core`).

## Installation

```
dotnet add package ItTiger.TigerQuery.CliCore
```

## Quick start

The public composition surface is `SqlServerConnectionCommands`, `SqlServerConnectionCommandOptions`, `TigerQueryCliContribution`, and `TigerQueryCliOptions`; the individual command and settings types are intentionally internal.

```csharp
using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;

// One instance, shared by the contribution and everything that reads connections.
var tigerQuery = new TigerQueryCliContribution(new TigerQueryCliOptions
{
    DefaultConnectionStoreFile =
        SqlServerConnectionStoreOptions.AppSpecific("YourVendor", "your-tool").FilePath
});

var app = TigerCliApp.CreateBuilder()
    .UseAssemblyMetadata(typeof(Program).Assembly)
    // Chain your own ResourceManager(s) in front to override or localize strings.
    .UseAppResources(SqlServerConnectionCommands.CreateAppResources())
    .AddContribution(tigerQuery)
    .AddCommandGroup("connections", group =>
    {
        group.SetDescription("Manage saved connections");
        SqlServerConnectionCommands.Configure(group, options =>
        {
            options.TigerQuery = tigerQuery.Options;
            options.ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional;
        });
    })
    // Your own commands take the same options instance, so they read the same store.
    .AddCommand("run", () => new RunCommand(tigerQuery.Options), "Run a script.")
    .Build();

return await app.RunAsync(args);
```

### One selected store

A run reads and writes exactly one store file. Which file it is gets decided once, in this order:

1. `--tq-connection-store-file <path>` on the command line;
2. the `TIGERQUERY_CONNECTION_STORE_FILE` environment variable;
3. `TigerQueryCliOptions.DefaultConnectionStoreFile`, your application's default.

An unusable higher-priority value **fails the run** rather than falling through to the next source, so a build agent pointed at the wrong path never quietly uses a developer's personal store. Precedence, environment reading, and path normalization all live in `ItTiger.TigerQuery.Core`; this package only carries the value across.

Registering the contribution and mounting the `connections` group are separate opt-ins. Register `TigerQueryCliContribution` at most once, and if your app already called `AddEnvironmentVariable("TIGERQUERY_CONNECTION_STORE_FILE", …)`, drop that registration — the contribution adds it and a duplicate fails at `Build()`.

The one rule that matters: **create the `TigerQueryCliOptions` once** and give that same instance to `AddContribution`, to `options.TigerQuery`, and to your own command factories and services. Two instances, or one registered and a different one passed to the commands, gives you a run whose resolved path nothing reads.

`options.Store` remains available for an application that selects one fixed store itself and offers no store-path option. It is mutually exclusive with `options.TigerQuery`; configuring both is rejected at `Configure` time, because a fixed store would ignore whatever the run selected.

### Option placement and lifecycle

`--tq-connection-store-file` is app-wide in meaning but it is still an option, so TigerCli's grammar applies: write it **after the command path and any positional arguments**.

```text
your-tool connections list --tq-connection-store-file C:\temp\e2e.json   valid
your-tool --tq-connection-store-file C:\temp\e2e.json connections list   invalid
your-tool "select 1" --tq-connection-store-file=-oddly-named.json        valid (=form for values starting with -)
```

Supplying it twice is an error — TigerCli does not take the last value — and supplying it without a value is an argument error. It is never prompted and never appears on a settings type.

TigerCli invokes the contribution once per run, before command settings are bound, so a bad path fails cleanly ahead of any handler. That applies to every command, including ones that never open the store: a malformed `TIGERQUERY_CONNECTION_STORE_FILE` fails the whole run, deliberately. Help rendering invokes no callbacks, so `ResolvedStorePath` is null and `Store` throws during help; help text that wants to name a location must use `DefaultConnectionStoreFile`, which is known at build time.

The store itself is constructed lazily on first access to `TigerQueryCliOptions.Store` and then reused for the rest of the run, so the commands, both providers, the `edit` loader, and your own services share one instance — one file, one lock, one mutation gate. `store.FilePath` reports the normalized absolute path it settled on. Sequential runs of one built app re-resolve and replace the store rather than accumulate; `TigerQueryCliOptions` is not thread-safe and parallel in-process runs of a single app instance are not supported.

### Copying stores between machines

The default password protector is DPAPI-based on Windows and scoped to the current user and machine. A store file copied to a build agent or container cannot be decrypted there, and the failure looks like a connection error rather than a configuration one. Point `TIGERQUERY_CONNECTION_STORE_FILE` at a store created *on that machine*, or supply `TigerQueryCliOptions.PasswordProtectorFactory` with a protector that suits the environment.

Your application keeps full ownership of everything around the group: overall app composition, themes, cultures, additional commands, and the application-wide exit-code policy (`UseExitCodes(...)`). The connection commands return portable TigerCli outcomes: `Success`, `ValidationError`, `NotFound`, and `AlreadyExists`. Map those kinds to your application's concrete exit-code enum with `ExitKind(...)`.

`add` and `edit` accept repeatable `--metadata key=value` and
`--remove-metadata key` options. `list` accepts repeatable
`--metadata key=value`, `--metadata-set key`, and `--metadata-not-set key`
filters; every filter must match. Metadata remains opaque, application-owned,
case-sensitive string data, is never included in connection strings, and must
not contain secrets.

## Localization

Command metadata, prompts, enum labels, and output are localized (en-US, pl-PL). `CreateAppResources(params ResourceManager[])` returns a chained manager: your resource managers are consulted first, the built-in connection-command strings act as the fallback — register the result with TigerCli's `UseAppResources(...)`.

## Related packages

- [ItTiger.TigerCli](https://www.nuget.org/packages/ItTiger.TigerCli/) — the CLI framework this package plugs into.
- [ItTiger.TigerQuery.Core](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/) — the connection-profile model and storage.
- [ItTiger.TigerQuery](https://www.nuget.org/packages/ItTiger.TigerQuery/) — the sqlcmd-compatible script engine (not required by this package).
- [tiger-sqlcmd](https://github.com/rkozlowski/TigerQuery/releases) — a complete CLI using this group in production.

## Links

- Project page: https://www.ittiger.net/projects/tigerquery/
- Repository: https://github.com/rkozlowski/TigerQuery
- License: [MIT](https://github.com/rkozlowski/TigerQuery/blob/main/LICENSE)

An open-source project by **IT Tiger** — https://www.ittiger.net/
