# ItTiger.TigerQuery.CliCore

Reusable **TigerCli command group for SQL Server connection management**, used by `tiger-sqlcmd` and other TigerQuery-family command-line tools.

This package is for **developers building [TigerCli](https://www.nuget.org/packages/ItTiger.TigerCli/) applications**, not for end users. Mount it in your app and you get a complete `connections` command group:

- `list` / `show` — structured table and details output, including metadata filters and a separate metadata section
- `add` / `edit` — parser-driven prompting, provider-backed selection (including live database enumeration), shared add/edit option surface, TigerCli `.AsEdit()` merge semantics, and repeatable metadata mutations
- `delete`
- Domain validation with clear errors and portable `TigerCliExitKind` outcomes
- en-US and pl-PL resources, merged behind your app's own resources so you can override any string

Profiles are stored through [ItTiger.TigerQuery.Core](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/), which this package depends on (along with `ItTiger.TigerCli` and `ItTiger.Core`).

## Installation

```
dotnet add package ItTiger.TigerQuery.CliCore
```

## Quick start

The public composition surface is `SqlServerConnectionCommands` plus `SqlServerConnectionCommandOptions`; the individual command and settings types are intentionally internal.

```csharp
using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;

var store = new SqlServerConnectionStore(
    SqlServerConnectionStoreOptions.AppSpecific("YourVendor", "your-tool"));

var app = TigerCliApp.CreateBuilder()
    .UseAssemblyMetadata(typeof(Program).Assembly)
    // Chain your own ResourceManager(s) in front to override or localize strings.
    .UseAppResources(SqlServerConnectionCommands.CreateAppResources())
    .AddCommandGroup("connections", group =>
    {
        group.SetDescription("Manage saved connections");
        SqlServerConnectionCommands.Configure(group, options =>
        {
            options.Store = store;
            options.ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional;
        });
    })
    .Build();

return await app.RunAsync(args);
```

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
