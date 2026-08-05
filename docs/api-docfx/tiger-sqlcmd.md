# TigerSqlCmd concepts and usage

`tiger-sqlcmd` is TigerQuery's command-line SQL Server client. It is a first-class
product for running inline queries and `.sql` files, managing reusable connections, and
using TigerQuery's sqlcmd-compatible parser without embedding the library in an
application.

TigerQuery is the parser and execution engine. TigerSqlCmd supplies the executable,
connection-store integration, interaction policy, console rendering, output routing,
logging, and stable process exit codes. Use the libraries when building an application;
use `tiger-sqlcmd` when a shell command is the right interface.

For disposable databases and session-scoped automation, continue with
[TigerSqlCmd E2E scenarios](tiger-sqlcmd-e2e.md).

## One Command Model, Multiple Interaction Modes

TigerSqlCmd follows TigerCli's defining model: one command model serves both guided
people and unattended callers. The same command path selects the same business operation
in both modes. Interaction mode changes prompting and presentation policy; it does not
change command semantics, SQL behavior, or safety checks.

Normal TigerSqlCmd execution uses TigerCli **semi-interactive mode**. One command is
parsed and executed, and TigerCli may use a command menu, prompt for eligible missing
input, load provider-backed choices, request confirmation, or render activity/progress UI.
This is guided completion of one invocation, not a persistent SQL shell.

Adding `--non-interactive` selects TigerCli **non-interactive mode** for that same
command. It disables command menus, prompts, confirmations, and keyboard input. Missing
promptable input fails clearly instead of blocking. Parsing, binding, framework and
custom validation, provider validation, command execution, structured output,
diagnostics, and process-exit mapping still occur. An activity body still executes, but
without its interactive dialog, spinner, buttons, or progress display.

Use non-interactive mode for scripts, CI pipelines, scheduled jobs, redirected execution,
and AI or coding agents—anywhere an invocation must never pause for a person. See the
authoritative TigerCli [README](https://github.com/rkozlowski/TigerCli/blob/main/README.md#one-command-model-multiple-interaction-modes)
and [interaction modes guide](https://github.com/rkozlowski/TigerCli/blob/main/docs/guides/interaction-modes.md)
for the framework model.

### TigerSqlCmd consequences

The `connection`, SQL execution, and E2E commands are not split into human and automation
APIs. Call the same command and add `--non-interactive` when the caller must supply every
required choice explicitly:

| Operation | Semi-interactive | Non-interactive |
| --- | --- | --- |
| Run SQL | `tiger-sqlcmd run -c local -q "SELECT 1;"` | `tiger-sqlcmd run -c local -q "SELECT 1;" --non-interactive` |
| Inspect a connection | `tiger-sqlcmd connection show local` | `tiger-sqlcmd connection show local --non-interactive` |
| Create an E2E resource | `tiger-sqlcmd e2e create --session-id <guid> --name-part smoke` | `tiger-sqlcmd e2e create --session-id <guid> --name-part smoke --non-interactive` |

When a command permits connection prompting, semi-interactive mode can present saved
connections; non-interactive mode must resolve the connection from an explicit
`-c`/`--connection` value. A SQL-authentication password or other secret is never prompted
for in non-interactive mode. Unattended SQL authentication therefore requires an
[external value reference](#sql-authentication), not a literal password or an expected
secret prompt. Missing connections, choices, or secret sources fail immediately instead
of hanging.

Once input has been resolved and validated, SQL parsing and execution, output routing,
error handling, E2E ownership rules, and every destructive-operation guard are unchanged.
Unattended callers must inspect the process exit code and retain standard error, routed
diagnostics, and logs; console text alone is not a success contract.

`--non-interactive` is an execution option and follows normal TigerCli grammar. Place it
after the command path and any required positional arguments—for example,
`tiger-sqlcmd connection show local --non-interactive`. App-wide options such as
`--tq-connection-store-file` follow the same placement rule.

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
| `tiger-sqlcmd exec` | Run an external program against a saved connection, handing it the resolved connection string. |
| `tiger-sqlcmd connection` | Manage saved connections. The group name is singular. |
| `tiger-sqlcmd e2e` | Create, drop, and clean session-scoped E2E resources. |

The friendly default command can prompt in semi-interactive mode for a saved connection
and SQL query when either is missing. Connection `add` and `edit` can prompt for
applicable fields and mask a SQL password. The advanced `run` command requires `--file`
or `--query` on the command line, although it can prompt for a missing connection.

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

Select `--authentication SqlPassword`. In semi-interactive mode, TigerSqlCmd can prompt
for the password and masks the input. Non-interactive mode never opens that secret prompt.
The `--password` setting cannot be supplied on the command line. This prevents a literal
password appearing in shell history, process lists, job definitions, and agent
transcripts.

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

The default command is the shortest path for one query. These two invocations select the
same query operation; the second only changes the interaction policy:

```console
tiger-sqlcmd -c local -q "SELECT @@SERVERNAME AS ServerName;"
tiger-sqlcmd -c local -q "SELECT @@SERVERNAME AS ServerName;" --non-interactive
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

### Batch failure and the `run` exit code

**Any SQL batch failure makes `run` return a nonzero exit code.** A batch fails when SQL
Server reports an error of severity 11 or higher for it, which includes the severity 11-16
errors the provider delivers as messages instead of throwing — `RAISERROR(..., 16, ...)`,
`THROW`, a failed `DROP DATABASE`, and ordinary compilation errors such as an invalid
object name. Reaching the end of the script is not success.

`:on error exit` and `:on error ignore` decide **how much of the script runs**, not what
the process reports:

| Script | Later batches | Exit code |
| --- | --- | --- |
| Every batch succeeds | All run | `0` |
| A batch fails, default or `:on error ignore` | Later batches still run | `1` (batch failed) |
| A batch fails under `:on error exit` | Not started | `1` (batch failed) |
| A fatal server error | Not started | `2` |

A later successful batch never clears an earlier failure, and successfully writing a
routed result file never masks a SQL failure. Both modes, `SqlCmd` and `SqlCmdEx`, behave
identically here.

Scripts and agents must branch on the exit code. SQL Server's error text stays on the
console exactly as before — it is diagnostics for a human or a log, not the success
contract, and it must never be parsed to decide whether a run worked:

```powershell
& tiger-sqlcmd run -c local -f deploy.sql --non-interactive --no-color
if ($LASTEXITCODE -ne 0) { throw "deploy.sql failed with exit code $LASTEXITCODE." }
```

## Running an external tool: `exec`

### Why the command exists

A saved connection is a TigerQuery concept. Plenty of useful tools — schema comparers,
migration runners, data loaders, reporting utilities, `SqlPackage` — need a SQL Server
connection string and have no idea what a TigerSqlCmd connection name is. Without `exec`,
the only way to bridge that gap is to build the connection string somewhere else, which
means duplicating the store, the external references, and the authentication rules that
`tiger-sqlcmd` already implements, and usually parking the result in a variable or a file.

`exec` closes the gap without widening the exposure. It resolves the named connection with
exactly the same store selection, external-reference resolution, authentication handling,
validation, and non-interactive policy as `run`, then hands the resulting connection string
to one child process and nothing else:

```console
tiger-sqlcmd exec --connection local --connection-string-env DB_CONNECTION -- my-tool --report
```

The command is deliberately generic. It knows nothing about any particular tool, and
TigerQuery adds no product-specific behavior for one.

### Direct execution, not shell execution

`exec` starts the executable directly. There is no `cmd.exe`, no `/bin/sh`, and no implied
shell of any other kind. Nothing between `tiger-sqlcmd` and the child re-parses, word-splits,
expands, globs, or interprets quoting: the child receives exactly the argument tokens that
appeared after `--`, with only the `{connection-string}` substitution applied.

Consequences worth stating explicitly:

- Arguments containing spaces are passed as single arguments; they are not re-split.
- Executable paths containing spaces work without extra quoting.
- `*.sql`, `$HOME`, `%PATH%`, backticks, `&&`, `|`, and `>` are literal argument text, not
  operators. If a pipeline or redirection is wanted, write it in the calling shell around
  `tiger-sqlcmd exec`, not inside the child command line.
- Shell built-ins are not executables and cannot be run.

Everything after the first `--` is the child command line: the first token is the executable
and the rest are its arguments. A later `--` is an ordinary child argument. The separator is
recognized only for `exec`; every other TigerSqlCmd command treats `--` exactly as it always
has.

The child inherits standard input, standard output, standard error, the caller's working
directory, and the caller's environment. Output is inherited, not captured and replayed, so
the child's console behavior, interleaving, and progress rendering are unchanged and there is
no redirected-pipe buffer to deadlock on.

### Argument substitution

The exact token `{connection-string}` in any child argument is replaced by the resolved
connection string:

```console
tiger-sqlcmd exec --connection local -- my-tool --target={connection-string} --report
```

The rules are narrow on purpose:

- Only the exact token `{connection-string}` is substituted. `{Connection-String}`,
  `{connection-string }`, and `${connection-string}` are ordinary text.
- It is substituted inside a larger argument, as in `--target={connection-string}`.
- Every occurrence in every argument is substituted.
- All other argument text is preserved byte for byte. No environment expansion, no quoting
  interpretation, no template language.
- The placeholder is **not** substituted into the executable itself; that is rejected.

### Environment-variable injection

`--connection-string-env <variable-name>` sets that variable to the resolved connection
string for the child process only:

```console
tiger-sqlcmd exec --connection local --connection-string-env DB_CONNECTION -- my-tool --report
```

The variable name must be a portable identifier: a letter or underscore followed by letters,
digits, or underscores. The child inherits the rest of the caller's environment unchanged,
and the `tiger-sqlcmd` process's own environment is not modified — the value exists only in
the child's environment block. TigerSqlCmd never prints it.

### Choosing a handoff, and using both

At least one handoff is required. A run with no `{connection-string}` placeholder and no
`--connection-string-env` fails as invalid arguments, because the child would otherwise start
without the value the command exists to deliver.

Both together are allowed and are applied together when explicitly requested: the placeholder
is substituted in the arguments *and* the variable is set. This suits a tool that reads the
variable for one purpose and takes a connection string argument for another.

**Prefer environment injection when the child supports it.** Argument substitution puts the
connection string — including any password it carries — into the child process's command
line, where it is visible to anyone who can inspect processes on the machine: `ps`, Task
Manager, `Get-CimInstance Win32_Process`, process-auditing agents, and crash or diagnostic
dumps. That is accepted, documented behavior of the placeholder, not a defect, and it is the
only option for a tool that has no environment-variable input. An environment variable is
narrower but is not secret either: it is readable by anything that can inspect the child
process's environment, and the child may itself log or re-export it. `exec` reduces exposure
compared with putting the connection string in your shell, a script, or a file; it does not
make the value private.

### Non-interactive use

`exec` follows the same interaction model as every other command. Add `--non-interactive` for
scripts, CI jobs, and agents; a missing `--connection` then fails immediately instead of
prompting, and a SQL password is never prompted for, so unattended SQL authentication needs
an [external value reference](#sql-authentication). The store is selected by
`--tq-connection-store-file`, then `TIGERQUERY_CONNECTION_STORE_FILE`, then the application
default, exactly as for `run`. Resolved secrets are never written back to the store.

An invalid handoff configuration is reported before the connection is resolved, so a bad
command line never reads an external reference or a stored secret.

### Exit codes

The child's exit code is returned unchanged. That is the point of the command: a wrapper that
remapped exit codes would break every caller that already checks the tool's own contract.

Two codes are TigerSqlCmd's own and are produced before the child runs or instead of it:

| Exit code | Meaning |
| --- | --- |
| `20` | The handoff configuration was invalid: no `--`, no executable after `--`, no handoff method, an invalid environment-variable name, or the placeholder in the executable. |
| `4` | The saved connection could not be resolved. |
| `21` | The child executable could not be started at all — not found, not executable, or refused by the operating system. |
| `2` | Framework validation rejected the command line, such as a missing `--connection` in non-interactive mode. |

Because the child's code passes through unchanged, a child may itself return `2`, `4`, `20`,
or `21`. There is no way to distinguish those from TigerSqlCmd's own codes by number alone;
TigerSqlCmd writes its own failures to standard error, and a caller that needs certainty can
have the child use a distinct code of its own.

### Diagnostics and redaction

TigerSqlCmd never prints the resolved connection string, and it never prints the resolved
child command line. It writes nothing at all to standard output on success — everything the
caller sees comes from the child.

When a start failure is reported, the child command line is shown with the *unsubstituted*
placeholder, so the resolved value cannot appear:

```text
Could not start the child executable 'my-tool': ...
Child command line: my-tool /TargetConnectionString:{connection-string}
```

Only values TigerSqlCmd resolved are guaranteed absent from that line. Text you typed into an
argument yourself is echoed as typed, so do not paste a literal connection string into a child
argument — use the placeholder or the environment variable.

Errors written by the child itself are outside TigerSqlCmd's control and pass through
untouched, including anything the child chooses to print about its own connection string.

### Cancellation

On a console, Ctrl+C reaches the child as well as `tiger-sqlcmd`. TigerSqlCmd absorbs the
first Ctrl+C so it stays alive long enough to report the child's real exit code, and never
kills the child itself. A second Ctrl+C is passed through and ends `tiger-sqlcmd`, so a child
that ignores Ctrl+C cannot hold the caller indefinitely.

### An illustration with SqlPackage

`SqlPackage` is one example of a tool that takes a connection string and cannot take a saved
connection name. It is used here purely as an illustration; TigerQuery has no DacFx
integration and no `SqlPackage`-specific behavior.

```console
tiger-sqlcmd exec --connection local --non-interactive -- sqlpackage /Action:Script /SourceFile:App.dacpac /TargetConnectionString:{connection-string} /OutputPath:deploy.sql
```

Every `SqlPackage` switch above is passed through verbatim; TigerSqlCmd only substituted the
one placeholder. `SqlPackage` reads target connection strings from arguments, so this example
accepts the process-inspection exposure described above. Where a tool offers an
environment-variable input instead, use `--connection-string-env`.

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

Process exit codes and diagnostics are the automation contract. `run` returns `0` only
when every batch it started succeeded; see
[Batch failure and the `run` exit code](#batch-failure-and-the-run-exit-code). Run
`tiger-sqlcmd --help-errors` for the authoritative table. Important values are `0`
success, `1` batch failure, `2` fatal SQL or connection-domain validation, `3`
cancellation, `4` connection failure/not found, `5` parse error/already exists, `6`
unhandled exception, `7` fatal exception, `8` output failure, `20` invalid or
incomplete command-line usage, and `21` an `exec` child that could not be started.
`exec` otherwise returns its child's exit code unchanged; see
[Exit codes](#exit-codes). A failed or cancelled run can leave valid partial output
files and SQL side effects that completed before the failure; automation must treat a
nonzero exit as a failed step and retain diagnostics.
