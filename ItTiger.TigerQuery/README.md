# ItTiger.TigerQuery

**TigerQuery** is a standalone SQL Server script parser and execution engine for .NET with familiar `sqlcmd` / SSMS SqlCmd-mode behavior — a deliberate, test-driven reimplementation that is compatible where it matters and safer where it should be.

It is a library: it renders nothing and owns no console. Embed it in your own tools, services, or CLIs. (The ready-made `tiger-sqlcmd` CLI is built on it — see below.)

## Capabilities

- `GO` batch separators, including repeat counts (`GO 5`)
- sqlcmd variables (`$(name)`) and `:setvar`
- `:on error` handling
- Plain, `sqlcmd`, and extended `sqlcmdex` parsing modes
- Fully asynchronous parsing and execution, from strings or files, with cancellation support
- Exact line/column metadata per batch
- Structured execution events (messages, batch start/end, result sets) and a typed execution result

## Installation

```
dotnet add package ItTiger.TigerQuery
```

## Quick start

```csharp
using ItTiger.TigerQuery;
using ItTiger.TigerQuery.Engine;

var options = new TigerQueryEngineOptions
{
    ConnectionString = "Server=localhost;Database=master;Integrated Security=true",
    Mode = SqlCmdMode.SqlCmd,
    Variables = new Dictionary<string, string> { ["Env"] = "Dev" },
    OnMessage = (message, isException) => Console.WriteLine(message.Text),
    OnBatchEnd = end => Console.WriteLine(
        $"Batch {end.BatchNumber}: {(end.Success ? "ok" : "failed")} in {end.Duration.TotalMilliseconds:F0} ms"),
    OnResultSet = resultSet => Console.WriteLine(
        $"{resultSet.Rows.Count} row(s), {resultSet.Columns.Count} column(s)")
};

var engine = new TigerQueryEngine(options);

var result = await engine.RunFromStringAsync(
    """
    :setvar Greeting Hello
    PRINT '$(Greeting) from $(Env)';
    GO 2
    SELECT name FROM sys.databases;
    GO
    """);

Console.WriteLine($"{result.ResultCode}: {result.ExecutedBatches} batch(es) in {result.TotalDuration.TotalMilliseconds:F0} ms");
```

Use `RunFromFileAsync(path)` for script files and `RunAsync(TextReader)` for anything else. All run methods accept a `CancellationToken`; cancellation maps to `ExecutionResultCode.UserCancelled`.

## How output is delivered

The engine never writes to the console. Everything flows through the callbacks on `TigerQueryEngineOptions`:

- `OnMessage` — `PRINT`, `RAISERROR`, info messages, and errors as `SqlCmdMessage` (severity, type, line number)
- `OnBatchStart` / `OnBatchEnd` — batch progress, success, and duration
- `OnResultSet` — column metadata (`ColumnInfo`) and rows (`object?[]`)

Each run returns an `ExecutionResult` with an `ExecutionResultCode` (success, batch failure, fatal error, cancellation, connection failure, parse error, …), executed/failed batch counts, and total duration. An optional `Microsoft.Extensions.Logging.ILogger` receives structured logs.

## Related packages

- [ItTiger.TigerQuery.Core](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/) — saved SQL Server connection profiles (storage, validation, resolution). Independent of this package; combine them when you want named connections in front of the engine.
- [ItTiger.TigerQuery.CliCore](https://www.nuget.org/packages/ItTiger.TigerQuery.CliCore/) — ready-made TigerCli connection-management commands for CLI applications.
- [tiger-sqlcmd](https://github.com/rkozlowski/TigerQuery/releases) — the ready-made CLI built on all three, distributed as GitHub release binaries.

## Links

- Project page: https://www.ittiger.net/projects/tigerquery/
- Repository: https://github.com/rkozlowski/TigerQuery
- License: [MIT](https://github.com/rkozlowski/TigerQuery/blob/main/LICENSE)

An open-source project by **IT Tiger** — https://www.ittiger.net/
