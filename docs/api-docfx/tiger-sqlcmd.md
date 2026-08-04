# TigerSqlCmd concepts and usage

`tiger-sqlcmd` is TigerQuery's command-line SQL Server client. It is a first-class
product for running inline queries and `.sql` files, managing reusable connections, and
using TigerQuery's sqlcmd-compatible parser without embedding the library in an
application.

TigerQuery is the parser and execution engine. TigerSqlCmd supplies the executable,
connection-store integration, interactive prompts, console rendering, output routing,
logging, and stable process exit codes. Use the libraries when building an application;
use `tiger-sqlcmd` when a shell command is the right interface.

For disposable databases and session-scoped automation, continue with
[TigerSqlCmd E2E scenarios](tiger-sqlcmd-e2e.md).

## Installation

### Windows installer

Download `TigerSqlCmdSetup_<version_with_underscores>.exe` from the matching
[GitHub Release](https://github.com/rkozlowski/TigerQuery/releases). Setup requires
administrator elevation, installs machine-wide under
`C:\Program Files\ItTiger\TigerSqlCmd`, places the CLI in its `cli` subdirectory, and
adds that directory to the system PATH. Open a new terminal after installation:

```console
tiger-sqlcmd --version
tiger-sqlcmd --help
```

The installer requires the x64 .NET 10 runtime. Interactive setup can download it
directly from Microsoft with approval. For unattended setup, install the runtime first or
explicitly add `/INSTALLDOTNET`:

```powershell
$installer = Get-Item .\TigerSqlCmdSetup_*.exe
$setup = Start-Process -FilePath $installer.FullName -Wait -PassThru -ArgumentList @(
  '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-'
)
if ($setup.ExitCode -ne 0) { throw "Setup failed: $($setup.ExitCode)" }
```

### Global .NET tool

The .NET tool requires a **.NET 10 SDK** at installation time:

```console
dotnet tool install --global ItTiger.TigerSqlCmd
tiger-sqlcmd --version
```

If an older SDK selects the package, it can misleadingly report that
`DotnetToolSettings.xml` is missing even though the file is present. Check
`dotnet --version` and any applicable `global.json`, then repeat the install with a .NET
10 SDK.

Update or uninstall the global tool with:

```console
dotnet tool update --global ItTiger.TigerSqlCmd
dotnet tool uninstall --global ItTiger.TigerSqlCmd
```

### Repository-local .NET tool

A local tool manifest pins the CLI for a repository without changing the machine-wide or
global tool installation:

```console
dotnet new tool-manifest
dotnet tool install --local ItTiger.TigerSqlCmd
dotnet tool run tiger-sqlcmd --help
```

Commit `.config/dotnet-tools.json` when the repository should share that pin. Installing
or restoring a local tool also requires a .NET 10 SDK.

## Command structure

Run `tiger-sqlcmd --help` at the root or append `--help` to a command path. The shipped
command groups and commands are:

| Command | Purpose |
| --- | --- |
| `tiger-sqlcmd -c <name> -q <sql>` | Friendly inline-query command; prompts for missing required values when interactive. |
| `tiger-sqlcmd run` | Inline SQL or file execution with sqlcmd mode, variables, output routing, verbosity, and logging. |
| `tiger-sqlcmd connection` | Manage saved connections. The group name is singular. |
| `tiger-sqlcmd e2e` | Create, drop, and clean session-scoped E2E resources. |

The option `--non-interactive` is a primary supported mode for scripts, CI jobs, build
pipelines, coding agents, and other unattended automation. It disables prompts; omitted
required values then fail with a usage or validation error instead of waiting for input.
Place application-wide options after the command path and any positional argument, just
like other TigerCli options.

### Interactive prompting

Without `--non-interactive`, the friendly default command prompts for a saved connection
and SQL query when either is missing. Connection `add` and `edit` prompt for applicable
fields and mask a SQL password. The advanced `run` command requires `--file` or `--query`
on the command line, although it can prompt for a missing connection. Interactive mode is
guided command completion, not a persistent SQL REPL; each invocation performs one
command and exits.

## Saved connections

### Add and inspect

Add an Integrated-authentication connection, then list and show it:

```console
tiger-sqlcmd connection add local --server sql01 --database AppDb
tiger-sqlcmd connection list
tiger-sqlcmd connection show local
```

`connection add <name>` requires a usable server value, either directly, through a
reference, or as part of a full connection-string reference. The default authentication
is `Integrated`; the default encryption mode is `Mandatory`. Common field options include
`--database`, `--encrypt`, `--trust-server-certificate`, `--application-intent`, pooling
options, and repeatable `--opt <key=value>` values.

`connection list` supports repeatable `--metadata`, `--metadata-set`, and
`--metadata-not-set` filters. `connection show` displays the profile and a separate
metadata table. Both commands describe external references without reading their values,
and they redact literal passwords, access tokens, and complete connection strings.

### Edit and delete

Edit only the fields that should change; omitted values are preserved:

```console
tiger-sqlcmd connection edit local --database Reporting --application-intent ReadOnly
tiger-sqlcmd connection delete local
```

`connection delete` refuses an owning E2E connection because deleting that record would
discard the evidence that authorizes safe database cleanup. Use `e2e drop` or `e2e
cleanup` for those records. It can delete ordinary connections and non-owning E2E clones.

### Clone for an E2E session

The cloning command is specifically `connection clone-e2e`; there is no general
`connection clone` alias. It copies authentication and unresolved references within the
same selected store, targets a pre-existing database, and marks the new connection as
non-owning:

```console
tiger-sqlcmd connection clone-e2e source --database ExistingDb --session-id 11111111-2222-3333-4444-555555555555 --name-part readonly
```

See [TigerSqlCmd E2E scenarios](tiger-sqlcmd-e2e.md) before using it in automation.

## Selecting the connection store

Each process reads and writes exactly one JSON store. Selection has strict precedence:

1. `--tq-connection-store-file <path>` on that invocation;
2. `TIGERQUERY_CONNECTION_STORE_FILE`;
3. TigerSqlCmd's application default.

The default is the shared per-user store
`%APPDATA%\ItTiger.net\sqlserver-connections.json` on Windows and
`~/.config/ItTiger.net/sqlserver-connections.json` on other supported platforms.

```powershell
tiger-sqlcmd connection list --tq-connection-store-file C:\agent\connections.json
$env:TIGERQUERY_CONNECTION_STORE_FILE = 'C:\agent\connections.json'
tiger-sqlcmd connection list
```

An unusable higher-priority path fails the run; TigerQuery does not silently fall back to
a lower-priority store. Supply `--tq-connection-store-file=<path>` when a value starts
with `-`. Do not put the option before the command path.

## Authentication and secrets

### Integrated authentication

Integrated authentication uses the operating-system identity of the `tiger-sqlcmd`
process. It is the default and does not use SQL username or password fields:

```console
tiger-sqlcmd connection add build-db --server sql01 --database BuildDb --non-interactive
```

The account running a CI service, build agent, or coding agent therefore needs the
intended SQL Server permissions; a developer's interactive login is not inherited by a
different service account.

### SQL authentication

Select `--authentication SqlPassword`. In an interactive terminal, TigerSqlCmd can prompt
for the password and masks the input. The `--password` setting cannot be supplied on the
command line. This prevents a literal password appearing in shell history, process lists,
job definitions, and agent transcripts.

For non-interactive work, use external references:

```powershell
$env:TQ_SQL_SERVER = 'sql01'
$env:TQ_SQL_USER = 'build_login'
tiger-sqlcmd connection add ci --non-interactive `
  --authentication SqlPassword `
  --server-reference '{"Source":"EnvironmentVariable","Name":"TQ_SQL_SERVER"}' `
  --username-reference '{"Source":"EnvironmentVariable","Name":"TQ_SQL_USER"}' `
  --password-reference '{"Source":"File","Path":"C:\\secrets\\sql-password","Format":"Text"}' `
  --database BuildDb
```

The five supported reference options are `--server-reference`,
`--database-reference`, `--username-reference`, `--password-reference`, and
`--connection-string-reference`. Their JSON shapes are:

```json
{"Source":"EnvironmentVariable","Name":"VARIABLE_NAME"}
{"Source":"File","Path":"/run/secrets/value","Format":"Text"}
{"Source":"File","Path":"/run/secrets/values.json","Format":"Json","Key":"property"}
```

Text files are UTF-8 and read whole without trimming. JSON references select an exact,
case-sensitive top-level property whose value must be a string. A reference option accepts
an object, not a JSON string literal. A full connection-string reference must be used
alone; it cannot be mixed with server, database, authentication, encryption, credential,
pooling, or free-form connection fields.

On Windows, an interactively entered literal SQL password is protected with current-user,
current-machine DPAPI. That stored value is not portable to another user, agent, machine,
or container. External references are the portable automation path.

## Running SQL

### Inline SQL

The default command is the shortest path for one query:

```console
tiger-sqlcmd -c local -q "SELECT @@SERVERNAME AS ServerName;"
```

For unattended execution, make the mode explicit:

```console
tiger-sqlcmd -c local -q "SELECT 1 AS Healthy;" --non-interactive
```

### SQL files and advanced runs

`run` requires exactly one of `--query` or `--file`:

```console
tiger-sqlcmd run -c local -q "SELECT DB_NAME() AS DatabaseName;" --non-interactive
tiger-sqlcmd run -c local -f deploy.sql --non-interactive
```

The default `run` mode is `SqlCmd`. It recognizes TigerQuery's supported
sqlcmd-compatible behavior: `GO` batch separators and repeat counts, `$(name)` variable
expansion, `:setvar`, `:on error exit`, `:on error ignore`, `:Out`, and `:Error`.
TigerQuery deliberately documents its compatibility rather than claiming every feature or
alias of Microsoft's `sqlcmd`.

Supply initial variables with repeatable `-v`/`--var`:

```console
tiger-sqlcmd run -c local -f deploy.sql -v Environment=Test -v BuildNumber=42 --non-interactive
```

In `SqlCmd` mode, a script's matching `:setvar` replaces an initial CLI value. In
`SqlCmdEx`, CLI values are protected and a matching `:setvar` is ignored; unrelated
script-local variables still work:

```console
tiger-sqlcmd run -c local -f deploy.sql --mode SqlCmdEx -v TargetDatabase=ControlledDb --non-interactive
```

Use `--mode Normal` only when sqlcmd directives should be sent as ordinary SQL text.

## Output, diagnostics, and automation

Without routing options, result sets are rendered as console tables and SQL messages use
the console renderer. Route result sets to CSV with `-o`/`--output`:

```console
tiger-sqlcmd run -c local -f report.sql -o report.csv --non-interactive
tiger-sqlcmd run -c local -f report.sql -o report.csv --output-mode FilePerResultSet --non-interactive
```

`--output-mode SingleFile` is the default. `FilePerResultSet` creates deterministic names
containing batch, execution, and result-set coordinates. `--output-encoding` accepts a
.NET encoding name. `-e`/`--error-output` routes SQL error messages, while
`--out-behavior ResultSetsAndNormalMessages` also sends normal SQL messages to the
`.messages.log` companion of the result path. `:Out` and `:Error` can change routes in a
script. See [Result output routing](../features/result-output-routing.md) for the exact
CSV, overwrite, naming, encoding, and partial-file contract.

Use `--verbosity`, `--log-file`, and `--log-level` for operational diagnostics. Saved
connection strings and resolved secrets are never printed. `--no-color` is useful when a
job captures output, and `--help-env` lists recognized environment variables.

Process exit codes are the automation contract. Run `tiger-sqlcmd --help-errors` for the
authoritative table. Important values are `0` success, `1` batch failure, `2` fatal SQL or
connection-domain validation, `3` cancellation, `4` connection failure/not found, `5`
parse error/already exists, `6` unhandled exception, `7` fatal exception, `8` output
failure, and `20` invalid or incomplete command-line usage. A failed or cancelled run can
leave valid partial output files and SQL side effects that completed before the failure;
automation must treat a nonzero exit as a failed step and retain diagnostics.
