# ItTiger.TigerQuery

**TigerQuery** is a standalone SQL Server script parser and execution engine for .NET with familiar `sqlcmd` / SSMS SqlCmd-mode behavior — a deliberate, test-driven reimplementation that is compatible where it matters and safer where it should be.

It is a library: it renders nothing and owns no console. Embed it in your own tools, services, or CLIs. (The ready-made `tiger-sqlcmd` CLI is built on it — see below.)

## Capabilities

- `GO` batch separators, including repeat counts (`GO 5`)
- sqlcmd variables (`$(name)`) and `:setvar`
- `:on error` handling
- `:Out` and `:Error` output routing with built-in RFC 4180 CSV files, single-file or
  one-file-per-result-set naming, and strict UTF-8 output (SQL `NULL` and the empty
  string are indistinguishable in CSV)
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

Use `RunFromFileAsync(path)` for script files and `RunAsync(TextReader)` for anything else. All run methods accept a `CancellationToken`. Cancellation raised during an active SQL batch maps to `ExecutionResultCode.UserCancelled`; cancellation during parsing, preparation, connection opening, or between executions propagates as `OperationCanceledException`.

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

Runs that reach the execution coordinator's result path return an `ExecutionResult` with an `ExecutionResultCode`, successful/failed execution counts (including `GO n` iterations), and total execution duration. Parser and connection-opening failures currently escape the run method rather than being normalized into a result. An optional `Microsoft.Extensions.Logging.ILogger` receives structured logs.

## `:on error` semantics

A batch attempt fails when SQL Server reports an error of severity 11 or higher for it — including the severity 11-16 errors the provider delivers as informational messages instead of throwing, which is what `RAISERROR(..., 16, ...)`, `THROW`, and ordinary compilation errors such as an invalid object name produce. A batch that returns normally after such an error is not counted as successful.

The effective policy starts at `ContinueOnError` and is updated by `:ON ERROR EXIT` and `:ON ERROR IGNORE` while parsing:

| | Effective exit-on-error | Effective continue-on-error |
| --- | --- | --- |
| Triggering batch | `BatchEnd.Success = false` | `BatchEnd.Success = false` |
| `FailedBatches` | incremented | incremented |
| `ExecutedBatches` | not incremented for the failing attempt | not incremented for the failing attempt |
| Later batches | not started; no `BatchStart`/`BatchEnd` for them | the next scheduled batch runs |
| `ResultCode` | `BatchFailed`, or `Fatal` for a fatal server error | `Success` |

`GO n` iterations follow the same rule individually, and a fatal server error stops the run under either policy.

For compatibility, an ignored failure keeps `ResultCode` at `Success` while `FailedBatches` is greater than zero, so a caller that tolerates no failed batch should check `FailedBatches` as well. Exit-on-error never returns `Success`.

Every diagnostic reaches `OnMessage` with its original number, severity, state, procedure, and line, and a diagnostic delivered both as a message and on a thrown exception is raised once. A batch that fails without a thrown exception carries a `SqlBatchErrorException` on `BatchEnd.Exception` and `ExecutionResult.Exception`, whose `Errors` collection holds those diagnostics.

Prepared and streaming execution share one coordinator, so the policy, lifecycle events, and counts are identical in both modes.

## Related packages

- [ItTiger.TigerQuery.Core](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/) — saved SQL Server connection profiles (storage, validation, resolution). Independent of this package; combine them when you want named connections in front of the engine.
- [ItTiger.TigerQuery.CliCore](https://www.nuget.org/packages/ItTiger.TigerQuery.CliCore/) — ready-made TigerCli connection-management commands for CLI applications.
- [tiger-sqlcmd](https://github.com/rkozlowski/TigerQuery/releases) — the ready-made CLI built on all three, distributed as GitHub release binaries.

## Links

- Project page: https://www.ittiger.net/projects/tigerquery/
- Repository: https://github.com/rkozlowski/TigerQuery
- License: [MIT](https://github.com/rkozlowski/TigerQuery/blob/main/LICENSE)

An open-source project by **IT Tiger** — https://www.ittiger.net/
