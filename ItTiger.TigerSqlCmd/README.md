# TigerSqlCmd

`tiger-sqlcmd` is a modern SQL Server CLI built on TigerQuery for SQL script
execution, saved connection profiles, interactive and non-interactive automation,
CI and agent workflows, and safe session-scoped E2E database lifecycles. It combines
TigerQuery's SQLCMD-compatible parser and execution engine with connection management,
output routing, stable exit codes, and external-tool integration.

## Installation

### Windows with WinGet

```console
winget install ItTiger.TigerSqlCmd
```

WinGet installs TigerSqlCmd machine-wide and resolves its .NET 10 runtime dependency.
The package is published in the Windows Package Manager community repository as
`ItTiger.TigerSqlCmd`.

### .NET tool

```bash
dotnet tool install --global ItTiger.TigerSqlCmd
```

Use a .NET 10 SDK to install the .NET tool. Older SDKs can misleadingly report that
`DotnetToolSettings.xml` is missing even though the package contains it; check
`dotnet --version` and any applicable `global.json` if that happens.

## Quick start

```bash
tiger-sqlcmd connection list
tiger-sqlcmd connection add local --server sql01 --database AppDb --non-interactive
tiger-sqlcmd run --connection local --query "SELECT DB_NAME();" --non-interactive
tiger-sqlcmd run --connection local --file deploy.sql --non-interactive
```

The command group is `connection` (singular). Saved profiles can use integrated or
SQL authentication and can resolve sensitive or environment-specific values from
supported external references instead of storing them directly.

## One Command Model, Multiple Interaction Modes

Normal execution is semi-interactive and can prompt for eligible missing input. Add
`--non-interactive` to the same command for scripts, CI jobs, scheduled work, and
agents. Command semantics and safety checks stay the same; missing promptable values
fail clearly instead of blocking.

See [TigerSqlCmd interaction modes and general usage](https://rkozlowski.github.io/TigerQuery/tiger-sqlcmd.html#one-command-model-multiple-interaction-modes).

## E2E workflows

```bash
tiger-sqlcmd e2e create --session-id 11111111-2222-3333-4444-555555555555 --name-part smoke --non-interactive
tiger-sqlcmd run --connection E2E-smoke-<generated-suffix> --file tests.sql --non-interactive
tiger-sqlcmd e2e cleanup --session-id 11111111-2222-3333-4444-555555555555 --non-interactive
```

An owned E2E database and its saved connection are correlated to the session, and
cleanup applies exact ownership and authorization checks. A `connection clone-e2e`
profile for a pre-existing database is non-owning, so cleanup removes only the saved
connection and never drops that database. Add `--memory-optimized` to `e2e create`
when the database must host memory-optimized tables.

See the [complete TigerSqlCmd E2E guide](https://rkozlowski.github.io/TigerQuery/tiger-sqlcmd-e2e.html).

## External tools

```bash
tiger-sqlcmd exec --connection local --connection-string-env DB_CONNECTION -- my-tool --report
```

`exec` resolves the saved connection and passes it to one external process through
the supported environment-variable and/or `{connection-string}` argument handoff.
Every TigerSqlCmd option goes before the raw `--` separator; everything after it is
the child command line.

See [Running an external tool: `exec`](https://rkozlowski.github.io/TigerQuery/tiger-sqlcmd.html#running-an-external-tool-exec).

## Documentation

- [TigerSqlCmd general guide](https://rkozlowski.github.io/TigerQuery/tiger-sqlcmd.html)
- [TigerSqlCmd E2E guide](https://rkozlowski.github.io/TigerQuery/tiger-sqlcmd-e2e.html)
- [TigerQuery documentation home](https://rkozlowski.github.io/TigerQuery/)
- [TigerQuery repository](https://github.com/rkozlowski/TigerQuery)
