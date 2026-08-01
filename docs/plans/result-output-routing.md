# Structured result-set output and output routing

Status: proposed design

Scope: `ItTiger.TigerQuery`, `tiger-sqlcmd`, and reusable consumers such as TigerWrap

Implementation: phases 1 through 3 complete (ordered execution steps; TigerQuery-owned routing, built-in CSV, file lifecycle and output-failure handling; `tiger-sqlcmd` options, mapping, help, localization, tests, and documentation). Phase 4 (TigerWrap reuse and hardening) not started.

## Current-state summary

Today `SqlCmdParser.ReadBatchesAsync` recognizes `GO`, `:setvar`, and `:ON ERROR`, but rejects other colon directives. `:ON ERROR` mutates `QueryExecutionContext`, and prepared execution compensates for the parser's mutable state by storing `ContinueOnError` beside each `SqlBatch` in the internal `PreparedExecutionPlan`.

`QueryExecutionContext.ExecuteBatchAsync` fully materializes each provider result set into `ResultSetInfo` and invokes `TigerQueryEngineOptions.OnResultSet`. Messages similarly reach `OnMessage`. The engine itself has no console dependency. `tiger-sqlcmd` connects those callbacks directly to `TigerSqlCmdRenderer`, which uses TigerCli to render result sets as tables and messages as styled console text.

That structure is a useful starting point, but output directives cannot be implemented as final parser fields. In this script, both changes are observable and must remain in order:

```sql
:Out first.csv
SELECT 1;
GO
:Out second.csv
SELECT 2;
GO
:Out first.csv
SELECT 3;
GO
```

The parser and prepared plan therefore need an ordered execution-step model. File routing and CSV writing belong below the application renderer, in TigerQuery. TigerQuery must continue to reference neither TigerCli nor `tiger-sqlcmd`.

## 1. Goals

1. Make TigerQuery the reusable owner of script-directed output routing, result-set file output, file naming, encoding, and lifecycle.
2. Route SQL messages and result sets as separate channels.
3. Preserve every `:Out` and `:Error` occurrence as an ordered execution directive in both streaming and prepared modes.
4. Provide RFC 4180-compatible CSV as the first built-in structured result-set writer.
5. Support one file for all compatible result sets and one file per result set.
6. Use deterministic, culture-independent names for generated files.
7. Default output files to UTF-8 with a byte-order mark (BOM).
8. Keep existing application callbacks as the fallback destinations when a channel is not redirected.
9. Give TigerWrap and other applications the same routing behavior without copying `tiger-sqlcmd` or TigerCli code.
10. Keep the internal writer boundary clean enough to add another built-in format later, without committing the first public API to an unvalidated formatter contract.
11. Preserve strict CSV validity. If one CSV file cannot represent the selected result sets predictably, fail instead of inserting prose, separators, or mismatched rows.

## 2. Non-goals

- Moving console or table rendering into TigerQuery.
- Referencing TigerCli from `ItTiger.TigerQuery`.
- Defining final `tiger-sqlcmd` option names or parsing those options in TigerQuery.
- Adding JSON in the first release. JSON and its lifetime/finalization behavior remain deferred.
- Designing discovery, registration, or dynamic loading for format plugins.
- Exposing a public custom result-set writer or formatter contract in the first release.
- Streaming individual data rows to callbacks in the first phase. The initial implementation may continue to use the existing fully materialized `ResultSetInfo` event model.
- Adding `:XML`, `:List`, `:Perftrace`, shell commands, include-file directives, or the complete Microsoft sqlcmd directive surface.
- Making progress events part of a redirected output file.
- Supporting tee/mirror output to both a file and the application in the first phase.
- Solving CSV formula-injection policy by modifying data. Values must not be silently prefixed or otherwise changed.

## 3. Output channels

TigerQuery should model three routable payload channels and keep lifecycle telemetry separate:

| Channel | Payload | Application destination | File destination |
|---|---|---|---|
| Result sets | `ResultSetInfo` with columns and rows | Existing `OnResultSet` callback | TigerQuery's built-in CSV output |
| Normal messages | SQL messages with severity 0-10, including `PRINT`, informational messages, and non-error `RAISERROR` | Existing `OnMessage` callback | Plain UTF-8 text message file |
| Error messages | SQL diagnostics classified as errors and explicitly modeled script-level batch diagnostics | Existing `OnMessage` callback | Plain UTF-8 text error file |

For SQL Server diagnostics, `SqlCmdMessage.IsError` is the normal/error boundary. A non-SQL exception converted through `SqlCmdMessage.FromException` is not automatically part of the stable script-error file contract merely because its synthetic severity makes `IsError` true. TigerQuery needs an explicit internal classification for the established batch diagnostics it intentionally exposes. The callback's existing `isException` Boolean alone does not determine file-channel classification.

Batch start, batch end, prepared-plan readiness, logging, and application progress are not normal output. Their callbacks continue independently and are never redirected by `:Out` or `:Error`. Structured logging also remains independent: redirecting a message must not suppress the configured `ILogger`.

When a channel is routed to a file, its presentation callback is not invoked. This is redirect behavior, not tee behavior. When the channel is routed to the application, the existing callback is invoked exactly as it is today. An application that needs an audit observer independent of presentation can use logging initially; a general observer/tee API is deferred.

### Message file representation

Message files are not CSV files. Each `SqlCmdMessage.Text` value is written as plain text in event order and terminated with CRLF. Embedded line endings in the message text are preserved. No TigerCli markup, colors, timestamps, localized prefixes, or table formatting are added. Rich message metadata remains available through the application callback when that channel is not redirected and through the logger.

Keeping message files plain and result-set files structured avoids contaminating CSV with prose.

Ordering is guaranteed within each channel. When channels are routed to different files, TigerQuery does not add cross-channel sequence markers or attempt to reconstruct one mixed transcript; doing so would make the CSV non-standard. A consumer that requires a persisted combined timeline needs a future independent observer/tee facility rather than a structured CSV route.

## 4. TigerQuery responsibilities

TigerQuery should own:

- recognition, syntax validation, variable expansion, and ordered representation of `:Out` and `:Error`;
- a run-scoped output-routing state machine;
- classification of messages into normal and error channels;
- resolution and canonicalization of output paths against one fixed base directory;
- lazy file creation, overwrite behavior, stream ownership, flushing, and disposal;
- default UTF-8-with-BOM encoding and CRLF line endings;
- single-file and per-result-set behavior;
- deterministic generated names;
- built-in CSV value conversion, quoting, escaping, headers, and record writing;
- schema compatibility checks before appending another result set to one CSV file;
- conversion of routing, serialization, and file I/O failures into a distinct fatal TigerQuery exception;
- invoking the application callback only when the applicable channel is currently routed to the application;
- identical directive and routing semantics in streaming and prepared execution.

The routing code should live in `ItTiger.TigerQuery`, close to the execution coordinator, rather than in `ItTiger.TigerQuery.CliCore`. CliCore is also an application-facing layer and is not an appropriate dependency for TigerWrap's reusable execution path.

## 5. Application responsibilities

Applications, including `tiger-sqlcmd` and TigerWrap, should own:

- console/table rendering and terminal styling;
- TigerCli-specific formatting and all TigerCli references;
- CLI option names, aliases, help text, validation, and argument parsing;
- selection of initial routes from application settings;
- selection of TigerQuery's built-in CSV behavior through public routing configuration;
- mapping `OutputRoutingException` to an exit code or user-facing error;
- user-facing progress and status presentation;
- any policy that disables script output directives in a service or restricted host.

`tiger-sqlcmd` should build `TigerQueryEngineOptions.OutputRouting` and continue to supply `TigerSqlCmdRenderer` as the application destination. It must not open CSV files, generate per-result-set names, or interpret `:Out`/`:Error` itself.

TigerWrap should consume the same TigerQuery options and built-in CSV implementation. It should not copy routing, naming, encoding, or CSV code from `tiger-sqlcmd`, and it should not need TigerCli merely to obtain file output.

## 6. CSV behavior and defaults

The first built-in writer is CSV with these fixed version-one defaults:

| Setting | Version-one behavior |
|---|---|
| Encoding | UTF-8 with BOM, configured to throw on invalid/unencodable input |
| Delimiter | Comma (`,`) |
| Record ending | CRLF |
| Header | Enabled |
| SQL `NULL` | Empty field |
| Quoting | RFC 4180-compatible minimal quoting |
| Culture | Invariant |

Header names and data fields use the same escaping rules. A field is enclosed in double quotes if it contains a comma, a double quote, CR, or LF. Every double quote inside a quoted field is doubled. Embedded CR/LF characters in a field are preserved; CRLF refers to the record terminator added by the writer.

Example records, with `[CRLF]` denoting the two record-ending bytes:

```text
Id,Name,Comment[CRLF]
1,Alice,"contains, comma"[CRLF]
2,Bob,"said ""hello"""[CRLF]
3,,[CRLF]
```

Both `DBNull.Value` and a null object reference are SQL nulls for serialization and produce an empty field. An empty string also produces an empty field. Consequently, SQL `NULL` and the empty string are deliberately indistinguishable in version one. This limitation must appear in API and CLI documentation.

The value conversion rules should be deterministic:

- strings and characters use their text value unchanged before CSV escaping;
- `DateTime` and `DateTimeOffset` use invariant ISO 8601 round-trip formatting;
- `TimeSpan` uses the invariant constant (`c`) format;
- `Guid` uses the `D` format;
- `byte[]` uses uppercase hexadecimal with a `0x` prefix;
- floating-point values use a round-trip invariant format;
- other `IFormattable` values use invariant culture;
- remaining values use `ToString()` and are then escaped as text.

These choices avoid machine-culture variation and remain readable by common CSV libraries and spreadsheet tools. The writer must not add result-set banners, blank separator records, comments, table borders, or message text to a CSV file.

TigerQuery must configure the selected encoding with exception fallbacks so unencodable characters cause an output failure rather than silent replacement. The UTF-8-with-BOM default is the supported interoperability baseline. If the public API permits another encoding, validate it before execution begins where possible and document that CSV-library and spreadsheet interoperability then depends on that encoding and its consumer support.

Result sets with zero columns are not CSV result sets and do not create a file. A zero-row result set with columns does create output and writes its header. In file-per-result-set mode a zero-column result likewise creates no file, but its original result-set coordinate remains consumed; later generated filenames are not renumbered around it.

Configurable delimiters are not part of the first phase. Disabling headers and choosing a non-empty null token are explicitly deferred even though the API should leave room to add CSV options later.

## 7. File modes and naming rules

### Recommended defaults

- `ResultSetFileMode.SingleFile`
- built-in CSV output
- UTF-8 with BOM
- overwrite on first use in each run
- lazy creation
- `OutDirectiveBehavior.ResultSetsOnly`
- application callbacks for all channels until an initial file route or script directive changes one

The parent directory must already exist. TigerQuery should not create a directory tree implicitly. Files are created only when the first payload for that physical destination is written; merely parsing a directive creates nothing.

On first use of a physical path in a run, TigerQuery uses create/truncate semantics. If the script later returns to the same canonical path, the run continues that destination rather than truncating it again. Output is never appended across separate runs by default. Single-file destinations remain open until run completion so their BOM, schema, header, and internal writer state cannot be duplicated. A run-scoped destination registry owns each lazily opened stream and disposes all of them in a `finally` path.

Version one imposes no TigerQuery-specific maximum on open destinations and performs no eviction. A pathological script that routes to many distinct single-file paths can therefore consume many file handles; limits and safe eviction/reopen policies are deferred.

TigerQuery should resolve relative paths against `OutputRoutingOptions.BaseDirectory`, captured once at run start. If it is not supplied, capture `Environment.CurrentDirectory` once. `RunFromFileAsync` should not silently use a different rule; an application that wants paths relative to the script can explicitly pass the script directory. This makes reader, string, and file entry points consistent.

Path comparison follows the platform's file-system semantics after full-path resolution. `:Out` and `:Error` targets, normal-message companion files, and generated result-set files must not collide across different channels. A known collision is a configuration/directive error rather than permission to mix payload types.

### Single-file mode

The resolved `:Out` path is used exactly as supplied; TigerQuery does not infer a format from the extension and does not add `.csv`. Thus `:Out report.csv` writes `report.csv`, while `:Out report` writes a CSV file named `report` when CSV is selected.

All result sets routed to that path in the run share one writer. Returning to a previous path continues it. For CSV, the first result set writes the header and establishes the required schema. Later compatible result sets append rows only; the header is not repeated.

CSV compatibility means the same column count and the same column names in the same ordinal positions, compared with `StringComparison.Ordinal`. The first result set establishes this schema and its exact header is written once. Later compatible result sets append rows only: their headers are never regenerated, rewritten, normalized, or substituted for the first header. CSV is text and has no type schema, so differing SQL/CLR types alone do not make a set incompatible. Requiring matching names prevents a single header from silently describing different data. Empty and duplicate column names are allowed but must match exactly in later sets.

TigerQuery validates each later result set before writing any bytes from that result set. An incompatible set throws `OutputRoutingException` and writes none of its header or rows. Earlier file content remains as partial run output.

```sql
:Out report.csv
SELECT 1 AS Id, 'one' AS Name;
GO
SELECT 2 AS Id, 'two' AS Name;       -- appends rows, no second header
GO
SELECT 3 AS Different;               -- fatal: incompatible with report.csv
GO
```

### File-per-result-set mode

The requested path is a base name. The writer generates one name from the globally stable result-set coordinates already present in `ResultSetInfo`:

```text
<stem>_b<batch>_e<execution>_r<result><extension>
```

Each numeric component is one-based, invariant, and padded to at least four digits. Values longer than four digits are not truncated. The original final extension is retained. If the base has no extension, the selected built-in format's default extension is added; CSV's default is `.csv`.

| Base path and source | Generated path |
|---|---|
| `report.csv`, batch 1, execution 1, result 1 | `report_b0001_e0001_r0001.csv` |
| `report.csv`, batch 3, `GO 2` execution 2, result 1 | `report_b0003_e0002_r0001.csv` |
| `exports/report`, batch 12, execution 1, result 3 | `exports/report_b0012_e0001_r0003.csv` |
| `report.data`, batch 10000, execution 1, result 1 | `report_b10000_e0001_r0001.data` |

The batch number is the logical batch number across the whole run, not the number since the most recent `:Out`. Batch, execution, and result-set coordinates are the original engine coordinates even when an earlier zero-column result created no file. This makes names identical in prepared and streaming modes and prevents route changes or skipped zero-column results from renumbering output. Each file has its own header, and no cross-result schema compatibility check is needed.

### Normal-message companion in all-normal mode

CSV cannot safely contain both rows and arbitrary messages. Therefore `ResultSetsAndNormalMessages` treats the `:Out` filename as the result-set destination and routes normal messages to a deterministic companion text file formed by appending `.messages.log` to the complete resolved result path:

```text
:Out report.csv
result sets     -> report.csv                    (single-file mode)
normal messages -> report.csv.messages.log

:Out report.csv
result sets     -> report_b0001_e0001_r0001.csv  (per-result-set mode)
normal messages -> report.csv.messages.log
```

The companion is lazy and is absent when no normal messages occur. It uses UTF-8 with BOM and CRLF. Keeping the requested result path stable in both `:Out` behaviors is less surprising than changing the CSV name according to whether messages are included.

## 8. `:Out` semantics

Colon commands are case-insensitive and are recognized only when `SqlCmdMode` is not `Normal`, matching the existing directive boundary. In normal mode they remain SQL text.

Accepted syntax is one non-empty filename argument:

```text
:Out results.csv
:Out "directory with spaces/results.csv"
```

The quoted form uses the parser's existing double-quoted sqlcmd escaping (a doubled quote represents a quote). A trailing single-line comment is allowed under the same rules as other directives. Extra arguments, an empty quoted path, or unsupported trailing content are parser errors. `$(name)` references are expanded at the directive's source position after the filename token is parsed. Undefined references remain literal, consistent with ordinary existing expansion.

There are no magic `stdout`, `stderr`, `off`, or `-` targets in version one; those strings are literal filenames. Once redirected, a channel remains redirected until another directive changes it or the run ends.

`OutDirectiveBehavior` controls which channels change:

- `ResultSetsOnly` (recommended default): change the result-set route only. Normal and error messages retain their current routes.
- `ResultSetsAndNormalMessages`: change the result-set route and the normal-message route. Result sets use the requested structured-output path, normal messages use its deterministic `.messages.log` companion, and errors remain controlled independently by `:Error`.

Precedence is:

1. application callbacks are the default destinations;
2. application-supplied initial output paths replace those defaults at run start;
3. each script directive replaces the relevant current route from its effective position onward;
4. the latest directive for a channel wins;
5. `:Out` never overrides `:Error`, and `:Error` never overrides result sets or normal messages.

Applications may disable script output directives for security or policy. The recommended disabled behavior is a clear parser error, not silently ignoring a script command. When directives are enabled (the command-line default), they override application initial paths as described above.

### Directive position relative to a buffered batch

The current parser allows `:ON ERROR` between SQL text and its terminating `GO`, and tests establish that the directive affects that buffered batch because the batch has not executed yet. `:Out` and `:Error` should follow the same execution rule:

```sql
SELECT 1;
:Out selected.csv
GO
```

`selected.csv` receives the result. Internally, directives encountered while a SQL batch is buffered are queued in source order and emitted as execution steps immediately before the completed batch step. They are not moved across a previously completed `GO` batch. Consecutive directives preserve their order.

This is execution order, not a final-state snapshot. It also handles a final unterminated batch at end of input.

## 9. `:Error` semantics

`:Error` has the same filename grammar, expansion, path resolution, lazy creation, overwrite-once behavior, and ordered lifetime as `:Out`.

The resolved filename is used exactly as supplied. `ResultSetFileMode` never adds a result-set suffix to an error file. For example, `:Error errors.log` writes error messages to `errors.log` in both single-file and file-per-result-set modes.

It redirects only the stable script-error channel. That channel contains:

- SQL Server diagnostics for which `SqlCmdMessage.IsError` is true, including diagnostics surfaced through either `InfoMessage` or `SqlException`;
- established TigerQuery batch-execution diagnostics that TigerQuery intentionally models as script errors, with a stable documented representation rather than arbitrary exception text.

It does not receive result sets, severity 0-10 messages, application progress, logs, or arbitrary infrastructure exception text. Parser and preparation failures, connection-opening failures, output-routing and serialization failures, configuration failures, encoding-validation failures, and unrelated application exceptions remain application-level failures unless a future contract explicitly models one as a script error.

TigerQuery must not implement `:Error` by writing `Exception.ToString()` or every caught exception's `Message`. Script-error output must avoid connection strings, SQL values, sensitive exception details, secrets, and unstable framework/provider implementation text.

Parser/preparation and connection-opening failures occur outside an active batch and currently escape `TigerQueryEngine.RunAsync`. They remain application responsibilities; neither mode should claim that a script directive captured an error that occurred before directive execution began.

Provider diagnostics that are delivered both through `InfoMessage` and a thrown `SqlException` continue to use the engine's existing per-attempt deduplication. Routing occurs after deduplication, preserving server order and preventing duplicate error-file lines.

An output failure cannot reliably be written to the file that just failed. TigerQuery should log it when a logger is available, close other destinations, and surface `OutputRoutingException` with the target path and original exception. During an active batch it is the primary exception on the failed batch/execution result described in section 12; failures outside the coordinator's result path may propagate it directly.

## 10. Streaming versus prepared execution

Both modes must consume the same ordered logical step model:

```text
SetOutRoute -> ExecuteBatch -> SetErrorRoute -> ExecuteBatch -> SetOutRoute -> ...
```

The parser should gain an execution-step enumeration used by the engine. Conceptually, the internal union is:

```csharp
internal abstract record ExecutionStep;
internal sealed record ExecuteBatchStep(ExecutionBatch Batch) : ExecutionStep;
internal sealed record SetOutRouteStep(OutputDirective Directive) : ExecutionStep;
internal sealed record SetErrorRouteStep(OutputDirective Directive) : ExecutionStep;
```

Names may change during implementation, but the discriminated, ordered representation is the requirement.

Streaming mode consumes steps as the parser yields them. The SQL connection continues to open before parsing, as it does today. Route steps mutate only the run's `OutputRoutingState`; batch steps execute through the existing coordinator.

Prepared mode stores all steps in `PreparedExecutionPlan` in the same sequence. It must not keep only `LastOutPath` or `LastErrorPath`, and it must not apply all directives before execution. Plan counts count only batch steps and their positive `GO n` executions; route directives do not change `ExecutionPlanReady` totals.

Preparation validates directive syntax, expands variables at the correct source position, resolves paths, and can detect statically known path collisions. It does not create or probe output files. After plan readiness and connection opening, the coordinator replays route and batch steps in order. Thus a valid prepared script with a connection failure creates no output files.

Both modes use the result coordinates assigned by the shared batch coordinator, so CSV content order, route transitions, generated filenames, callback suppression, and failures are identical for the same successfully parsed and executed prefix. The established mode difference remains: a late parser error can occur after earlier side effects and files in streaming mode, while prepared mode catches it before connection opening or file creation.

The public `SqlCmdParser.ReadBatchesAsync` API remains backward-compatible and continues to return only `SqlBatch` values. It projects away recognized `:Out` and `:Error` execution steps while retaining their syntax validation and the existing batch parsing behavior. Direct parser consumers therefore do not receive routing metadata through this method.

The engine must stop using `ReadBatchesAsync` as its authoritative execution representation and instead consume a new internal ordered step reader. No public script-step API is introduced in the initial implementation. A public step API may be considered later only if direct parser consumers demonstrate a concrete need.

## 11. Proposed public API shape

The following is a proposed shape, not a commitment to exact type names. The first release exposes routing and built-in-format selection, but no formatter or writer extension contract.

```csharp
public sealed class TigerQueryEngineOptions
{
    // Existing members remain.
    public OutputRoutingOptions OutputRouting { get; init; } = new();
}

public sealed class OutputRoutingOptions
{
    public string? InitialOutPath { get; init; }
    public string? InitialErrorPath { get; init; }
    public string? BaseDirectory { get; init; }

    public OutDirectiveBehavior OutBehavior { get; init; }
        = OutDirectiveBehavior.ResultSetsOnly;

    public ResultSetFileMode ResultSetFileMode { get; init; }
        = ResultSetFileMode.SingleFile;

    // null selects TigerQuery's strict UTF-8-with-BOM default. Any supplied
    // encoding is validated and used with exception fallback behavior.
    public Encoding? FileEncoding { get; init; }

    public ResultSetOutputFormat ResultSetFormat { get; init; }
        = ResultSetOutputFormat.Csv;

    public bool AllowScriptOutputDirectives { get; init; } = true;
}

public enum OutDirectiveBehavior
{
    ResultSetsOnly = 0,
    ResultSetsAndNormalMessages = 1
}

public enum ResultSetFileMode
{
    SingleFile = 0,
    FilePerResultSet = 1
}

public enum ResultSetOutputFormat
{
    Csv = 0
}

public sealed class OutputRoutingException : TigerQueryException
{
    public string? Path { get; }
}
```

TigerQuery creates and owns the `Stream`, `StreamWriter`, encoding, flush policy, and disposal. CSV serialization and its one-writer-per-file state are internal to `ItTiger.TigerQuery`. TigerWrap selects `ResultSetOutputFormat.Csv` and reuses this implementation through normal engine options; it does not receive or recreate a CSV writer object.

The implementation should still separate routing, file ownership, and CSV serialization internally. Those internal boundaries can be exercised by tests and can support a future JSON experiment without redesigning routing. They must not be public in version one.

A public writer contract is considered only after the built-in CSV implementation and a real JSON implementation have validated writer lifetime, finalization, cancellation, partial-document behavior, stream ownership, and one-writer-per-file behavior. JSON itself remains deferred and is not selectable through the first-release API.

The application fallback remains the existing `OnResultSet` and `OnMessage` callbacks; a second set of TigerCli-specific sink interfaces is unnecessary. Internally, `OutputRouter` decides whether to invoke the callback or the current file destination.

The file system and clock do not need to become public abstractions. Small internal stream-factory seams are appropriate for deterministic failure tests.

## 12. Error handling and failure modes

### Parser and configuration failures

Malformed directive syntax, a disabled script directive, an invalid path, or a statically detectable channel collision throws a `TigerQueryException` before the affected batch executes. Prepared mode detects all such parser-visible failures before opening the SQL connection. Streaming mode detects them when reached.

Unknown enum values, an unusable encoding, an encoding configured for silent replacement, or a base directory that cannot be resolved should fail at run start with an argument/configuration exception. Encoding and base-directory validation should happen before parsing, connection opening, SQL execution, or output-file creation where possible.

### File and writer failures

Directory-not-found, access denied, sharing violations, name collisions discovered at creation, unencodable characters, CSV incompatibility, serialization failures, flush failures, and close failures become `OutputRoutingException`.

An output failure is fatal regardless of `:ON ERROR IGNORE` and `ContinueOnErrorForUnhandledExceptions`. Continuing would execute later SQL while losing or corrupting its requested output. When result serialization or message routing fails during a batch:

- `OutputRoutingException` is the primary exception;
- `BatchEnd` is emitted with `Success = false` and that exception;
- the batch is counted as failed, not successful;
- the run stops immediately regardless of the effective `:ON ERROR` policy;
- the execution result uses a deterministic output-failure result code, preferably a new `ExecutionResultCode.OutputFailed`, and retains the same `OutputRoutingException` as `ExecutionResult.Exception`;
- `tiger-sqlcmd` maps that result distinctly and deterministically, whether to a dedicated exit code or an explicitly documented existing one.

SQL Server may already have completed the command, and may have committed side effects, before TigerQuery discovers a result serialization failure. The output and overall batch outcome are nevertheless failed. Diagnostics should state that SQL execution completed before output failed when this is known, without reclassifying the batch as successful. A route change that fails outside a batch stops before the next batch and uses the same output-failure classification.

Provider message events are synchronous. File-write exceptions raised while handling an `InfoMessage` should be captured by the router and rethrown at the next safe coordinator boundary rather than escaping unpredictably through SqlClient's event dispatch. Preserve the first `OutputRoutingException` as primary and attach any contemporaneous SQL exception only as secondary diagnostic context.

### Partial output and durability

Version one writes directly to destination files; it does not promise atomic replacement. A failed or cancelled run may leave valid partial CSV containing all complete result sets written before the failure. Because the current engine materializes a result set before routing it, a provider failure while reading that set writes none of that set.

Flush after each complete result set and at batch boundaries. Message text should be flushed at batch boundaries rather than forcing a disk flush for every message. All destinations are flushed and disposed when the run succeeds, fails, or is cancelled. If cleanup also fails, retain the primary exception and report cleanup failures without replacing the primary cause.

The engine should expose the target path in routing exceptions but avoid embedding connection strings, SQL values, or other secrets in error messages.

## 13. Backward compatibility

- With no initial file paths and no `:Out`/`:Error`, all current callbacks and `tiger-sqlcmd` table/message output remain unchanged.
- `:Out` and `:Error` currently fail as unknown directives in sqlcmd modes, so recognizing them is additive for executable scripts rather than a change to a working routing behavior.
- `SqlCmdMode.Normal` continues to send colon-prefixed text to SQL Server.
- Existing `ResultSetInfo`, `ColumnInfo`, `SqlCmdMessage`, batch lifecycle events, and prepared-plan count notification remain usable.
- The first implementation can continue materializing result sets, avoiding an event-model break.
- Existing `:ON ERROR` behavior and its per-batch prepared snapshot remain unchanged. It may later move into the same ordered step model, but output routing does not require that compatibility-sensitive cleanup.
- Existing logging continues even when presentation output is redirected.
- File routing is opt-in through an initial route or an encountered directive. The recommended `tiger-sqlcmd` rollout should not change console output merely because the binary was upgraded.
- The TigerQuery project file must gain no TigerCli or `tiger-sqlcmd` reference. A dependency test or project-reference assertion should protect this boundary.

Direct users of `SqlCmdParser.ReadBatchesAsync` receive the same batch-only shape as before. Release notes and API documentation must state that the parser now recognizes and validates `:Out` and `:Error` but projects those internal execution steps away; consumers that need routing should execute through `TigerQueryEngine`.

## 14. Testing strategy

### Parser and plan tests

- Accept casing, quoted paths, escaped quotes, comments, variables, absolute paths, and relative paths.
- Reject missing paths, extra tokens, empty paths, bad terminators, disabled directives, and invalid path forms.
- Assert exact step order for alternating `:Out`, `:Error`, SQL batches, `GO n`, and end-of-input batches.
- Assert that a directive after buffered SQL but before `GO` is ordered before that batch.
- Assert that prepared plans retain every directive, including repeated routes to the same path.
- Assert that directive steps do not affect logical-batch or execution totals.
- Assert no files or callbacks on prepared parser failure.
- Assert that public `ReadBatchesAsync` returns the same batches as the internal step path while projecting every `:Out` and `:Error` step away.
- Assert that no public script-step types or methods are introduced in the first-release API surface.

### Routing-state tests

- Cover all three channels and both `OutDirectiveBehavior` values.
- Prove that the most recent directive changes only its documented channels.
- Prove that application callbacks are used before redirection and suppressed after redirection.
- Prove normal/error classification at severities 10, 11, and fatal SQL severity, plus each explicitly modeled TigerQuery script-error diagnostic.
- Prove parser, connection, routing, configuration, encoding, and unrelated infrastructure exception text never enters a `:Error` file.
- Prove logger calls remain independent of routing.
- Cover initial-route versus script-directive precedence and disabled directive policy.
- Cover return to an earlier path without a second truncation or BOM.

### CSV golden-byte tests

- Assert the exact UTF-8 BOM bytes.
- Assert the default encoding throws on invalid/unencodable input instead of writing a replacement character.
- Validate alternate encodings before SQL execution where possible and test their documented interoperability boundary.
- Assert comma delimiter and CRLF record bytes on every platform.
- Cover commas, quotes, CR, LF, CRLF, Unicode, leading/trailing whitespace, empty strings, nulls, and zero-row results.
- Cover invariant numbers under a non-English current culture.
- Cover round-trip date/time, GUID, duration, binary, decimal, and floating-point formats.
- Parse emitted files with at least one widely used CSV library in tests to guard interoperability.
- Explicitly assert that null and empty string serialize identically.
- Assert no file for a zero-column result and a header-only file for a zero-row result.

### File-mode tests

- Single file: the first exact header written once, ordered rows across compatible result sets, exact ordinal-name comparison, no later header normalization/regeneration, and failure before any bytes from an incompatible set are written.
- File per result set: one header per file, no cross-file schema restriction, and no file for a zero-column result.
- Golden tests for names with and without extensions, directories, `GO n`, multiple results, values over 9999, Unicode stems, and culture changes.
- Assert that a skipped zero-column result does not renumber later generated filenames.
- Collision tests across result, normal-message companion, and error paths.
- Existing-file overwrite, missing directory, access denied, sharing violation, cancellation, and cleanup behavior.
- Assert single-file destinations remain open until run completion and retain exactly one BOM/header after switching away and back.

### Execution-mode parity tests

Run the same scripted route changes through streaming and prepared probes and compare:

- execution event order;
- callback/file disposition;
- file names and byte content;
- schema failures and routing exception type;
- output failure after successful SQL execution: failed `BatchEnd`, failed-batch count, immediate stop, primary `OutputRoutingException`, and deterministic output-failure result code;
- cancellation cleanup;
- `:ON ERROR` interaction;
- message deduplication.

Retain the expected timing difference for a late parser failure: streaming may have earlier files and SQL effects; prepared mode has neither.

### Application tests

- `tiger-sqlcmd` settings map to TigerQuery routing options, without duplicating serialization, naming, encoding, or path logic.
- Existing invocations still render tables and messages when no file route is selected.
- Routed messages contain no TigerCli markup.
- CLI exit-code mapping for parser, connection, SQL, cancellation, and output failures is deterministic.
- TigerWrap can select and reuse TigerQuery CSV without TigerCli and without any public writer interface.
- Public API approval tests confirm that no first-release custom-writer or script-step contract is exposed.
- A project/dependency test confirms `ItTiger.TigerQuery` does not reference TigerCli.

Live SQL tests should be limited to provider ordering and multi-result behavior that cannot be represented by engine probes; CSV and path behavior should remain fast unit tests.

## 15. Deferred features

- Configurable CSV null tokens.
- Disabling or customizing CSV headers.
- Alternate delimiters, quoting policies, or newline policies.
- JSON and other additional formats.
- A public result-set writer/formatter contract. Consider it only after CSV and a real JSON implementation validate lifetime, finalization, cancellation, partial-document behavior, stream ownership, and one-writer-per-file behavior.
- A format registry, discovery mechanism, or dynamically loaded plugins.
- Row-by-row streaming output that avoids materializing `ResultSetInfo.Rows`.
- Tee/mirror routes and independent audit observers.
- Append-across-runs, fail-if-exists, and atomic replace policies.
- Automatic directory creation.
- Compression and archive output.
- Standard-output/standard-error magic directive targets and a directive that restores application routing.
- CSV formula-injection mitigation options.
- Schema selection policies looser or stricter than exact column names in ordinal order.
- File-name templates supplied by users.
- Asynchronous message-file writes or bounded message buffering.
- Open-destination limits and eviction/reopen policies for pathological scripts with many route targets.
- Sandboxed path policies beyond the host's ability to disable directives and choose a base directory.

## 16. Open design questions

The engine/parser decisions in this plan are resolved. The remaining questions belong to the application contract:

1. **Error exit code:** Should `tiger-sqlcmd` introduce a dedicated output-failure exit code or map `ExecutionResultCode.OutputFailed` to an existing fatal/output category? Either choice must be explicit and deterministic.
2. **CLI surface:** What final `tiger-sqlcmd` option names, aliases, and help grouping should represent initial output, file mode, format, and `OutDirectiveBehavior`? TigerQuery must not define these names.

None of these questions permits mixing messages into CSV, collapsing directives into final parser state, moving routing into `tiger-sqlcmd`, or adding a TigerCli dependency to TigerQuery.

## 17. Proposed phased rollout

### Phase 1: ordered execution model

- Add parser characterization tests for directive placement around buffered batches.
- Introduce internal ordered execution steps and change both engine modes to consume them.
- Extend `PreparedExecutionPlan` to retain route steps while preserving current counts and `:ON ERROR` behavior.
- Keep public `ReadBatchesAsync` batch-only by projecting internal route steps away; do not add a public step API.
- Keep all destinations as application callbacks in this phase to isolate coordinator risk.

### Phase 2: TigerQuery routing and CSV

- Add `OutputRoutingOptions`, run-scoped routing state, path resolution, destination registry, and output exceptions.
- Add internal writer boundaries plus built-in CSV, strict encoding, single-file compatibility validation, per-result-set naming, message files, and lifecycle.
- Implement `:Out` and `:Error` on top of the ordered step stream.
- Keep all writer/formatter types internal and expose only built-in CSV selection.
- Complete parser, golden-byte, failure-injection, and mode-parity tests.

### Phase 3: `tiger-sqlcmd` integration

- Add application-owned options for initial output path, CSV selection, file mode, and `:Out` behavior. Suggested concepts are `output`, `output-mode`, `format`, and `out-behavior`; final names and aliases belong to the CLI review.
- Map those settings to TigerQuery options.
- Continue using `TigerSqlCmdRenderer` only as the application fallback for console tables/messages and progress.
- Add localized help and explicit documentation of BOM, headers, null/empty ambiguity, filename examples, overwrite behavior, and partial files.
- Add deterministic output-failure exit handling.

### Phase 4: reuse and hardening

- Migrate TigerWrap to TigerQuery routing and remove any duplicate execution/file-output logic.
- Validate that TigerWrap reuses TigerQuery's built-in CSV path without public writer interfaces or TigerCli.
- Explore a real JSON implementation internally to test writer lifetime, finalization, cancellation, partial documents, stream ownership, and one-writer-per-file behavior.
- Consider a public writer extension contract only after both CSV and JSON have proved the internal design; do not make that contract part of this rollout by default.
- Review memory use and decide whether row streaming warrants a separate future design.
- Publish compatibility notes for direct parser consumers and the newly recognized directives.

Each phase should leave the no-routing execution path green. The rollout is complete when `tiger-sqlcmd` and TigerWrap use the same TigerQuery routing implementation, CSV golden tests pass in both execution modes, and `ItTiger.TigerQuery` remains free of TigerCli references.
