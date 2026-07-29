# TigerQuery engine

The [ItTiger.TigerQuery](https://www.nuget.org/packages/ItTiger.TigerQuery/)
package parses SQL scripts and coordinates asynchronous execution against SQL
Server. It recognizes `GO` separators and repeat counts, sqlcmd variables and
`:setvar`, `:on error`, and plain, `SqlCmd`, and extended `SqlCmdEx` modes.

The main composition types are
[TigerQueryEngine](xref:ItTiger.TigerQuery.Engine.TigerQueryEngine) and
[TigerQueryEngineOptions](xref:ItTiger.TigerQuery.Engine.TigerQueryEngineOptions).
Lower-level parsing is available through
[SqlCmdParser](xref:ItTiger.TigerQuery.SqlCmdParser).

## SqlCmd and SqlCmdEx

`SqlCmdEx` is TigerQuery's extended scripting mode for applications and
automation. It keeps familiar sqlcmd script syntax while allowing the host
application to provide protected variables that scripts cannot override with
`:setvar`.

Select the mode with
[TigerQueryEngineOptions.Mode](xref:ItTiger.TigerQuery.Engine.TigerQueryEngineOptions.Mode):

| Mode | Application-provided variables | Script-local variables | Typical use |
| --- | --- | --- | --- |
| `SqlCmd` | Seed the variable table; a matching `:setvar` replaces the host value | Created and updated by `:setvar` | Conventional sqlcmd-style scripts |
| `SqlCmdEx` | Take precedence; a matching `:setvar` is ignored | Created and updated when the name does not conflict with a protected host value | Embedded applications, automation, and controlled workflows |

Variables supplied through
[TigerQueryEngineOptions.Variables](xref:ItTiger.TigerQuery.Engine.TigerQueryEngineOptions.Variables)
are loaded before parsing and matched case-insensitively. In `SqlCmd`, they are
ordinary initial values. In `SqlCmdEx`, each host-provided value is protected
for the run. A script can still create its own variables with `:setvar`, and
later `:setvar` commands can update those script-local values.

For example, the script attempts to choose its own target:

```sql
:setvar TargetDatabase ScriptDatabase
PRINT 'Deploying $(TargetDatabase)';
GO
```

The application retains control by supplying `TargetDatabase` in `SqlCmdEx`:

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

Practical uses include deployment automation, test orchestration, code
generation, database provisioning, and applications that inject environment
or project values into reusable scripts. Protection prevents a script from
accidentally changing an application-owned target or workflow value.

`SqlCmdEx` and prepared execution solve separate problems. `SqlCmdEx` controls
variable precedence; [prepared versus streaming execution](#prepared-versus-streaming-execution)
controls when parsing occurs relative to connection opening and batch
execution. Either execution mode can be combined with `SqlCmd` or `SqlCmdEx`.

## Prepared versus streaming execution

> [!IMPORTANT]
> Choose the execution mode according to when the complete script structure
> must be known—not according to SQL validation. Neither mode parses or
> validates T-SQL.

| | Streaming | Prepared |
| --- | --- | --- |
| Configuration | Default | `ExecutionMode = TigerQueryExecutionMode.Prepared` |
| Parsing | One logical batch at a time | Entire TigerQuery/sqlcmd structure first |
| SQL connection | Opened as incremental execution begins | Opened only after successful preparation |
| Late directive errors | May occur after earlier batches executed | Prevent any SQL execution |
| Memory | Retains little script text | Retains every expanded logical batch |
| Plan totals | Not available up front | Reported once through `OnExecutionPlanReady` |
| Best fit | Very large scripts and sqlcmd-like incremental flow | Full-script structural validation and known scheduling totals |

Streaming execution minimizes retained text, but a malformed TigerQuery/sqlcmd
directive late in the script can be discovered after earlier batches execute.

Prepared execution parses the complete structure first:

```csharp
using ItTiger.TigerQuery.Engine;

var options = new TigerQueryEngineOptions
{
    ConnectionString = connectionString,
    ExecutionMode = TigerQueryExecutionMode.Prepared,
    OnExecutionPlanReady = plan => Console.WriteLine(
        $"{plan.LogicalBatchCount} logical batch(es), "
        + $"{plan.TotalExecutionCount} scheduled execution(s)")
};
```

After preparation succeeds, `OnExecutionPlanReady` fires once, before the
connection opens and before batch callbacks. Its execution total includes
positive `GO n` repeat counts. `GO 0` and negative repeat counts still count as
logical batches but schedule no executions.

Prepared mode does not prevalidate the connection, permissions, T-SQL syntax,
compilation, or runtime behavior. Those failures still occur during execution.
It retains each expanded logical batch until execution finishes, although
`GO n` does not duplicate SQL text in memory.

See [TigerQueryExecutionMode](xref:ItTiger.TigerQuery.Engine.TigerQueryExecutionMode)
and [ExecutionPlanReady](xref:ItTiger.TigerQuery.Events.ExecutionPlanReady) for
the precise API contracts.

## Output and results

The engine reports work through callbacks:

- `OnMessage` receives `PRINT`, `RAISERROR`, informational messages, and errors.
- `OnBatchStart` and `OnBatchEnd` report batch progress and duration.
- `OnResultSet` receives column metadata and rows.
- `OnExecutionPlanReady` reports prepared-mode scheduling totals.

A run that reaches the execution coordinator's result path returns an
[ExecutionResult](xref:ItTiger.TigerQuery.Engine.ExecutionResult). Parser and
connection-opening failures can escape the run method instead of being
normalized into an execution result. An optional
`Microsoft.Extensions.Logging.ILogger` receives structured logs.

## Parsing modes

[SqlCmdMode](xref:ItTiger.TigerQuery.SqlCmdMode) selects normal SQL parsing,
ordinary TigerQuery sqlcmd-style behavior, or TigerQuery's extended `SqlCmdEx`
behavior.
Parsed batches include one-based source positions, batch text, and repeat
metadata for tooling that needs lower-level control.
