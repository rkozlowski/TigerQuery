# E2E connection stores and database lifecycle

For the operational `tiger-sqlcmd` guide—including bootstrap setup, disposable database
and read-only clone examples, parallel-agent isolation, cleanup, and recovery—see
[TigerSqlCmd E2E scenarios](~/tiger-sqlcmd-e2e.md). This document remains the
architecture and safety-contract reference.

The CLI examples here use `--non-interactive` because repository E2E tests, CI jobs,
scripts, and agents must fail on missing input instead of opening a menu or prompt. The
flag selects TigerCli's unattended interaction policy for the same commands; it does not
relax the lifecycle contract described on this page. See
[One Command Model, Multiple Interaction Modes](~/tiger-sqlcmd.md#one-command-model-multiple-interaction-modes)
for the operational explanation.

TigerQuery's end-to-end infrastructure runs SQL-backed tests only against a connection
that a user or operator deliberately selected and authorized. It combines four durable
contracts:

1. one explicitly selected connection-store file;
2. an expected bootstrap profile name plus exact reserved authorization metadata;
3. resolution that reads configuration but never probes SQL Server;
4. a database lifecycle that creates and cleans up only the exact database owned by that
   lifecycle instance.

The central safety rule is:

> Reachability is not authorization.

A server that answers, an ordinary saved profile, a familiar local instance name, or a
database with a recognizable prefix does not grant permission to run E2E work or delete
anything.

## Goals and safety model

The infrastructure is designed to make a fresh checkout inert while keeping deliberate
local, CI, and container setup straightforward. It must:

- use the same named connection profiles as TigerQuery-family applications;
- let a host define its normal store and expected bootstrap name;
- support an isolated store override without turning the override into an enable switch;
- distinguish missing configuration from malformed or unauthorized configuration;
- keep secrets out of command lines, persisted resolved values, and diagnostics;
- create unique databases only with an explicit database-creation grant;
- establish ownership from one successful create operation, not from naming patterns;
- report possible orphans without deleting them;
- runtime-skip unconfigured xUnit tests before any SQL activity.

Safety failures are intentionally noisy. A missing host-default profile is normal
`NotConfigured` state; an unreadable store, malformed metadata, duplicate selected name,
missing permission, or invalid profile is a failure. Quiet fallback would turn a typo or
corrupt configuration into execution against a different store or server.

## No discovery or probing

`SqlServerE2eConnectionResolver` reads one selected `SqlServerConnectionStore`, selects by
name, validates reserved metadata and the profile, and returns. It does not:

- try `.`, `(local)`, `localhost`, LocalDB, named instances, ports, services, or
  containers;
- enumerate SQL Server instances or inspect running processes;
- open a `SqlConnection`, test credentials, check reachability, or inspect permissions;
- scan profiles and choose the first one that connects;
- create a store file, profile, database, or directory.

The repository live-test harness likewise reads only
`TIGERQUERY_CONNECTION_STORE_FILE` for store selection. Legacy endpoint environment
variables and reachable local servers cannot activate the suite.

## Selecting one connection store

TigerQuery standardizes this precedence:

1. an explicit path supplied by the caller, such as
   `--tq-connection-store-file <path>`;
2. `TIGERQUERY_CONNECTION_STORE_FILE`;
3. the host application's `DefaultConnectionStoreFile`.

`SqlServerConnectionStorePathResolver` normalizes the winning value to an absolute path
and reports its `SqlServerConnectionStorePathSource`. A source that supplies no value is
skipped. A source that supplies a blank, malformed, or directory-only path decides the
outcome and fails; TigerQuery never falls through to a lower-priority store.

Path resolution is syntactic and inert. It does not check whether the file exists, touch
the filesystem, or open SQL. An absent selected store later loads as empty for E2E
resolution and normally yields `NotConfigured`. An existing store that cannot be read or
parsed yields `Invalid`; it is never mistaken for an empty store.

Every operation on a constructed `SqlServerConnectionStore` uses that instance's one
normalized `FilePath`. There is no hidden second lookup. Mutations use the store's normal
coordinated, atomic persistence path; a failed mutation leaves the previous store intact.

### `tiger-sqlcmd` store choices

`tiger-sqlcmd` uses the shared per-user store returned by:

```csharp
SqlServerConnectionStoreOptions.Shared("ItTiger.net").FilePath
```

An environment or command-line override changes only the store path. The selected store
must independently contain the expected bootstrap name and all required metadata;
selecting a path does not authorize E2E work. The complete regular-store and isolated-store
setup is in
[TigerSqlCmd E2E scenarios](~/tiger-sqlcmd-e2e.md#regular-and-isolated-stores).

## TigerCli integration

`ItTiger.TigerQuery.CliCore` exposes `TigerQueryCliContribution` and
`TigerQueryCliOptions` so hosts use the same option, environment variable, precedence,
and failure rules as `tiger-sqlcmd`.

```csharp
using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;

var contribution = new TigerQueryCliContribution(new TigerQueryCliOptions
{
    DefaultConnectionStoreFile =
        SqlServerConnectionStoreOptions.AppSpecific("Example", "example-sql").FilePath,
    DefaultE2eBootstrapConnectionName = "example-sql-e2e"
});

var app = TigerCliApp.CreateBuilder()
    .UseAssemblyMetadata(typeof(Program).Assembly)
    .UseAppResources(SqlServerConnectionCommands.CreateAppResources())
    .AddContribution(contribution)
    .AddCommandGroup("connection", group =>
    {
        SqlServerConnectionCommands.Configure(group, options =>
        {
            options.TigerQuery = contribution.Options;
            options.ValidationPolicy =
                SqlServerConnectionValidationPolicy.DatabaseOptional;
        });
    })
    .Build();
```

Create one `TigerQueryCliOptions` instance and share it with the contribution, connection
commands, providers, and host services. The contribution runs once before settings are
bound, resolves the store path, and stores the result. `TigerQueryCliOptions.Store` is
then created lazily and reused for the run. A malformed override fails before a command
handler, even for a command that would not otherwise open the store.

Registering the contribution and mounting the `connection` group are separate opt-ins.
Register the contribution at most once. It also contributes help metadata for
`TIGERQUERY_CONNECTION_STORE_FILE`; a host adopting it must not register the same
environment variable independently.

## E2E metadata and bootstrap selection

TigerQuery owns the ordinal, case-sensitive `ittiger.e2e.*` namespace. The namespace
currently defines these keys:

```text
ittiger.e2e.enabled=true
ittiger.e2e.bootstrap=true
ittiger.e2e.allow-database-create=true
```

| Key | Meaning |
| --- | --- |
| `ittiger.e2e.enabled` | The profile may be used for TigerQuery E2E work. |
| `ittiger.e2e.bootstrap` | The selected profile may act as an E2E bootstrap. |
| `ittiger.e2e.allow-database-create` | E2E infrastructure may create databases through the profile. |
| `ittiger.e2e.session-id` | Exact session safety-correlation GUID for a generated non-bootstrap connection. |
| `ittiger.e2e.database.name` | Exact database targeted by that connection. |
| `ittiger.e2e.database.allow-drop` | Exact lowercase Boolean granting or withholding database ownership. |

Only the exact lowercase values `true` and `false` are valid. `True`, `1`, `yes`, an
empty value, and values with surrounding whitespace are malformed. Malformed reserved
metadata is an error, not a denial or an absent flag.

The complete lowercase `ittiger.e2e.*` namespace is reserved for TigerQuery. Generic profile
and CLI metadata mutation rejects keys in that namespace. Applications must use their own
prefix. Unknown reserved keys are preserved and ignored by older readers so newer stores
remain forward-compatible. Use `SqlServerE2eMetadata.AuthorizeNewProfile` or
`AuthorizeNewBootstrapProfile`, rather than generic metadata setters, to write the known
authorization keys.

### Expected bootstrap name

`tiger-sqlcmd` configures the default bootstrap name:

```text
tiger-sqlcmd-e2e
```

Core itself invents no global default; a library host supplies
`SqlServerE2eConnectionResolutionOptions.DefaultConnectionName` or an explicit
`ConnectionName`.

Name and metadata have separate mandatory roles:

- the explicit caller name, or host default name, identifies the intended profile;
- exact `enabled=true` and `bootstrap=true` metadata authorizes that selected profile.

Neither is sufficient alone. A conventionally named ordinary profile is not authorized,
and a store containing one authorized bootstrap is not selected implicitly when no name
is supplied. Store order and connection reachability never break a tie. Database-creating
work additionally requests and requires exact `allow-database-create=true`.

`SqlServerE2eConnectionResolver.Resolve` returns one of four statuses:

| Status | Meaning |
| --- | --- |
| `Resolved` | Exactly one named profile is complete and has every required authorization. This is the only status carrying a profile. |
| `NotConfigured` | No name is available, the selected store is absent/empty, or the host-default name has not been created. Test infrastructure may skip. |
| `Ambiguous` | Duplicate requested names or multiple authorized candidates with no supplied name make selection unsafe. Never choose the first. |
| `Invalid` | An explicit name is missing, the store is unreadable, metadata is absent/malformed, a permission is missing, or profile validation fails. Fail rather than skip. |

The explicit `ConnectionName` and host `DefaultConnectionName` intentionally fail
differently. A caller explicitly naming a missing profile made an invalid request; a
host convention that has never been configured describes a clean machine.

## Connection-management command boundary

`connection add --allow-database-create` requires `--e2e`. Regular `add --e2e` writes
`ittiger.e2e.enabled=true` and, when requested, the database-creation flag. It never writes
`ittiger.e2e.bootstrap=true`.

The dedicated `connection add-e2e-bootstrap [--name <name>]` command writes
`enabled=true` and `bootstrap=true`, plus
`allow-database-create=true` only when requested. It is add-only and refuses to overwrite
an existing profile. Ordinary edit preserves reserved metadata but generic `--metadata`
and `--remove-metadata` cannot grant, alter, or remove reserved authorization.

Older E2E profiles without `bootstrap=true` are invalid bootstraps. Preserve any needed
settings, then recreate them with `connection add-e2e-bootstrap`, or use the public
TigerQuery-owned authorization API and persist the updated profile. Regular
`connection add --e2e` is not a bootstrap migration.

See [TigerSqlCmd E2E scenarios](~/tiger-sqlcmd-e2e.md) for verified CLI
invocations and secret-reference setup.

## Session-scoped CLI lifecycle

The `tiger-sqlcmd e2e` group is a durable, connection-record-driven lifecycle. Every
command requires a non-empty GUID `--session-id`; it is a safety correlation value, not a
secret.

One command creates both a new database and a saved connection targeting it. There is no
database-only create command. The authorized bootstrap must have exact
`enabled=true`, `bootstrap=true`, and `allow-database-create=true`. The copied profile
preserves authentication and unresolved external references; resolved values are never
persisted. If connection persistence fails, TigerQuery attempts to roll back only the exact
database created by that invocation and distinguishes successful rollback from a rollback
failure that needs manual attention.

The generated database is `_TQ_E2E_<database-part>_<suffix>` and the connection is
`E2E-<connection-part>-<suffix>`. `--name-part` defaults both parts;
`--database-name-part` and `--connection-name-part` override their respective part. Without
any part, `session` is used. Name parts are trimmed, runs of unsupported characters become
one hyphen, and the result must contain an ASCII letter or digit and fit the SQL identifier
limit. Prefixes are fixed and the same random suffix is used for both names.

The paired connection receives the complete protected schema:

```text
ittiger.e2e.enabled=true
ittiger.e2e.bootstrap=false
ittiger.e2e.allow-database-create=false
ittiger.e2e.session-id=<canonical-guid>
ittiger.e2e.database.name=<exact-created-database-name>
ittiger.e2e.database.allow-drop=true
```

`connection clone-e2e` performs no SQL operation. It preserves authentication and unresolved external
references, retargets the clone to the exact selected database, rejects a generated name
that already exists, and writes the same protected schema with
`ittiger.e2e.database.allow-drop=false`. This is the supported path for pre-existing or
read-only databases.

`e2e drop` requires exact `enabled=true`, `bootstrap=false`, and session-ID metadata. It reads the
database name and ownership Boolean only from protected metadata. With `allow-drop=true`,
it additionally validates the exact `_TQ_E2E_` prefix, drops through the authorized
bootstrap, and removes the connection only after the drop succeeds or the exact database
is confirmed absent. With `allow-drop=false`, it never connects to SQL and removes only the
saved connection.

Cleanup enumerates matching protected non-bootstrap connection records, never database
names or prefixes. It continues after each failure, reports dropped, absent, detached, and
failed items, and returns non-zero while any matching record remains incomplete. Bootstrap
records and other sessions are never selected. Regular `connection delete` refuses
`allow-drop=true` records and directs callers to `e2e drop` or `e2e cleanup`; it permits
`allow-drop=false` records.

## External value references

Connection profiles support external values for server, database, SQL username, SQL
password, and a complete connection string. This lets a writable store retain stable
configuration while CI or container secrets remain in environment variables or mounted
files.

Existing JSON strings remain literal values, so older stores need no migration. A value
can instead be a tagged reference object.

### Environment variables

```json
{
  "Source": "EnvironmentVariable",
  "Name": "TQ_E2E_SQL_SERVER"
}
```

The named variable is read only when an effective connection string is built. A missing
or empty required value fails resolution without exposing another resolved value.

### Whole files

```json
{
  "Source": "File",
  "Path": "/run/secrets/sql-password",
  "Format": "Text"
}
```

Text files are read whole as UTF-8 and are not trimmed. A trailing newline is part of the
value.

### Keyed JSON files

```json
{
  "Source": "File",
  "Path": "/run/secrets/sql-auth.json",
  "Format": "Json",
  "Key": "username"
}
```

The file must contain a top-level JSON object. `Key` is an exact, case-sensitive
top-level property and its value must be a JSON string. Missing files, malformed JSON,
missing keys, non-string values, and unknown source shapes fail cleanly.

### CLI reference boundary

The `add`, `edit`, and `add-e2e-bootstrap` commands accept:

```text
--server-reference <json>
--database-reference <json>
--username-reference <json>
--password-reference <json>
--connection-string-reference <json>
```

Reference options require an object; a JSON string literal is rejected so they cannot be
used to smuggle plaintext secrets onto the command line. Sensitive password and access
token keys are also rejected through `--opt`.

Operational PowerShell examples are kept in
[TigerSqlCmd E2E scenarios](~/tiger-sqlcmd-e2e.md#non-interactive-bootstrap-with-external-secrets).

Profiles operate in exactly one mode:

- full connection-string mode uses `ConnectionString` or its reference;
- field mode uses server, database, authentication, username/password, encryption, and
  related fields.

Mixing a full connection string with individual fields is rejected before the store is
changed. The CLI and validator do not choose precedence.

References are resolved lazily into an in-memory effective connection string. Copy and
edit preserve the reference objects. Resolved values are never written back to the
profile or store. List/show output and diagnostics display safe reference descriptions
(variable name, file path, and optional key), never read the source, and redact literal
passwords and complete connection strings. Locators themselves are visible, so do not put
secret material in variable names, paths, or JSON keys.

On Windows, the default literal-password protection is DPAPI scoped to the current user
and machine. A copied store does not make its encrypted password portable. For CI and
containers, prefer external references or configure an appropriate
`IConnectionPasswordProtector` in a library host.

## Library resolution

Library consumers can select a store and authorize a bootstrap without any CLI:

```csharp
using ItTiger.TigerQuery.Core;

var path = SqlServerConnectionStorePathResolver.Resolve(
    new SqlServerConnectionStorePathOptions
    {
        ExplicitFilePath = configuredStorePath,
        DefaultFilePath =
            SqlServerConnectionStoreOptions.AppSpecific("Example", "tests").FilePath
    });

if (!path.IsSuccess)
    throw new InvalidOperationException(path.ErrorMessage);

var store = new SqlServerConnectionStore(
    new SqlServerConnectionStoreOptions { FilePath = path.FilePath! });

var resolution = SqlServerE2eConnectionResolver.Resolve(
    store,
    new SqlServerE2eConnectionResolutionOptions
    {
        DefaultConnectionName = "example-e2e",
        RequireDatabaseCreationPermission = true,
        ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional
    });

if (resolution.Status == SqlServerE2eResolutionStatus.NotConfigured)
    return; // The host decides whether this means skip, disable, or a setup message.

if (resolution.Status != SqlServerE2eResolutionStatus.Resolved)
    throw new InvalidOperationException(string.Join(" ", resolution.Errors));
```

Core returns neutral status values and does not depend on xUnit. Test-framework mapping is
a harness responsibility.

## `SqlServerE2eDatabaseLifecycle`

`ItTiger.TigerQuery.E2e.SqlServerE2eDatabaseLifecycle` performs the destructive SQL part
only after Core has returned a resolved bootstrap. Creation rechecks the selected profile
in the same store and requires current `enabled=true`, `bootstrap=true`, and
`allow-database-create=true` authorization.

```csharp
using ItTiger.TigerQuery.E2e;

var lifecycle = new SqlServerE2eDatabaseLifecycle(store, resolution);
var databaseName = await lifecycle.CreateDatabaseAsync(cancellationToken);

try
{
    var generatedProfile = lifecycle.AddDatabaseProfile();
    await lifecycle.RunSetupSqlAsync(schemaSql, cancellationToken);

    // Run library calls or a tiger-sqlcmd child process with generatedProfile.Name.

    await lifecycle.RunTeardownSqlAsync(teardownSql, cancellationToken);
}
finally
{
    if (lifecycle.CreatedDatabaseName is not null && !lifecycle.DatabaseWasDropped)
        await lifecycle.CleanupAsync(CancellationToken.None);
}
```

### Names and host overrides

The default prefix is:

```text
_TQ_E2E_
```

Creation appends a 32-character GUID-style suffix. A host may set
`SqlServerE2eDatabaseLifecycleOptions.DatabasePrefix`; validation keeps the generated SQL
identifier within SQL Server's 128-character limit.

The prefix is a defensive cleanup guard, not ownership evidence. Changing it should make
test databases easy to recognize and scope operational permissions, but it never permits
prefix-wide deletion.

### Exact-instance ownership and cleanup

One lifecycle instance creates at most one database and records its exact name only after
the create command succeeds. Cleanup is allowed only when:

1. the supplied cleanup target exactly equals that instance's recorded live name using
   ordinal comparison; and
2. the recorded name still starts with the configured prefix.

Another lifecycle's database, an unrecorded name, a merely prefix-matching name, and an
already dropped database are rejected before `DROP DATABASE`. Cleanup clears idle pooled
connections for the exact database, issues one exact-name drop through `master`, and
removes an optional copied profile only after the database drop succeeds.

If drop fails or is cancelled, `SqlServerE2eDatabaseCleanupException.DatabaseName`
identifies the exact database that may remain. Ownership state and any generated profile
are retained so the same cleanup can be retried. Active connections are not forcibly
adopted or killed; they can make the drop fail.

`AddDatabaseProfile` uses Core's same-store copy path, preserving protected values and
external references while setting the created database as the catalog. A full
connection-string profile can run lifecycle SQL in memory, but cannot be persisted as a
database-specific copied profile without violating full-string/field separation or
writing resolved plaintext. In that case, use the lifecycle SQL methods directly.

### Orphan reporting only

`DetectOrphansAsync` queries names with the configured prefix, excludes the current
instance's exact database, removes duplicates, sorts ordinally, and returns an
`SqlServerE2eOrphanReport`. The report is informational. There is no orphan-delete or
sweep API, no age-based cleanup, and no automatic deletion.

An operator must inspect an orphan candidate, establish ownership independently, and use
separate administration tooling with explicit approval. Prefix, apparent age, and absence
of a running test are not sufficient evidence.

## Mixed library and CLI workflow

The supported mixed-mode pattern is:

1. Core selects the store and resolves the authorized bootstrap.
2. `SqlServerE2eDatabaseLifecycle` creates one database and copies a named profile in the
   same store.
3. A library test or child `tiger-sqlcmd` process uses that generated profile.
4. The lifecycle runs teardown and drops its exact database.
5. Only after a successful drop does it delete the generated profile.

The repository's `TigerSqlCmdE2eWorkflowLiveTests` exercises this complete external
process path, including external-value references, secret redaction, report-only orphan
detection, rejection of cross-instance cleanup, a deliberately blocked cleanup, and a
successful retry.

## Repository live-test behavior

The repository harness supplies
`TigerSqlCmdApp.DefaultE2eBootstrapConnectionName` (`tiger-sqlcmd-e2e`) as the host default
and normally requires database creation. A usable bootstrap therefore has all three exact
entries:

```text
ittiger.e2e.enabled=true
ittiger.e2e.bootstrap=true
ittiger.e2e.allow-database-create=true
```

When the expected default bootstrap is missing, Core returns `NotConfigured` and the
xUnit harness calls `Assert.Skip` at runtime before opening a SQL connection. `Invalid`
and `Ambiguous` call `Assert.Fail`; bad configuration must not masquerade as an optional
environment.

Live tests require normal host execution because they need SQL Server access, normal
filesystem and user-profile access, child-process execution for `tiger-sqlcmd`, named
pipes/tooling processes, and access to the selected store. Sandbox-only TLS,
`File.Replace`, named-pipe, or child-process failures must be rerun with normal host access
before being treated as product defects.

### Validated default-store mode

```powershell
Remove-Item Env:TIGERQUERY_CONNECTION_STORE_FILE -ErrorAction Ignore
dotnet test ItTiger.TigerQuery.Tests\ItTiger.TigerQuery.Tests.csproj `
  --filter "FullyQualifiedName~ItTiger.TigerQuery.Tests.Live" `
  --no-restore --no-build --logger "console;verbosity=normal" -m:1 /nodeReuse:false
```

This proves that the regular `tiger-sqlcmd` application-default store and expected name
are sufficient; no special E2E enable environment variable exists.

### Validated environment-selected mode

Prepare an isolated store containing its own `tiger-sqlcmd-e2e` bootstrap, then run:

```powershell
$env:TIGERQUERY_CONNECTION_STORE_FILE = 'C:\temp\tigerquery-e2e.json'
dotnet test ItTiger.TigerQuery.Tests\ItTiger.TigerQuery.Tests.csproj `
  --filter "FullyQualifiedName~ItTiger.TigerQuery.Tests.Live" `
  --no-restore --no-build --logger "console;verbosity=normal" -m:1 /nodeReuse:false
Remove-Item Env:TIGERQUERY_CONNECTION_STORE_FILE -ErrorAction Ignore
```

This proves the environment override outranks the application default and remains only a
path selector. Always remove the process override after a run so later commands do not
silently remain attached to the alternate store.

### Unconfigured safety gate

The full suite is also run against a unique missing store and verifies that resolution
does not create it:

```powershell
$unconfiguredStore = Join-Path $env:TEMP (Join-Path `
  ('TigerQuery-unconfigured-' + [Guid]::NewGuid().ToString('N')) `
  'connections.json')
$env:TIGERQUERY_CONNECTION_STORE_FILE = $unconfiguredStore
dotnet test TigerQuery.sln --no-restore --no-build `
  --logger "console;verbosity=normal" -m:1 /nodeReuse:false
$testExit = $LASTEXITCODE
$storeCreated = Test-Path -LiteralPath $unconfiguredStore
Remove-Item Env:TIGERQUERY_CONNECTION_STORE_FILE -ErrorAction Ignore
"UnconfiguredStoreCreated=$storeCreated"
exit $testExit
```

The expected result is `UnconfiguredStoreCreated=False`. An unconfigured skip proves
only that the selected store had no usable bootstrap and no SQL work occurred; it does
not prove that the environment variable was absent.

## Operational guidance by environment

### Local development

- Prefer the regular shared store and the default `tiger-sqlcmd-e2e` name.
- Grant `--allow-database-create` only on a dedicated test SQL Server or principal.
- Leave `TIGERQUERY_CONNECTION_STORE_FILE` unset except for intentional isolation.
- Treat reported orphan names as manual investigation items.

### CI

- Use a job-owned writable store selected by `TIGERQUERY_CONNECTION_STORE_FILE`.
- Create or provision the exact expected bootstrap in that store.
- Supply SQL credentials through environment or mounted-file references, not literal argv
  values.
- Publish logs; cleanup exceptions name the exact database that may require attention.
- Run the unconfigured gate separately from the authorized live workflow.

### Containers

- Mount the store and referenced secret files at paths visible inside the container.
- Do not copy a developer's DPAPI-protected store and expect it to decrypt.
- Use an explicitly addressable server supplied in the profile; do not add endpoint
  discovery as a convenience fallback.
- Give the SQL principal only the permissions required in a dedicated E2E environment.

### Library hosts

- Define the application default and expected bootstrap name explicitly.
- Use Core's path and E2E resolvers; do not pre-merge values in a way that loses their
  different failure semantics.
- Map `NotConfigured` according to host policy and treat `Invalid`/`Ambiguous` as faults.
- Inject `SqlServerExternalValueResolutionOptions` when tests need controlled environment
  or file readers.

## Package ownership boundaries

| Component | Owns |
| --- | --- |
| `ItTiger.TigerQuery.Core` | Connection profiles and stores, path precedence, external values, validation/redaction, reserved E2E metadata, neutral bootstrap resolution, and same-store profile copying. It opens no SQL connection during E2E resolution. |
| `ItTiger.TigerQuery.CliCore` | Reusable TigerCli `connection` commands, `TigerQueryCliContribution`, the standard store option/environment help, `--e2e`, `add-e2e-bootstrap`, external-reference CLI binding, and semantic CLI outcomes. |
| `ItTiger.TigerQuery` | SQLCMD execution and the SQL-backed `SqlServerE2eDatabaseLifecycle`, exact ownership, setup/teardown execution, cleanup exceptions, and report-only orphan detection. |
| `tiger-sqlcmd` | The executable host: shared-store default, `tiger-sqlcmd-e2e` convention, composition of Core/CliCore/engine, application exit codes, console UI, and child-process workflows. |

These boundaries matter. Core authorization remains useful without TigerCli or xUnit;
CliCore does not need the script engine; the lifecycle reuses Core authorization instead
of inventing a second connection mechanism; and the executable supplies host conventions
without turning them into universal library defaults.

## Security and lifecycle rationale

- **Explicit store selection prevents lateral fallback.** A malformed CI override cannot
  redirect work into a developer's personal default store.
- **Name plus metadata separates identity from authority.** The name prevents selection
  drift; reserved metadata records deliberate authorization. Requiring both prevents an
  ordinary profile from becoming destructive merely because it has a familiar name.
- **Database creation is a separate grant.** General E2E permission does not imply schema
  or database creation rights.
- **No probing keeps absence safe.** A fresh machine remains inert even if SQL Server is
  reachable locally.
- **Exact ownership prevents prefix-based deletion.** Prefixes help humans and add a
  second guard, but only the lifecycle's successful create record authorizes cleanup.
- **Retryable cleanup preserves evidence.** A failed drop retains the exact name and
  copied profile instead of concealing or broadening the failure.
- **References stay references.** Resolving only in memory lets automation use rotating
  secrets without persisting them or exposing them through inspection.
- **Orphans are reported, not swept.** Automated code cannot prove that a similarly named
  database is abandoned or belongs to the current job.

## Known limitations

- The repository live workflow requires a SQL principal that can create and drop
  databases when `allow-database-create=true` is required.
- SQL-authentication profiles intentionally skip tests that specifically require Windows
  integrated authentication. This is a coverage limitation, not an E2E-resolution
  failure; SQL-auth-compatible live tests can still run.
- DPAPI-protected literal passwords are bound to the Windows user and machine. Use
  external references or a host-selected protector for portable automation.
- A full connection-string profile is retargeted through a persisted initial-catalog
  override when TigerQuery creates a per-database copy. The referenced connection string
  remains unresolved and is never written back.
- The lifecycle does not force-close active sessions. Cleanup can fail and must be
  retried or investigated using the exact reported database name.
- Orphan detection is read-only and intentionally provides no automatic cleanup.
- The default `_TQ_E2E_` prefix is recognizable but is not a security boundary or proof
  of ownership.
- E2E resolution validates configuration without testing reachability or SQL permissions;
  those failures occur only when the authorized workflow performs SQL.
