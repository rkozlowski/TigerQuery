# `tiger-sqlcmd`

## One-time E2E connection setup

Create the default bootstrap profile with Integrated authentication:

```console
tiger-sqlcmd connections add-e2e-bootstrap --server sql01
```

With no `--name`, `tiger-sqlcmd` uses its host-configured default name,
`tiger-sqlcmd-e2e`. Override it for one invocation when needed:

```console
tiger-sqlcmd connections add-e2e-bootstrap --name team-bootstrap --server sql01
```

Add `--allow-database-create` only when test infrastructure is explicitly
allowed to create databases through that profile. This command creates a
connection profile only; it never creates or deletes a database.

To create an E2E-authorized profile that is not the bootstrap identity, reuse
regular add:

```console
tiger-sqlcmd connections add worker-e2e --server sql01 --e2e
```

For a non-interactive build agent using Integrated authentication, select its
writable store explicitly and supply all connection inputs:

```console
$env:TIGERQUERY_CONNECTION_STORE_FILE = 'C:\agent\state\connections.json'
tiger-sqlcmd connections add-e2e-bootstrap --non-interactive --server sql01
```

For fully non-interactive SQL authentication, keep the secret out of argv and
the writable store by supplying reference JSON. This PowerShell example uses an
environment variable for the server, a keyed JSON file for the username, and a
whole text file for the password:

```powershell
$env:TIGERQUERY_CONNECTION_STORE_FILE = 'C:\agent\state\connections.json'
$env:TQ_E2E_SQL_SERVER = 'sql01'
tiger-sqlcmd connections add-e2e-bootstrap --non-interactive `
  --authentication SqlPassword `
  --server-reference '{"Source":"EnvironmentVariable","Name":"TQ_E2E_SQL_SERVER"}' `
  --username-reference '{"Source":"File","Path":"C:\\secrets\\sql-auth.json","Format":"Json","Key":"username"}' `
  --password-reference '{"Source":"File","Path":"C:\\secrets\\sql-password","Format":"Text"}'
```

The JSON file must be a top-level object and the exact keyed property must be a
string. A text file is read whole with no trimming, so include a trailing newline
only when it is part of the intended value.

Alternatively, reference one complete connection string and do not supply any
individual connection fields:

```powershell
tiger-sqlcmd connections add-e2e-bootstrap --non-interactive `
  --connection-string-reference '{"Source":"EnvironmentVariable","Name":"TQ_E2E_SQL_CONNECTION_STRING"}'
```

The five reference options are `--server-reference`, `--database-reference`,
`--username-reference`, `--password-reference`, and
`--connection-string-reference`. They accept only reference objects, never
literal JSON strings. Full connection-string and field modes are mutually
exclusive and a mixed invocation fails before modifying the store. The regular
`connections add <name>` flow accepts the same options, including alongside
`--e2e`.

The store path may instead be supplied as
`--tq-connection-store-file <path>`. Like every TigerCli option, place it after
the command path and required positional arguments, for example:

```console
tiger-sqlcmd connections add worker-e2e --server sql01 --e2e --tq-connection-store-file C:\state\connections.json
tiger-sqlcmd connections add-e2e-bootstrap --server sql01 --tq-connection-store-file=C:\state\connections.json
```

Use the `--name=value` or `--tq-connection-store-file=value` form when an option
value begins with `-`. CLI store selection outranks
`TIGERQUERY_CONNECTION_STORE_FILE`, which outranks the application default.

> [!WARNING]
> SQL Server access is not E2E authorization. TigerQuery does not probe local
> instances, localhost, LocalDB, services, containers, or other profiles. A
> bootstrap must be selected by its explicit/default name and carry the exact
> `ittiger.e2e.enabled=true` metadata written by the commands above.

> [!IMPORTANT]
> On Windows, protected passwords use current-user/current-machine DPAPI. Copying
> a store file to CI or a container does not make those passwords decryptable
> there. Prefer an environment or mounted-file password reference for portable
> automation. `show` and `list` print reference locators, not resolved values;
> file paths and variable names are therefore visible and should be named with
> that in mind.

## Output routing

The advanced `tiger-sqlcmd run` command can send result sets and SQL messages
to files through TigerQuery's output-routing engine. Routing is opt-in. With no
output options and no `:Out` or `:Error` directive, result sets still use the
existing TigerCli table renderer and messages still use the existing styled
console renderer.

## Result output options

| Option | Default | Purpose |
| --- | --- | --- |
| `-o`, `--output <file>` | Application/console renderer | Set the initial result-set path. A later `:Out` replaces it. |
| `--format`, `--result-format <Csv>` | `Csv` | Select the built-in structured result-set format. CSV is the only Phase 3 format. |
| `--output-mode`, `--result-file-mode <SingleFile\|FilePerResultSet>` | `SingleFile` | Write compatible result sets to one file or generate one file per result set. |
| `--output-encoding`, `--encoding <name>` | UTF-8 with BOM | Use a .NET encoding name for every routed result and message file. |

For example:

```console
tiger-sqlcmd run -c local -f report.sql -o exports/report.csv
tiger-sqlcmd run -c local -f report.sql -o exports/report.csv --output-mode FilePerResultSet
```

Relative paths, including paths in script directives, resolve from the process
working directory captured at the start of the run. The parent directory must
already exist; neither `tiger-sqlcmd` nor TigerQuery creates it automatically.

## Message and error routing options

| Option | Default | Purpose |
| --- | --- | --- |
| `-e`, `--error-output <file>` | Application/console renderer | Set the initial SQL error-message path. A later `:Error` replaces it. |
| `--out-behavior <ResultSetsOnly\|ResultSetsAndNormalMessages>` | `ResultSetsOnly` | Decide whether `:Out` and `--output` also redirect normal SQL messages. |

`ResultSetsAndNormalMessages` does not mix prose into CSV. Result sets use the
requested output path and normal messages use a plain-text companion formed by
appending `.messages.log` to that complete path. For example, `report.csv`
produces normal messages in `report.csv.messages.log`. Error messages remain
controlled independently by `--error-output` and `:Error`.

File routing is redirection, not tee output: a routed channel no longer calls
its console presentation renderer. Batch progress and logging are not routed.
`:Out` and `:Error` are interpreted in order by TigerQuery, and a directive
overrides the applicable initial CLI route from its position onward.

## CSV and encoding contract

The built-in CSV writer always uses a comma delimiter, CRLF record endings,
minimal RFC 4180 quoting, invariant value formatting, and a header. Headers
cannot be disabled in this version. In `SingleFile` mode the first result set
establishes the header; later result sets must have exactly the same column
names in the same ordinal positions and append rows without another header.

The default encoding is UTF-8 with a byte-order mark (BOM). An explicit
`--output-encoding` value is resolved as a .NET encoding name and validated
before connection resolution. TigerQuery configures it with exception
fallbacks, so an unencodable value fails the run rather than being silently
replaced. The selected encoding's BOM preference is preserved, and
interoperability with CSV readers and spreadsheets then depends on their
support for that encoding.

> [!IMPORTANT]
> SQL `NULL` and an empty string both become an empty CSV field. They are
> intentionally indistinguishable in this version.

## File names, overwrite, and partial files

`SingleFile` uses the requested path exactly as supplied and does not infer an
extension. `FilePerResultSet` treats it as a base name and generates:

```text
<stem>_b<batch>_e<execution>_r<result><extension>
```

Coordinates are one-based and padded to at least four digits. Thus
`report.csv` for batch 1, execution 1, result set 1 becomes
`report_b0001_e0001_r0001.csv`. If the base name has no extension, `.csv` is
added. Each generated file has its own header.

Files are created lazily. On the first use of a physical path in a run, an
existing file is overwritten; returning to the same path later in that run
continues the same destination without another BOM or header. Output is never
appended across separate runs.

Output is written directly rather than through an atomic replacement. A failed
or cancelled run can therefore leave a valid partial file containing complete
result sets written before the failure. SQL execution may also have completed
and committed side effects before a serialization or file-write failure is
detected.

An output path, permission, sharing, directory, encoding, schema, flush, or
close failure stops execution regardless of `:ON ERROR` policy and exits
`tiger-sqlcmd` with the dedicated output-failure code `8`.

For the reusable engine API and the precise directive grammar, see
[Output routing and CSV files](engine.md#output-routing-and-csv-files).
