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

## Streaming and prepared execution

`TigerQueryExecutionMode.Streaming` is the default. It matches the traditional
sqlcmd-like incremental flow: TigerQuery parses one logical batch, executes it,
and then continues parsing. This minimizes retained script text, but a malformed
TigerQuery/sqlcmd directive late in the script can be discovered after earlier
batches have executed.

Set `ExecutionMode = TigerQueryExecutionMode.Prepared` to parse the complete
TigerQuery/sqlcmd structure before the SQL connection is opened:

```csharp
var options = new TigerQueryEngineOptions
{
    ConnectionString = connectionString,
    ExecutionMode = TigerQueryExecutionMode.Prepared,
    OnExecutionPlanReady = plan => Console.WriteLine(
        $"{plan.LogicalBatchCount} logical batch(es), "
        + $"{plan.TotalExecutionCount} scheduled execution(s)")
};
```

After successful preparation, `OnExecutionPlanReady` fires once before
connection opening and before any batch callbacks. Its execution total includes
positive `GO n` repeat counts. Batches with `GO 0` or a negative repeat count
still count as logical batches but contribute zero scheduled executions. These
counts describe batch scheduling only; they are not a percentage and do not
estimate work within a SQL batch.

Prepared mode prevents SQL execution when full parsing finds a TigerQuery/sqlcmd
structure error. It does not parse or validate T-SQL. Connection, permission,
T-SQL syntax and compilation, and runtime failures still occur during execution.
Parser exceptions retain their existing behavior and escape the run call.

Prepared mode retains every expanded logical batch until execution finishes, so
its memory use grows with the complete expanded script. `GO n` does not duplicate
the SQL text in memory. Prefer streaming mode for very large scripts when
full-script validation and totals are not required.

## How output is delivered

The engine never writes to the console. Everything flows through the callbacks on `TigerQueryEngineOptions`:

- `OnMessage` — `PRINT`, `RAISERROR`, info messages, and errors as `SqlCmdMessage` (severity, type, line number)
- `OnExecutionPlanReady` — prepared-mode logical batch and scheduled execution totals
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
