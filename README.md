# TigerQuery

<img src="docs/assets/TigerQuery256.png" alt="TigerQuery Logo" width="80"/>

**TigerQuery** is a lightweight SQL script engine and parser with familiar
`sqlcmd` and SSMS SqlCmd-mode scripting, plus **`SqlCmdEx`**: an extended mode
for applications and automation that protects host-provided variables from
script-level `:setvar` overrides.

It powers `tiger-sqlcmd`, a modern CLI for executing complex `.sql` scripts with
repeatable batches, variable injection, and advanced scripting features. TigerSqlCmd
follows TigerCli's **One Command Model, Multiple Interaction Modes**: the same commands
serve normal semi-interactive execution and automation-safe `--non-interactive`
execution for scripts, CI, scheduled jobs, redirected runs, and coding agents.

## Documentation

**[Read the published TigerQuery documentation](https://rkozlowski.github.io/TigerQuery/).**

- [TigerSqlCmd interaction modes and usage](https://rkozlowski.github.io/TigerQuery/tiger-sqlcmd.html#one-command-model-multiple-interaction-modes)
- [TigerSqlCmd E2E scenarios](https://rkozlowski.github.io/TigerQuery/tiger-sqlcmd-e2e.html)

---

## 🧠 Philosophy

**TigerQuery is not a clone of sqlcmd.**  
It’s a deliberate reimplementation — compatible where it matters, safer where it should be, and documented with precision.

Unlike sqlcmd or SSMS, TigerQuery:

- Has a dedicated, composable parser
- Tracks batch structure and execution metadata
- Is fully test-covered and intentionally divergent where appropriate

---

## ✨ Features

- ✅ Familiar `:setvar`, `$(var)`, `:on error`, and `GO [n]` handling, including
  sqlcmd-compatible stop-on-error for severity 11-16 server errors
- ✅ `SqlCmdEx` protected host variables for automation and embedded tooling
- ✅ Fully async parser and execution engine
- ✅ Tracks exact line/column metadata per batch
- ✅ Structured error handling via `TigerQueryException`
- ✅ Differentiates between `sqlcmd`, `sqlcmdex`, and normal modes
- ✅ Easily embeddable in CLI tools or .NET apps

---

## 🔐 SqlCmdEx: protected variables for applications

`SqlCmdEx` is TigerQuery’s extended scripting mode for applications and
automation. It keeps familiar sqlcmd script syntax while allowing the host
application to provide protected variables that scripts cannot override with
`:setvar`.

| Mode | Application-provided variables | Script-local variables |
|---|---|---|
| `SqlCmd` | Seed the variable table, but `:setvar` can replace them | Created and updated by `:setvar` |
| `SqlCmdEx` | Take precedence; matching `:setvar` assignments are ignored | Still work when they do not conflict with protected host values |

Variable names are matched case-insensitively. For example, this script tries
to replace `TargetDatabase`:

```sql
:setvar TargetDatabase ScriptDatabase
PRINT 'Deploying $(TargetDatabase)';
GO
```

The host can protect its value by selecting `SqlCmdEx`:

```csharp
using System;
using System.Collections.Generic;
using ItTiger.TigerQuery;
using ItTiger.TigerQuery.Engine;

const string script = """
    :setvar TargetDatabase ScriptDatabase
    PRINT 'Deploying $(TargetDatabase)';
    GO
    """;

var options = new TigerQueryEngineOptions
{
    ConnectionString =
        "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true",
    Mode = SqlCmdMode.SqlCmdEx,
    Variables = new Dictionary<string, string>
    {
        ["TargetDatabase"] = "HostDatabase"
    },
    OnMessage = (message, _) => Console.WriteLine(message.Text)
};

await new TigerQueryEngine(options).RunFromStringAsync(script);
// PRINT reports: Deploying HostDatabase
```

This is useful for deployment automation, test orchestration, code generation,
database provisioning, and applications that inject environment or project
values into reusable scripts without allowing accidental overrides.

See [SqlCmd and SqlCmdEx in the hosted documentation](https://rkozlowski.github.io/TigerQuery/engine.html#sqlcmd-and-sqlcmdex)
for the detailed precedence rules.

## Prepared and streaming execution

`SqlCmdEx` works with both execution modes. Streaming is the memory-efficient
default; prepared execution parses the complete TigerQuery/sqlcmd structure
before opening the SQL connection. Mode selection and execution timing are
independent choices.

See [prepared versus streaming execution](https://rkozlowski.github.io/TigerQuery/engine.html#prepared-versus-streaming-execution)
for the trade-offs.

---

## 🧪 Tests = Documentation

TigerQuery uses a structured unit test suite to document:

- 🔍 Known compatibility issues  
- 💡 Intentional differences from `sqlcmd` or SSMS  
- 🧪 Parser edge cases, whitespace behavior, comment handling  
- 🧠 Design decisions that prioritize clarity over legacy quirks

See:

- [`SqlCmdParserKnownIssues`](ItTiger.TigerQuery.Tests/Parser/SqlCmdParserKnownIssues.cs)  
- [`SqlCmdParserIntentionalDifferences`](ItTiger.TigerQuery.Tests/Parser/SqlCmdParserIntentionalDifferences.cs)

---

## 🚀 Quickstart with tiger-sqlcmd

Normal execution is semi-interactive and may prompt for eligible missing input. Add
`--non-interactive` to the same command for unattended use; menus, prompts,
confirmations, and keyboard input are disabled, while validation, execution, output,
diagnostics, and exit-code mapping remain active. See
[One Command Model, Multiple Interaction Modes](docs/api-docfx/tiger-sqlcmd.md#one-command-model-multiple-interaction-modes).

```bash
tiger-sqlcmd run -c local -m sqlcmdex -f script.sql
```

Here, `local` is a saved connection managed with `tiger-sqlcmd connection`.
The `run` command supports `-v name=value` for variables, `--verbosity`,
`--log-level`, and more. Route result sets to TigerQuery's built-in CSV writer
with `-o`/`--output`:

```bash
tiger-sqlcmd run -c local -f script.sql -o results.csv
```

Use `--output-mode FilePerResultSet` for generated per-result-set files,
`-e`/`--error-output` for SQL errors, and `--out-behavior` to choose whether
`:Out` also redirects normal messages. See the
[result output routing feature guide](docs/features/result-output-routing.md) for the
complete CLI, API, CSV, overwrite, naming, encoding, and partial-file contract.

### Choosing a connection store

Saved connections live in a per-user JSON store shared across Tiger tools. A
single run can point somewhere else — a scratch store, a CI workspace, a
container mount — without touching that default:

```bash
tiger-sqlcmd connection list --tq-connection-store-file /tmp/scratch.json
export TIGERQUERY_CONNECTION_STORE_FILE=/workspace/runtime/connections.json
```

The command-line option wins over the environment variable, which wins over the
application default. A store path that is supplied but unusable fails the run
rather than quietly falling back, so a mistyped CI path can never send a job to
a developer's personal store. The option is app-wide in meaning but still an
option, so write it after the command path and any positionals; use
`--tq-connection-store-file=<path>` when the path begins with `-`. See
[selecting the connection store](docs/api-docfx/connection-profiles.md#selecting-one-store).

### Session-scoped E2E resources

After configuring the authorized `tiger-sqlcmd-e2e` bootstrap, create a database
and its paired owning connection with one session correlation ID:

```bash
tiger-sqlcmd e2e create --session-id 11111111-2222-3333-4444-555555555555 --name-part smoke --non-interactive
tiger-sqlcmd e2e cleanup --session-id 11111111-2222-3333-4444-555555555555 --non-interactive
```

To target a pre-existing database without granting TigerQuery permission to drop it:

```bash
tiger-sqlcmd connection clone-e2e source --database ExistingDb \
  --session-id 11111111-2222-3333-4444-555555555555 --name-part readonly \
  --non-interactive
```

See [TigerSqlCmd E2E scenarios](docs/api-docfx/tiger-sqlcmd-e2e.md) for the complete
bootstrap, session, CI/agent, clone, cleanup, and recovery workflows. The
[E2E connection-store architecture](docs/features/e2e-connection-stores.md) remains the
underlying safety contract.

---

## 📦 Installation

### NuGet packages

| Package | Purpose |
|---|---|
| [`ItTiger.TigerQuery`](https://www.nuget.org/packages/ItTiger.TigerQuery/) | Standalone sqlcmd-compatible SQL script parser and execution engine |
| [`ItTiger.TigerQuery.Core`](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/) | Saved SQL Server connection profiles: storage, validation, resolution, same-store copying |
| [`ItTiger.TigerQuery.CliCore`](https://www.nuget.org/packages/ItTiger.TigerQuery.CliCore/) | Reusable TigerCli connection-management commands for CLI apps |

```bash
dotnet add package ItTiger.TigerQuery
```
```bash
dotnet add package ItTiger.TigerQuery.Core
```
```bash
dotnet add package ItTiger.TigerQuery.CliCore
```

### tiger-sqlcmd CLI

Install the `tiger-sqlcmd` .NET tool globally:

```bash
dotnet tool install --global ItTiger.TigerSqlCmd
```

The tool targets .NET 10, so run installation with a .NET 10 SDK. An older SDK
can misleadingly report that `DotnetToolSettings.xml` is missing even though it
is present in the package; check `dotnet --version` and any applicable
`global.json` if that message appears.

For a repository-local installation, create or reuse a tool manifest and install
the same package locally:

```bash
dotnet new tool-manifest
dotnet tool install --local ItTiger.TigerSqlCmd
dotnet tool run tiger-sqlcmd --help
```

Update or remove the global tool with:

```bash
dotnet tool update --global ItTiger.TigerSqlCmd
dotnet tool uninstall --global ItTiger.TigerSqlCmd
```

For a local manifest, replace `--global` with `--local`. A machine-wide Windows
installer is also distributed through
[GitHub releases](https://github.com/rkozlowski/TigerQuery/releases). It requires
administrator elevation, installs under Program Files, and adds TigerSqlCmd to the system
PATH. See [TigerSqlCmd concepts and usage](docs/api-docfx/tiger-sqlcmd.md) for all
installation options and the command reference.

---

## 🔧 Status

TigerQuery v0.8.3 is a **snapshot release** — not issue-free, but stable, tested, and ready to use.

It is meant as a transparent, inspectable tool — bugs and all.  
The test suite tracks known issues, documents differences, and protects your upgrade path.

---

## 🛡️ Copyright & Project Sponsor

<p align="left">
  <img src="docs/assets/ItTiger-head.png" alt="IT Tiger Logo" width="120"/>
</p>

TigerQuery is an open-source project by **IT Tiger**  
🔗 Project page: https://www.ittiger.net/projects/tigerquery/  
🔗 https://www.ittiger.net/
