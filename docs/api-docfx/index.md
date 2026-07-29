# TigerQuery documentation

<img src="../assets/TigerQuery256.png" alt="TigerQuery logo" width="96">

TigerQuery is a lightweight SQL script engine and parser with familiar
`sqlcmd` and SSMS SqlCmd-mode behavior. Its defining **`SqlCmdEx`** mode adds
protected application-provided variables for automation and embedded tooling.
Companion packages add named SQL Server connection profiles and reusable
connection-management commands for TigerCli applications.

This site combines a short guide with API documentation generated from the
libraries' C# source and XML documentation comments.

> [!TIP]
> **`SqlCmdEx` is controlled scripting for applications.** It keeps familiar
> sqlcmd syntax, but a script cannot replace host-provided variables with
> `:setvar`. Non-conflicting script-local variables continue to work. See
> [SqlCmd and SqlCmdEx](engine.md#sqlcmd-and-sqlcmdex) for the comparison and
> a complete example.

> [!IMPORTANT]
> TigerQuery supports two execution modes. **Streaming** execution is the
> memory-efficient default and may discover a late directive error after
> earlier batches have run. **Prepared** execution parses the entire
> TigerQuery/sqlcmd structure before opening the connection or executing SQL.
> See [Prepared versus streaming execution](engine.md#prepared-versus-streaming-execution)
> before choosing a mode.

## Packages

| Package | Use it for |
| --- | --- |
| [ItTiger.TigerQuery](https://www.nuget.org/packages/ItTiger.TigerQuery/) | Parsing and executing SQL scripts |
| [ItTiger.TigerQuery.Core](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/) | Saving, validating, and resolving SQL Server connection profiles |
| [ItTiger.TigerQuery.CliCore](https://www.nuget.org/packages/ItTiger.TigerQuery.CliCore/) | Adding connection-management commands to a TigerCli application |

Start with [Getting started](getting-started.md), or browse the
[API reference](api-reference.md).

## Project links

- [GitHub repository](https://github.com/rkozlowski/TigerQuery)
- [TigerQuery project page](https://www.ittiger.net/projects/tigerquery/)
- [MIT license](https://github.com/rkozlowski/TigerQuery/blob/main/LICENSE)
