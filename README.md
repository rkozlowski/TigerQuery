# TigerQuery

<img src="docs/assets/TigerQuery256.png" alt="TigerQuery Logo" width="80"/>

**TigerQuery** is a lightweight SQL script engine and parser with familiar
`sqlcmd` and SSMS SqlCmd-mode scripting, plus **`SqlCmdEx`**: an extended mode
for applications and automation that protects host-provided variables from
script-level `:setvar` overrides.

It powers `tiger-sqlcmd`, a modern CLI for executing complex `.sql` scripts with repeatable batches, variable injection, and advanced scripting features.

## Documentation

**[Read the published TigerQuery documentation](https://rkozlowski.github.io/TigerQuery/).**

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

- ✅ Familiar `:setvar`, `$(var)`, `:on error`, and `GO [n]` handling
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

```bash
tiger-sqlcmd run -c local -m sqlcmdex -f script.sql
```

Here, `local` is a saved connection managed with `tiger-sqlcmd connections`.
The `run` command supports `-v name=value` for variables, `--verbosity`,
`--log-level`, and more.

---

## 📦 Installation

### NuGet packages

| Package | Purpose |
|---|---|
| [`ItTiger.TigerQuery`](https://www.nuget.org/packages/ItTiger.TigerQuery/) | Standalone sqlcmd-compatible SQL script parser and execution engine |
| [`ItTiger.TigerQuery.Core`](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/) | Saved SQL Server connection profiles: storage, validation, resolution |
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

The `tiger-sqlcmd` command-line tool is distributed as prebuilt binaries via
[GitHub releases](https://github.com/rkozlowski/TigerQuery/releases), or build it from source.

---

## 🔧 Status

TigerQuery v0.8.2 is a **snapshot release** — not issue-free, but stable, tested, and ready to use.

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
