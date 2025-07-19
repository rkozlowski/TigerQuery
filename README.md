# TigerQuery

<img src="docs/assets/TigerQuery256.png" alt="TigerQuery Logo" width="80"/>

**TigerQuery** is a lightweight SQL script engine and parser built for compatibility with `sqlcmd` and SSMS SqlCmd mode — but with cleaner, safer behavior and a test-driven foundation.

It powers `tiger-sqlcmd`, a modern CLI for executing complex `.sql` scripts with repeatable batches, variable injection, and advanced scripting features.

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

- ✅ Compatible `:setvar`, `$(var)`, `:on error`, `GO [n]` handling
- ✅ Fully async parser and execution engine
- ✅ Tracks exact line/column metadata per batch
- ✅ Structured error handling via `TigerQueryException`
- ✅ Differentiates between `sqlcmd`, `sqlcmdex`, and normal modes
- ✅ Easily embeddable in CLI tools or .NET apps

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
tiger-sqlcmd -c "Server=.;Database=MyDb" -m sqlcmdex -f script.sql
```

Supports `-v name=value` for variables, `--verbosity`, `--log-level`, and more.

---

## 📦 Installation

TigerQuery is included with [`tiger-sqlcmd`](https://github.com/rkozlowski/TigerQuery/releases).  
Build from source or use the prebuilt binaries.

---

## 🔧 Status

TigerQuery v0.8.0 is a **snapshot release** — not issue-free, but stable, tested, and ready to use.

It is meant as a transparent, inspectable tool — bugs and all.  
The test suite tracks known issues, documents differences, and protects your upgrade path.

---

## 🛡️ Copyright & Project Sponsor

<p align="left">
  <img src="docs/assets/ItTiger-head.png" alt="IT Tiger Logo" width="120"/>
</p>

TigerQuery is an open-source project by **IT Tiger**  
🔗 https://www.ittiger.net/
