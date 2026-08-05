# TigerSqlCmd E2E scenarios

This guide is for operators, CI jobs, build pipelines, and coding agents that need safe,
session-scoped SQL Server resources. The underlying safety and ownership contracts are
documented separately in
[E2E connection stores and database lifecycle](../features/e2e-connection-stores.md).

The core rule is simple: SQL Server reachability is not authorization. TigerQuery uses an
exact bootstrap profile, protected metadata, an exact session GUID, and durable connection
records before it will create or drop a database.

## One Command Model, Multiple Interaction Modes

TigerSqlCmd E2E commands are not a separate automation API. The same `e2e create`,
`connection clone-e2e`, `e2e drop`, and `e2e cleanup` command implementations serve a
person working with TigerCli's guided semi-interactive presentation and an unattended
caller using `--non-interactive`.

Interaction mode changes only whether TigerCli may prompt or render interactive activity
UI. It does not change which E2E operation runs. Use `--non-interactive` for CI pipelines,
scripts, scheduled jobs, and coding agents so a missing choice fails immediately instead
of waiting for menus, prompts, confirmations, or keyboard input. The complete TigerSqlCmd
model is described in
[One Command Model, Multiple Interaction Modes](tiger-sqlcmd.md#one-command-model-multiple-interaction-modes).

| Operation | Semi-interactive | Unattended |
| --- | --- | --- |
| Create an owned database and connection | `tiger-sqlcmd e2e create --session-id <guid> --name-part smoke` | `tiger-sqlcmd e2e create --session-id <guid> --name-part smoke --non-interactive` |
| Clone for an existing database | `tiger-sqlcmd connection clone-e2e source --database ExistingDb --session-id <guid> --name-part readonly` | `tiger-sqlcmd connection clone-e2e source --database ExistingDb --session-id <guid> --name-part readonly --non-interactive` |
| Drop one exact resource | `tiger-sqlcmd e2e drop --connection <exact-name> --session-id <guid>` | `tiger-sqlcmd e2e drop --connection <exact-name> --session-id <guid> --non-interactive` |
| Clean one exact session | `tiger-sqlcmd e2e cleanup --session-id <guid>` | `tiger-sqlcmd e2e cleanup --session-id <guid> --non-interactive` |

For repeatable unattended work, select one store explicitly, keep credentials in external
value references, and generate and retain one `--session-id` for the complete workflow.
Those inputs make the same command model deterministic; they do not weaken it.
Non-interactive mode preserves exact session matching, protected ownership metadata,
`ittiger.e2e.database.allow-drop`, exact database-name checks, and `_TQ_E2E_` prefix
validation. It also preserves SQL execution behavior, diagnostics, and process exit
codes.

## Bootstrap connection and permissions

TigerSqlCmd's expected bootstrap connection name is `tiger-sqlcmd-e2e`. It must contain
all three exact, case-sensitive metadata entries:

```text
ittiger.e2e.enabled=true
ittiger.e2e.bootstrap=true
ittiger.e2e.allow-database-create=true
```

Do not add these keys with generic `--metadata`; the reserved namespace rejects that.
Create the profile through `connection add-e2e-bootstrap --allow-database-create`.

Use a dedicated non-production SQL Server and a dedicated login or service identity. The
principal needs permission to connect to `master`, create a database, connect to each
database it creates, and drop those owned databases. `CREATE ANY DATABASE` plus ownership
of the created database is a narrower starting point than `sysadmin`, but SQL Server
cannot restrict that server permission to TigerQuery's name prefix. Isolation of the SQL
Server instance remains the primary boundary. A source connection used only for a
pre-existing read-only database needs no create/drop grant; give it only the read access
the scenario requires.

## Regular and isolated stores

With no override, TigerSqlCmd uses its regular per-user store. This is convenient for a
developer workstation:

```powershell
Remove-Item Env:TIGERQUERY_CONNECTION_STORE_FILE -ErrorAction Ignore
tiger-sqlcmd connection add-e2e-bootstrap --non-interactive `
  --server sql01 --allow-database-create
```

For CI, containers, and parallel agents, select a job-specific writable store. The
override chooses a store; it does not enable E2E work. That isolated store must contain
its own correctly named and authorized bootstrap:

```powershell
$env:TIGERQUERY_CONNECTION_STORE_FILE = 'C:\agent\state\job-42\connections.json'
tiger-sqlcmd connection add-e2e-bootstrap --non-interactive `
  --server sql01 --allow-database-create
```

The explicit `--tq-connection-store-file <path>` option outranks the environment
variable, which outranks the application default. Use the same selected store for
bootstrap creation, E2E creation, SQL runs, and cleanup.

## Non-interactive bootstrap with external secrets

Literal passwords are not accepted on the command line. For SQL authentication, keep
the password outside argv and the writable connection store:

```powershell
$env:TIGERQUERY_CONNECTION_STORE_FILE = 'C:\agent\state\job-42\connections.json'
$env:TQ_E2E_SQL_SERVER = 'sql01'
tiger-sqlcmd connection add-e2e-bootstrap --non-interactive `
  --authentication SqlPassword `
  --server-reference '{"Source":"EnvironmentVariable","Name":"TQ_E2E_SQL_SERVER"}' `
  --username-reference '{"Source":"File","Path":"C:\\secrets\\sql-auth.json","Format":"Json","Key":"username"}' `
  --password-reference '{"Source":"File","Path":"C:\\secrets\\sql-password","Format":"Text"}' `
  --allow-database-create
```

Alternatively, reference one complete connection string and supply no individual
connection fields:

```powershell
tiger-sqlcmd connection add-e2e-bootstrap --non-interactive `
  --connection-string-reference '{"Source":"EnvironmentVariable","Name":"TQ_E2E_SQL_CONNECTION_STRING"}' `
  --allow-database-create
```

File references are resolved when SQL is actually used. Text is read whole without
trimming; a trailing newline is part of a password. A JSON reference selects an exact,
case-sensitive top-level string property.

## Session IDs and names

Every `e2e create`, `e2e drop`, and `e2e cleanup` call requires a non-empty GUID through
`--session-id`. `connection clone-e2e` requires the same correlation value. Generate one
per job or agent and retain it until cleanup finishes.

PowerShell:

```powershell
$sessionId = [Guid]::NewGuid().ToString('D')
```

POSIX shell with `uuidgen`:

```bash
session_id=$(uuidgen | tr '[:upper:]' '[:lower:]')
```

`e2e create --name-part smoke` uses `smoke` for both names. Override them separately with
`--database-name-part` and `--connection-name-part`:

```console
tiger-sqlcmd e2e create --session-id 11111111-2222-3333-4444-555555555555 --name-part smoke --database-name-part schema-tests --connection-name-part agent-7
```

Generated database names are
`_TQ_E2E_<database-part>_<random-suffix>`; generated connection names are
`E2E-<connection-part>-<random-suffix>`. A paired create uses the same suffix for both.
Prefixes are fixed. Name parts are sanitized and do not become ownership evidence.

## Disposable database: complete PowerShell workflow

`e2e create` always creates a database and its paired owning connection. It prints both
exact names. This fresh-job example selects one isolated store explicitly on every
command, creates its authorized bootstrap from an externally supplied connection-string
reference, captures the generated connection name, runs SQL non-interactively, and
guarantees exact-session cleanup. The job's secret manager must set
`TQ_E2E_SQL_CONNECTION_STRING`; its value never appears in argv or the store.

```powershell
$ErrorActionPreference = 'Stop'
$storeFile = 'C:\agent\state\job-42\connections.json'
$sessionId = [Guid]::NewGuid().ToString('D')

if ([string]::IsNullOrWhiteSpace($env:TQ_E2E_SQL_CONNECTION_STRING)) {
  throw 'TQ_E2E_SQL_CONNECTION_STRING must be supplied by the job secret manager.'
}

& tiger-sqlcmd connection add-e2e-bootstrap `
  --connection-string-reference '{"Source":"EnvironmentVariable","Name":"TQ_E2E_SQL_CONNECTION_STRING"}' `
  --allow-database-create --non-interactive --no-color `
  --tq-connection-store-file $storeFile
if ($LASTEXITCODE -ne 0) { throw "E2E bootstrap failed with exit code $LASTEXITCODE." }

try {
  $createOutput = @(& tiger-sqlcmd e2e create `
    --session-id $sessionId --name-part ci-smoke `
    --non-interactive --no-color --tq-connection-store-file $storeFile)
  if ($LASTEXITCODE -ne 0) { throw "E2E create failed with exit code $LASTEXITCODE." }
  $createOutput | Write-Host

  $connectionName = $createOutput |
    Select-String '^Created E2E connection (?<name>E2E-[A-Za-z0-9_-]+)\.$' |
    ForEach-Object { $_.Matches[0].Groups['name'].Value } |
    Select-Object -First 1
  if ([string]::IsNullOrWhiteSpace($connectionName)) {
    throw 'TigerSqlCmd did not report the created E2E connection name.'
  }

  & tiger-sqlcmd run --connection $connectionName `
    --query 'CREATE TABLE dbo.Health(Id int NOT NULL); SELECT DB_NAME() AS DatabaseName;' `
    --mode SqlCmdEx --non-interactive --no-color `
    --tq-connection-store-file $storeFile
  if ($LASTEXITCODE -ne 0) { throw "SQL run failed with exit code $LASTEXITCODE." }
}
finally {
  & tiger-sqlcmd e2e cleanup --session-id $sessionId `
    --non-interactive --no-color --tq-connection-store-file $storeFile
  if ($LASTEXITCODE -ne 0) {
    Write-Error "E2E cleanup was incomplete for session $sessionId."
  }
}
```

The bootstrap is add-only. Provision it once outside the per-session portion when a job
reuses an isolated store; do not ignore an `already exists` result from the fresh-store
workflow.

## Disposable database: complete POSIX shell workflow

The same command model works in a supported POSIX shell. This `sh` workflow has the same
fresh-store and secret-manager assumptions as the PowerShell version. It preserves the
first failing exit status unless cleanup is the only failure, and it always emits
TigerSqlCmd diagnostics:

```bash
#!/usr/bin/env sh
set -eu

store_file=/workspace/state/job-42/connections.json
session_id=$(uuidgen | tr '[:upper:]' '[:lower:]')
: "${TQ_E2E_SQL_CONNECTION_STRING:?must be supplied by the job secret manager}"

cleanup() {
  status=$?
  trap - EXIT
  cleanup_status=0
  tiger-sqlcmd e2e cleanup --session-id "$session_id" \
    --non-interactive --no-color \
    --tq-connection-store-file "$store_file" || cleanup_status=$?
  if [ "$status" -eq 0 ]; then status=$cleanup_status; fi
  exit "$status"
}
trap cleanup EXIT

tiger-sqlcmd connection add-e2e-bootstrap \
  --connection-string-reference \
    '{"Source":"EnvironmentVariable","Name":"TQ_E2E_SQL_CONNECTION_STRING"}' \
  --allow-database-create --non-interactive --no-color \
  --tq-connection-store-file "$store_file"

create_output=$(tiger-sqlcmd e2e create \
  --session-id "$session_id" --name-part ci-smoke \
  --non-interactive --no-color \
  --tq-connection-store-file "$store_file")
printf '%s\n' "$create_output"

connection_name=$(printf '%s\n' "$create_output" |
  sed -n 's/^Created E2E connection \(E2E-[A-Za-z0-9_-]*\)\.$/\1/p' |
  sed -n '1p')
if [ -z "$connection_name" ]; then
  echo 'TigerSqlCmd did not report the created E2E connection name.' >&2
  exit 1
fi

tiger-sqlcmd run --connection "$connection_name" \
  --query 'CREATE TABLE dbo.Health(Id int NOT NULL); SELECT DB_NAME() AS DatabaseName;' \
  --mode SqlCmdEx --non-interactive --no-color \
  --tq-connection-store-file "$store_file"
```

The owning connection records the exact database and
`ittiger.e2e.database.allow-drop=true`. `e2e cleanup` selects only protected,
non-bootstrap records whose stored session ID exactly equals the supplied canonical GUID.
For an individual resource, use:

```console
tiger-sqlcmd e2e drop --connection E2E-ci-smoke-<exact-suffix> --session-id 11111111-2222-3333-4444-555555555555 --non-interactive
```

If the exact owned database exists, TigerQuery drops it and then removes the connection.
If it is already absent, TigerQuery removes the connection. If the drop fails, the
owning record remains so the same exact operation can be retried.

## Handing the session connection to an external tool

A session database is usually not the end of the job. Something has to deploy a schema into
it, and that something is often an external tool that takes a connection string and cannot
take a TigerSqlCmd connection name. `tiger-sqlcmd exec` bridges that gap without the job
having to rebuild the connection string itself: it resolves the same saved connection from
the same selected store and hands the result to one child process.

This workflow creates the session resources, deploys with an external tool through `exec`,
runs verification SQL against the same connection, and cleans up the same session. The
deployment tool is a stand-in — `exec` adds no product-specific behavior for any tool.

```powershell
$ErrorActionPreference = 'Stop'
$storeFile = 'C:\agent\state\job-42\connections.json'
$sessionId = [Guid]::NewGuid().ToString('D')

$createOutput = @(& tiger-sqlcmd e2e create `
  --session-id $sessionId --name-part orders `
  --non-interactive --no-color --tq-connection-store-file $storeFile)
if ($LASTEXITCODE -ne 0) { throw "E2E create failed with exit code $LASTEXITCODE." }
$createOutput | Write-Host

$connectionName = $createOutput |
  Select-String '^Created E2E connection (?<name>E2E-[A-Za-z0-9_-]+)\.$' |
  ForEach-Object { $_.Matches[0].Groups['name'].Value } |
  Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($connectionName)) {
  throw 'TigerSqlCmd did not report the created E2E connection name.'
}

try {
  # 1. Deploy with an external tool that reads a connection string from its environment.
  #    Preferred: the value never reaches the child's command line.
  & tiger-sqlcmd exec --connection $connectionName `
    --connection-string-env DEPLOY_CONNECTION_STRING `
    --non-interactive --no-color --tq-connection-store-file $storeFile `
    -- deploy-tool --apply .\schema
  if ($LASTEXITCODE -ne 0) { throw "Schema deployment failed with exit code $LASTEXITCODE." }

  # 2. Verify with TigerSqlCmd itself, against the very same saved connection.
  & tiger-sqlcmd run --connection $connectionName `
    --query 'SELECT COUNT(*) AS TableCount FROM sys.tables;' `
    --mode SqlCmdEx --non-interactive --no-color `
    --tq-connection-store-file $storeFile
  if ($LASTEXITCODE -ne 0) { throw "Verification SQL failed with exit code $LASTEXITCODE." }
}
finally {
  & tiger-sqlcmd e2e cleanup --session-id $sessionId `
    --non-interactive --no-color --tq-connection-store-file $storeFile
  if ($LASTEXITCODE -ne 0) {
    Write-Error "E2E cleanup was incomplete for session $sessionId."
  }
}
```

`exec` returns the deployment tool's own exit code unchanged, so `$LASTEXITCODE` is the
tool's contract, not a TigerSqlCmd translation of it. Exit code `21` means the tool could not
be started at all, and `20` means the `exec` handoff configuration was rejected before the
connection was resolved.

Use argument substitution only when the tool has no environment-variable input:

```powershell
& tiger-sqlcmd exec --connection $connectionName `
  --non-interactive --no-color --tq-connection-store-file $storeFile `
  -- deploy-tool --apply .\schema --target '{connection-string}'
```

That form puts the resolved connection string into the child's command line, where any
process listing on the agent can read it. On a shared or long-lived build agent, prefer the
environment form. Quote the placeholder in PowerShell as shown so `{connection-string}`
reaches `tiger-sqlcmd` intact; `exec` performs no other expansion and starts the tool
directly, with no shell in between. The complete contract is in
[Running an external tool: `exec`](tiger-sqlcmd.md#running-an-external-tool-exec).

## Existing database: non-owning clone example

`connection clone-e2e` performs no SQL operation. It copies a source profile within the
same store, changes the target database, preserves authentication and unresolved external
references, and writes `ittiger.e2e.database.allow-drop=false`.

This example targets an existing read-only database, uses the clone, and removes only the
session connection:

```powershell
$ErrorActionPreference = 'Stop'
$storeFile = 'C:\agent\state\job-42\connections.json'
$sessionId = [Guid]::NewGuid().ToString('D')

try {
  $cloneOutput = @(& tiger-sqlcmd connection clone-e2e reporting-source `
    --database ExistingReportingDb --session-id $sessionId --name-part readonly `
    --non-interactive --no-color --tq-connection-store-file $storeFile)
  if ($LASTEXITCODE -ne 0) { throw "E2E clone failed with exit code $LASTEXITCODE." }
  $cloneOutput | Write-Host

  $connectionName = $cloneOutput |
    Select-String '^Created E2E connection (?<name>E2E-[A-Za-z0-9_-]+) for database ExistingReportingDb\.$' |
    ForEach-Object { $_.Matches[0].Groups['name'].Value } |
    Select-Object -First 1
  if ([string]::IsNullOrWhiteSpace($connectionName)) {
    throw 'TigerSqlCmd did not report the cloned E2E connection name.'
  }

  & tiger-sqlcmd run --connection $connectionName `
    --query 'SELECT TOP (10) * FROM dbo.ReportSource;' `
    --mode SqlCmdEx --non-interactive --no-color `
    --tq-connection-store-file $storeFile
  if ($LASTEXITCODE -ne 0) { throw "Read-only SQL run failed with exit code $LASTEXITCODE." }
}
finally {
  & tiger-sqlcmd e2e cleanup --session-id $sessionId `
    --non-interactive --no-color --tq-connection-store-file $storeFile
  if ($LASTEXITCODE -ne 0) {
    Write-Error "E2E clone cleanup was incomplete for session $sessionId."
  }
}
```

Cleanup of a non-owning clone never resolves the bootstrap, opens SQL, or drops the
database. It removes only the exact saved connection. `e2e drop` has the same non-owning
behavior when given that one connection and its exact session ID.

## Parallel jobs and cleanup safety

Give every parallel job and coding agent both a unique store path and a unique session
GUID. Shared stores are mutation-safe, but isolated stores make ownership, logs, and
post-failure recovery easier to understand. Never reuse a session GUID for unrelated
jobs.

Cleanup is connection-record driven. It does not delete by session prefix, connection
prefix, database prefix, partial GUID, age, or apparent inactivity. Bootstrap records and
other sessions are not candidates. A database created by a session is droppable only
through its owning record; a pre-existing database targeted by a clone is never owned.

Regular `connection delete` rejects an owning E2E record and directs the operator to the
dedicated lifecycle. This prevents accidental loss of the durable ownership record.

## Partial failures, recovery, and orphans

`e2e cleanup` continues across every exact-session candidate. It reports each dropped,
already-absent, detached, or failed item and exits nonzero if any item is incomplete. Keep
the store and complete logs, fix the exact failure, then retry with the same session GUID.
Do not delete an owning connection record by editing the JSON store.

If database creation succeeds but saving its paired connection fails, TigerQuery attempts
to roll back only the exact database created by that invocation. A rollback failure names
the exact possible orphan for manual investigation.

TigerQuery's library can perform read-only orphan reporting for names matching its
protected prefix. Reporting is not proof of ownership and there is intentionally no CLI
or library sweep/delete-by-prefix operation. A human administrator must inspect an
orphan report, establish ownership independently, and explicitly delete the exact database
with SQL administration tooling. Never automate deletion from the `_TQ_E2E_` prefix
alone.
