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

### Bounded batch progress

In prepared mode, `BatchStart` and `BatchEnd` include the same
`TotalLogicalBatchCount` and `TotalExecutionCount` values reported by
`OnExecutionPlanReady`. `OverallExecutionNumber` advances from one as each
execution attempt begins, so an end callback can report bounded progress:

```csharp
var options = new TigerQueryEngineOptions
{
    ExecutionMode = TigerQueryExecutionMode.Prepared,
    OnBatchEnd = batch =>
    {
        if (batch.TotalExecutionCount is long total)
        {
            var percent = 100d * batch.OverallExecutionNumber / total;
            Console.WriteLine(
                $"{batch.OverallExecutionNumber}/{total} attempts ended "
                + $"({percent:F0}%)");
        }
    }
};
```

These totals continue to describe the complete prepared plan if execution
stops early. In streaming mode they are `null` because the remaining script is
not known; `OverallExecutionNumber` is still populated. The values measure
scheduled batch execution attempts, not progress within a SQL batch.

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

The message and result-set callbacks are the default destinations. They can be
redirected to files by `OutputRouting`, `:Out`, and `:Error`, as described in
[Output routing and CSV files](#output-routing-and-csv-files).

A run that reaches the execution coordinator's result path returns an
[ExecutionResult](xref:ItTiger.TigerQuery.Engine.ExecutionResult). Parser and
connection-opening failures can escape the run method instead of being
normalized into an execution result. An optional
`Microsoft.Extensions.Logging.ILogger` receives structured logs.

## `:on error` semantics

A batch attempt **fails** when SQL Server reports an error of severity 11 or
higher for it. That includes errors the provider delivers as informational
messages rather than by throwing, which is how severity 11-16 arrives — the
range that `RAISERROR(..., 16, ...)`, `THROW`, and ordinary compilation errors
such as an invalid object name fall into. A batch that returns normally after
such an error is not a successful batch.

The effective policy comes from
[ContinueOnError](xref:ItTiger.TigerQuery.Engine.TigerQueryEngineOptions.ContinueOnError)
and is updated by `:ON ERROR EXIT` and `:ON ERROR IGNORE` as the script is
parsed.

| | Effective exit-on-error | Effective continue-on-error |
| --- | --- | --- |
| Triggering batch | `BatchEnd.Success = false` | `BatchEnd.Success = false` |
| `FailedBatches` | Incremented | Incremented |
| `ExecutedBatches` | Not incremented for the failing attempt | Not incremented for the failing attempt |
| Later batches | Not started — no `BatchStart` or `BatchEnd` is raised for them | The next scheduled batch runs |
| `ResultCode` | `BatchFailed`, or `Fatal` for a fatal server error | `BatchFailed`, or `Fatal` for a fatal server error |

`GO n` repeat counts follow the same rule per iteration: under exit-on-error a
failing iteration stops the remaining iterations, under continue-on-error they
proceed. A fatal server error stops the run under either policy.

The policy decides how much of the script runs; it does not decide what the run
reports. `ResultCode` is `Success` only when `FailedBatches` is zero. Continuing
past a failed batch still ends the run as `BatchFailed`, and a later successful
batch never clears an earlier failure — reaching the end of a script is not
evidence that it worked.

Diagnostics reach `OnMessage` in server order with their original number,
severity, state, procedure, and line, and a diagnostic delivered both as a
message and on a thrown exception is raised once. When a batch fails without a
thrown exception, `BatchEnd.Exception` and `ExecutionResult.Exception` carry a
[SqlBatchErrorException](xref:ItTiger.TigerQuery.SqlBatchErrorException) whose
`Errors` collection holds those same diagnostics.

Prepared and streaming execution share one coordinator, so this behavior,
the batch lifecycle, and the counts are identical in both modes.

## `:Out` and `:Error` directives

In `SqlCmd` and `SqlCmdEx` modes the parser recognizes and validates the
`:Out` and `:Error` output directives, which previously failed as unknown colon
commands. Each takes exactly one non-empty filename, either bare or in the
sqlcmd double-quoted form (a doubled quote represents a quote), optionally
followed by a single-line comment. `$(name)` references in the filename are
expanded at the directive's source position, and undefined references stay
literal. In `SqlCmdMode.Normal` the text is still sent to SQL Server unchanged.

Internally the engine consumes an ordered step stream in which each directive
keeps its source position relative to the batches around it, so repeated routes
to the same path and a directive placed between buffered SQL and its terminating
`GO` are preserved rather than collapsed into final parser state. That stream is
an implementation detail; there is no public script-step API.
[SqlCmdParser.ReadBatchesAsync](xref:ItTiger.TigerQuery.SqlCmdParser.ReadBatchesAsync*)
remains batch-only and unchanged for direct consumers: it validates the
directives and then projects them away. Applications that need routing must
execute through
[TigerQueryEngine](xref:ItTiger.TigerQuery.Engine.TigerQueryEngine).

A host can refuse script-directed routing entirely with
`OutputRouting.AllowScriptOutputDirectives = false`, which turns an encountered
directive into a parser error rather than silently ignoring a script command.
Initial paths supplied by the host still apply.

## Output routing and CSV files

[OutputRoutingOptions](xref:ItTiger.TigerQuery.Engine.OutputRoutingOptions)
configures where output goes. Routing is entirely opt-in: with no initial path
and no directive, result sets and messages reach `OnResultSet` and `OnMessage`
exactly as before and no file is created.

TigerQuery models three routable channels:

| Channel | Payload | Application destination | File destination |
| --- | --- | --- | --- |
| Result sets | `ResultSetInfo` | `OnResultSet` | Built-in CSV |
| Normal messages | Severity 0-10, including `PRINT` | `OnMessage` | Plain UTF-8 text |
| Error messages | SQL Server diagnostics with `IsError` | `OnMessage` | Plain UTF-8 text |

Redirecting a channel **replaces** its callback; it does not mirror to both.
Batch lifecycle, plan readiness, and progress are never redirected, and the
configured `ILogger` keeps receiving every message whatever the routes are.

Only SQL Server diagnostics can enter a routed message file. Parser failures,
connection failures, configuration and encoding failures, output failures, and
unrelated application exceptions are surfaced through `OnMessage`, the logger,
and `ExecutionResult` — never written to an `:Error` file — so the file cannot
accumulate connection strings or unstable framework text.

Precedence is: callbacks by default, then `InitialOutPath`/`InitialErrorPath` at
run start, then each script directive from its position onward, latest wins.
`:Out` never changes the error route, and `:Error` never changes the result-set
or normal-message routes.

[OutDirectiveBehavior](xref:ItTiger.TigerQuery.Engine.OutDirectiveBehavior)
selects what `:Out` moves. Under `ResultSetsAndNormalMessages`, result sets use
the requested path and normal messages use a companion file named by appending
`.messages.log` to the complete resolved result path, because CSV cannot safely
hold both rows and arbitrary prose.

### Files and naming

Relative paths resolve against `OutputRouting.BaseDirectory`, captured once at
run start; without it the process's current directory is captured. The same rule
applies to `RunFromFileAsync`, so a host that wants script-relative paths must
pass the script directory explicitly.

The parent directory must already exist — TigerQuery never creates directories.
Files are created lazily on the first payload, truncated on first use in a run,
never appended across runs, and kept open until the run ends so a script can
leave a path and return to it without a second byte-order mark or header. Every
destination is flushed and closed on success, failure, and cancellation alike.

A resolved path belongs to exactly one channel for the whole run. Pointing two
channels at the same file is a configuration error, not permission to mix
payloads.

[ResultSetFileMode](xref:ItTiger.TigerQuery.Engine.ResultSetFileMode) chooses the
file layout. `SingleFile` uses the requested path exactly as supplied — no
extension is inferred, so `:Out report` writes a CSV file named `report`.
`FilePerResultSet` treats the path as a base name and generates
`<stem>_b<batch>_e<execution>_r<result><extension>` from the engine's stable
coordinates, each component one-based, invariant, and padded to at least four
digits and never truncated. `:Out report.csv` therefore gives
`report_b0001_e0001_r0001.csv`. Coordinates are the original engine coordinates,
so route changes and skipped zero-column results never renumber later files, and
names are identical in streaming and prepared mode. An `:Error` file never
receives a result-set suffix.

### CSV behavior

Version one is fixed: UTF-8 with a byte-order mark, comma delimiter, CRLF
records, a header, minimal RFC 4180 quoting, and invariant formatting. A field is
quoted when it contains a comma, a double quote, CR, or LF, and inner quotes are
doubled; header names use the same rules as data.

`DateTime` and `DateTimeOffset` use ISO 8601 round-trip form, `TimeSpan` the
constant `c` form, `Guid` the `D` form, `byte[]` uppercase hex with a `0x`
prefix, floating-point values a round-trip form, and other `IFormattable` values
invariant culture.

> [!IMPORTANT]
> SQL `NULL` and the empty string both produce an empty field and are therefore
> indistinguishable in version one.

A zero-column result is not a CSV result set: it writes nothing and creates no
file, though its coordinate is still consumed. A result with columns and no rows
writes its header.

In `SingleFile` mode the first result set establishes the header and the required
schema. A later result set is compatible when it has the same column count and
the same column names in the same ordinal positions, compared ordinally; it then
appends rows only and its header is never rewritten. Differing SQL or CLR types
are fine, because CSV has no type schema. An incompatible result set is validated
before any of its bytes are written, so the file keeps only complete earlier
content.

The encoding is always configured with exception fallbacks, so an unencodable
character fails the run instead of producing a replacement character. Supplying
`FileEncoding` keeps that encoding's byte-order-mark preference but strengthens
its fallbacks; interoperability then depends on that encoding.

### Output failures

Path, permission, sharing, directory, encoding, schema, flush, and close failures
become [OutputRoutingException](xref:ItTiger.TigerQuery.OutputRoutingException),
which carries the target `Path` and derives from `TigerQueryException`.

An output failure is fatal regardless of `:ON ERROR IGNORE` and
`ContinueOnErrorForUnhandledExceptions`, because continuing would execute later
SQL while losing its requested output. During a batch it becomes the primary
exception on a failed `BatchEnd`, counts as a failed batch, stops the run
immediately, and produces
[ExecutionResultCode.OutputFailed](xref:ItTiger.TigerQuery.Engine.ExecutionResultCode)
with the same exception on the result. A route change that fails between batches
uses the same classification and stops before the next batch. SQL Server may
already have completed the command, and may have committed side effects, before
the failure was discovered; the batch is still counted as failed, and the log
says so when it is known.

Because version one writes directly to destination files, a failed or cancelled
run can leave valid partial CSV containing every result set completed before the
failure.

Prepared mode resolves routes and detects statically known collisions before
plan readiness and before the connection opens, so a prepared script that cannot
route creates no file at all. Streaming mode reaches the same failure when it
gets there, after any earlier batches and files — the established difference
between the two modes.

## Parsing modes

[SqlCmdMode](xref:ItTiger.TigerQuery.SqlCmdMode) selects normal SQL parsing,
ordinary TigerQuery sqlcmd-style behavior, or TigerQuery's extended `SqlCmdEx`
behavior.
Parsed batches include one-based source positions, batch text, and repeat
metadata for tooling that needs lower-level control.
