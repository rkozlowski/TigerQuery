# Result output routing

TigerQuery result output routing lets a SQLCMD script or an embedding application send
result sets, normal SQL Server messages, and SQL Server error messages to deterministic
files without making the command-line application responsible for parsing directives,
serializing rows, or managing file lifetimes.

Routing is opt-in. With no initial output paths and no `:Out` or `:Error` directives,
TigerQuery keeps its normal behavior: result sets go to `OnResultSet`, messages go to
`OnMessage`, and `tiger-sqlcmd` renders them in the console. A routed channel is redirected,
not mirrored; its presentation callback is not invoked. Structured logging and batch
lifecycle/progress callbacks remain independent and are never redirected.

## Design and ownership

TigerQuery owns routing because directives are part of ordered SQLCMD execution, not a
terminal-rendering concern. The engine knows the exact point at which each directive takes
effect, the batch/execution/result-set coordinates, SQL message severity and origin, and
whether execution is streaming or prepared. Keeping those rules in the engine gives CLI
and library consumers the same behavior and prevents each host from implementing subtly
different parsing, naming, CSV, error, and cleanup rules.

The division of responsibility is:

- TigerQuery parses and orders `:Out` and `:Error`, tracks run-scoped routes, resolves and
  reserves paths, writes CSV and message files, and reports output failures.
- `tiger-sqlcmd` maps CLI options to `OutputRoutingOptions` and continues to own console
  rendering and numeric exit-code presentation.
- Library hosts configure routing through `TigerQueryEngineOptions.OutputRouting` and
  receive unrouted payloads through their callbacks.

## Ordered execution

TigerQuery represents executable SQLCMD input as ordered route and batch steps. The run
starts with application callbacks, then applies any `InitialOutPath` and
`InitialErrorPath`, then consumes script directives and batches in source order. The most
recent directive for a channel wins from its effective position onward:

```sql
:Out first.csv
SELECT 1 AS Id;
GO
:Out second.csv
SELECT 2 AS Id;
GO
:Out first.csv
SELECT 3 AS Id;
GO
```

This produces:

```text
first.csv   Id, then rows 1 and 3
second.csv  Id, then row 2
```

Returning to a path already used in the same run continues the existing destination. It
does not write a second byte-order mark or header. `:Out` never changes the error route;
`:Error` never changes result-set or normal-message routes.

A directive encountered after SQL text but before that text's terminating `GO` affects
the buffered batch because the batch has not executed yet:

```sql
SELECT 1 AS Id;
:Out selected.csv
GO
```

Both prepared and streaming execution use the same ordered steps and produce the same
route changes and stable result coordinates. The public batch-only
`SqlCmdParser.ReadBatchesAsync` API still returns `SqlBatch` values: it recognizes and
validates output directives but projects the route steps away. Consumers that need
routing must execute through `TigerQueryEngine`.

## Console and file routes

There are three presentation channels:

| Channel | Default destination | File route |
| --- | --- | --- |
| Result sets | `TigerQueryEngineOptions.OnResultSet` | `InitialOutPath` or `:Out` |
| Normal server diagnostics, severities 0–10 | `TigerQueryEngineOptions.OnMessage` | `:Out` only when `OutBehavior` is `ResultSetsAndNormalMessages` |
| Error server diagnostics, severities 11 and above | `TigerQueryEngineOptions.OnMessage` | `InitialErrorPath` or `:Error` |

Only SQL Server diagnostics are eligible for message files. Parser, connection,
configuration, cancellation, routing, and other engine exception text is not written to
`:Error` files. This avoids treating unstable exception strings or potentially sensitive
configuration text as script output. Redirected server messages still reach the
configured `ILogger`.

With `OutDirectiveBehavior.ResultSetsOnly`, the default, `:Out` changes only the result-set
route. With `ResultSetsAndNormalMessages`, result sets use the requested path and normal
messages use a separate text companion formed by appending `.messages.log` to the complete
resolved path:

```text
report.csv               result sets
report.csv.messages.log  normal SQL Server messages
```

The companion is lazy and is not created if no normal messages arrive. Errors remain
controlled independently by `:Error`.

## CSV output

`ResultSetOutputFormat.Csv` is the built-in structured format. It has a fixed,
portable contract:

- comma delimiter;
- CRLF record endings on every platform;
- one header row per destination schema;
- minimal RFC 4180-compatible quoting;
- doubled quotes inside quoted fields;
- invariant, culture-independent value formatting;
- `byte[]` values as uppercase `0x` hexadecimal;
- round-trip formats for date/time, floating-point, GUID, and duration values;
- SQL `NULL` as an empty field.

Headers use the same escaping rules as data. Fields containing commas, quotes, CR, or LF
are quoted, and embedded line endings are preserved. CSV files contain no banners,
separator records, row-count prose, or server messages.

SQL `NULL` and an empty string are intentionally indistinguishable in this format. If a
consumer must retain that distinction, use the engine result-set callback and a
consumer-owned format rather than routed CSV.

The requested filename does not select the format and is used exactly in
`SingleFile` mode. `:Out report` writes CSV to a file named `report`; TigerQuery does not
add `.csv` merely because CSV is selected.

## File modes and deterministic names

`ResultSetFileMode.SingleFile` writes every result set routed to a path into that file.
The first non-zero-column result set writes the header and establishes the schema. Later
result sets must have the same column count and the same column names in the same ordinal
positions, compared ordinally. Compatible rows append without another header; differing
CLR/SQL value types are allowed. An incompatible result set fails before any bytes from
that result set are written, although earlier output remains in the file.

`ResultSetFileMode.FilePerResultSet` treats the route as a base name and generates one
file for each result set:

```text
<stem>_b<batch>_e<execution>_r<result><extension>
```

Coordinates are one-based, invariant, and padded to at least four digits. For example,
the second result set in the first execution of batch 1 routed from `report.csv` becomes:

```text
report_b0001_e0001_r0002.csv
```

If the base path has no extension, the selected format's `.csv` extension is added to
generated files. Each file has its own header and there is no cross-file schema
restriction. Coordinates are the engine's original logical coordinates; route changes,
`GO` repetitions, and skipped zero-column results do not renumber later files. A
zero-column routed result produces no file and no result callback.

## Paths, creation, encoding, and overwrite behavior

Relative paths are resolved against `OutputRoutingOptions.BaseDirectory`, captured once
at run start. When it is null, TigerQuery captures `Environment.CurrentDirectory` once.
`RunFromFileAsync` follows the same rule; it does not implicitly use the script's
directory. A library host that wants script-relative output must set `BaseDirectory` to
that directory explicitly.

Parent directories must already exist. TigerQuery does not create them. Physical paths
are reserved by channel after full resolution, using platform filesystem comparison
semantics. Result, normal-message companion, and error channels may not collide. A known
collision is an `OutputRoutingException`, even if no payload has yet caused the file to
be created.

Files are opened lazily on their first payload. The first use of a physical path in a run
creates or truncates it. Reusing that path later in the same run continues it. Separate
runs never append: an existing destination is overwritten on the next run. Output is
written directly, not by atomic file replacement, so cancellation or failure can leave a
valid partial file containing complete earlier result sets.

The default encoding for every result and message file is UTF-8 with a BOM. A custom
`.NET` `Encoding` keeps its BOM preference, but TigerQuery strengthens it with exception
fallbacks. An unencodable value therefore fails instead of being replaced silently.
Spreadsheet and downstream-tool compatibility is the caller's responsibility when a
non-default encoding is selected.

## Failure semantics

Invalid paths, channel collisions, missing directories, access denial, sharing
violations, incompatible single-file schemas, serialization or encoding failures, and
flush or close failures become `OutputRoutingException`. The exception's `Path` is the
fully resolved target when resolution succeeded, otherwise the originally supplied path.

An output failure is fatal regardless of `:ON ERROR` and
`ContinueOnErrorForUnhandledExceptions`. It stops subsequent batches and produces
`ExecutionResultCode.OutputFailed`; `tiger-sqlcmd` maps that result to exit code `8`.
When SQL Server and output fail contemporaneously, the output failure remains primary and
the SQL exception may be retained as secondary diagnostic context. Earlier SQL side
effects may already be committed, and earlier file content remains as partial output.

## `tiger-sqlcmd` / SqlCmdEx usage

Route an inline query to one CSV file:

```powershell
tiger-sqlcmd run --non-interactive --connection local `
  --query "SELECT Id, Name FROM dbo.Customer ORDER BY Id" `
  --output .\exports\customers.csv --format Csv
```

The `exports` directory must already exist. `-o` is an alias for `--output`.

Set initial result and error routes while allowing later script directives to replace
them:

```powershell
tiger-sqlcmd run --connection local --file .\report.sql --mode SqlCmdEx `
  --output .\exports\initial.csv `
  --error-output .\exports\sql-errors.log
```

Write one generated file per provider result set and route normal messages to companion
files:

```powershell
tiger-sqlcmd run --connection local --file .\report.sql --mode SqlCmdEx `
  --output .\exports\report.csv `
  --output-mode FilePerResultSet `
  --out-behavior ResultSetsAndNormalMessages `
  --output-encoding utf-8
```

Available aliases are:

| Purpose | Options |
| --- | --- |
| Initial result path | `-o`, `--output` |
| Initial error path | `-e`, `--error-output` |
| Format | `--format`, `--result-format` |
| File mode | `--output-mode`, `--result-file-mode` |
| Encoding | `--output-encoding`, `--encoding` |
| `:Out` channel behavior | `--out-behavior` |

A script can switch destinations between batches:

```sql
:Out customers.csv
SELECT customer.Id, customer.Name, status.Name AS StatusName
FROM dbo.Customer AS customer
JOIN dbo.Status AS status ON status.Id = customer.StatusId
ORDER BY customer.Id;
GO
:Out projects.csv
SELECT project.Id, project.Name, status.Name AS StatusName
FROM dbo.Project AS project
JOIN dbo.Status AS status ON status.Id = project.StatusId
ORDER BY project.Id;
GO
```

Run it with SQLCMD extensions enabled:

```powershell
tiger-sqlcmd run --non-interactive --connection local `
  --file .\route-results.sql --mode SqlCmdEx --format Csv
```

Relative directive paths resolve from the process working directory captured at run
start. Each `:Out` affects its following batch, so the joined and explicitly ordered
queries land in separate deterministic CSV files.

## Library API usage

Library consumers configure the reusable engine behavior directly:

```csharp
using ItTiger.TigerQuery.Engine;
using System.Text;

var options = new TigerQueryEngineOptions
{
    ConnectionString = connectionString,
    Mode = SqlCmdMode.SqlCmdEx,
    OutputRouting = new OutputRoutingOptions
    {
        BaseDirectory = Path.Combine(workDirectory, "exports"),
        InitialOutPath = "initial.csv",
        InitialErrorPath = "errors.log",
        OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages,
        ResultSetFileMode = ResultSetFileMode.SingleFile,
        ResultSetFormat = ResultSetOutputFormat.Csv,
        FileEncoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: true,
            throwOnInvalidBytes: true),
        AllowScriptOutputDirectives = true
    },
    OnResultSet = result => RenderUnroutedResult(result),
    OnMessage = (message, isException) => RenderUnroutedMessage(message)
};

var result = await new TigerQueryEngine(options)
    .RunFromFileAsync(scriptPath, cancellationToken);

if (result.ResultCode == ExecutionResultCode.OutputFailed)
{
    var failure = (OutputRoutingException)result.Exception!;
    Console.Error.WriteLine($"Output failed for '{failure.Path}'.");
}
```

Set `AllowScriptOutputDirectives` to `false` in restricted or service hosts that must
control every destination. Initial host routes still work, but encountering `:Out` or
`:Error` becomes a parser error rather than being ignored.

## Limitations and deterministic-output guidance

- CSV is the only built-in routed result-set format, and its delimiter, CRLF endings,
  header, and null representation are fixed.
- Single-file routing requires identical column names and order across all result sets
  sent to the same physical path. Use `FilePerResultSet` for heterogeneous schemas.
- File routing is not transactional with SQL execution and not an atomic file publish.
  Write into a run-owned directory and promote files only after a successful engine result
  when consumers require all-or-nothing publication.
- Always use `ORDER BY` when row order matters. TigerQuery preserves provider order but
  cannot create an order the SQL query did not request.
- Use explicit aliases for stable column names, and keep culture-independent CSV
  conversion in mind when comparing golden files.
- Set `BaseDirectory` explicitly in services, tests, and build jobs. This prevents output
  locations from changing with the caller's working directory.
- Do not route different payload channels to the same path. Companion paths are reserved
  too, so avoid choosing an error file such as `report.csv.messages.log` when that
  companion can be generated.
- Direct parser consumers do not receive route steps. Execute through `TigerQueryEngine`
  for implemented routing semantics.

## Live E2E coverage

`TigerSqlCmdCsvRoutingLiveTests.ThreeBatchesRouteJoinedResultsToSeparateCsvFiles` proves
the complete CLI path against SQL Server. The test creates related status, customer,
project, and work-item tables in an exactly owned E2E database, runs a real child
`tiger-sqlcmd` process in `SqlCmdEx` mode, and executes three ordered join queries separated
by `GO`, each preceded by a different `:Out` directive.

The test parses `customers.csv`, `projects.csv`, and `work-items.csv` with CsvHelper and
asserts exact headers and row order. It also proves that informational messages and row
counts do not contaminate CSV, no unexpected CSV or companion files are created, every
output stays in the run-owned directory, and the generated database/profile are cleaned
up through exact-instance lifecycle ownership.
