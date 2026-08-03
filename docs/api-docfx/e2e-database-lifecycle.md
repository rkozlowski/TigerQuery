# Safe E2E database lifecycle

`ItTiger.TigerQuery` provides `SqlServerE2eDatabaseLifecycle` for the destructive
SQL part of an explicitly authorized test workflow. `ItTiger.TigerQuery.Core`
continues to own connection stores, profiles, external references, and E2E
authorization; the lifecycle reuses those contracts and TigerQuery's existing SQL
execution engine.

## Create and clean up one database

Resolve the bootstrap profile by an explicit name (or a host-configured default)
and require the separate database-creation permission:

```csharp
using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.E2e;

var resolution = SqlServerE2eConnectionResolver.Resolve(
    store,
    new SqlServerE2eConnectionResolutionOptions
    {
        ConnectionName = configuredBootstrapName,
        RequireDatabaseCreationPermission = true
    });

if (resolution.Status != SqlServerE2eResolutionStatus.Resolved)
    throw new InvalidOperationException(string.Join(" ", resolution.Errors));

var lifecycle = new SqlServerE2eDatabaseLifecycle(store, resolution);
var databaseName = await lifecycle.CreateDatabaseAsync(cancellationToken);

try
{
    // Optional: uses the existing store copy, validation, and persistence path.
    var databaseProfile = lifecycle.AddDatabaseProfile();

    await lifecycle.RunSetupSqlAsync(schemaSql, cancellationToken);
    // Run tests against databaseName or databaseProfile.
    await lifecycle.RunTeardownSqlAsync(teardownSql, cancellationToken);
}
finally
{
    await lifecycle.CleanupAsync(cancellationToken);
}
```

The default generated-name prefix is `_TQ_E2E_`, followed by a 32-character
unique suffix. A host can supply a different prefix through
`SqlServerE2eDatabaseLifecycleOptions.DatabasePrefix`. Prefixes are validated so
the generated SQL Server identifier remains within 128 characters.

Creation is refused unless the selected profile currently has
`ittiger.e2e.enabled=true`, `ittiger.e2e.bootstrap=true`, and
`ittiger.e2e.allow-database-create=true`. Before creation, the lifecycle rechecks
the explicitly selected name in its own store; after creation it retains that
authorized profile for setup and exact-owned cleanup even if the store is edited.
External values are still resolved only while building an effective connection for
an operation and are never written back.

`AddDatabaseProfile` is optional and delegates to the normal Core store-copy path.
It preserves protected and external field values and removes only the exact profile
it created after a successful database drop. A full connection-string reference can
be used for lifecycle SQL, but cannot safely become a persisted database-specific
copy: applying an initial-catalog override would either violate strict full-string
versus field mode or persist resolved plaintext. Use the lifecycle's setup and
teardown helpers directly in that case.

## Ownership and cleanup guards

A lifecycle instance can create at most one database. It records the exact name
only after the create command reports success and retains that record after cleanup
as ownership history. Cleanup sends `DROP DATABASE` only when:

1. the requested target is exactly the current instance's recorded name; and
2. that recorded name still starts with the configured prefix.

A prefix match, database age, server reachability, and discovery are never ownership
evidence. Names created by another lifecycle instance and unrecorded names are
rejected before SQL execution. If drop execution fails or is cancelled,
`SqlServerE2eDatabaseCleanupException` reports the exact `DatabaseName` that may be
left behind, and the lifecycle retains its live ownership state so cleanup can be
retried.

## Orphan reporting is not deletion

`DetectOrphansAsync` performs a read-only query for names matching the configured
prefix, excludes the current lifecycle's exact recorded name, and returns an
`SqlServerE2eOrphanReport`. The report is informational. The lifecycle deliberately
has no orphan-delete or sweep API and never drops reported candidates.

Deleting an orphan requires a separate manual administrative process: a human must
inspect the exact reported name, establish ownership independently, and explicitly
approve the deletion using their chosen SQL administration tooling. Do not treat the
prefix, apparent age, or absence of a current test run as approval. There is no
automatic age-based sweeper.

## Running the repository E2E workflow

The environment variable is optional. With no
`TIGERQUERY_CONNECTION_STORE_FILE`, the live-test harness reads the normal
`tiger-sqlcmd` application-default user store and looks only for the host's expected
`tiger-sqlcmd-e2e` bootstrap name. The local opt-in is that exact named profile carrying
`ittiger.e2e.enabled=true`, `ittiger.e2e.bootstrap=true`, and
`ittiger.e2e.allow-database-create=true`; server reachability and ordinary profiles do not
enable E2E work. The name identifies the expected profile and metadata authorizes it as
bootstrap; both are required.

If the expected bootstrap is absent, resolution returns `NotConfigured` and xUnit
runtime-skips before discovering or probing SQL Server, opening a connection, creating a
database, or modifying the store. An unreadable store, duplicate expected name,
malformed metadata, missing permission, or invalid profile is a failure rather than a
skip.

For the normal local workflow, create the bootstrap in the regular user store once and
then run the tests without an environment variable:

```powershell
tiger-sqlcmd connections add-e2e-bootstrap --non-interactive `
  --server localhost --allow-database-create
Remove-Item Env:TIGERQUERY_CONNECTION_STORE_FILE -ErrorAction Ignore
dotnet test ItTiger.TigerQuery.Tests\ItTiger.TigerQuery.Tests.csproj `
  --filter 'FullyQualifiedName~TigerSqlCmdE2eWorkflowLiveTests'
```

Replace `localhost` with the explicitly configured server. The suite never searches
for one. The gated workflow resolves that profile, creates a unique `_TQ_E2E_...`
database, runs setup through a real child `tiger-sqlcmd` process, uses a generated
database profile, runs teardown, verifies cross-instance cleanup refusal and
report-only orphan detection, forces an exact-name cleanup failure, and retries the
same owned cleanup successfully. References remain references throughout and process
diagnostics are checked for secret disclosure.

For an isolated local run, CI, or a container, set the standard variable to an alternate
writable store. It overrides the regular application-default store; it is not an E2E
enable switch. The selected alternate store must independently contain the same expected
bootstrap name and exact authorization metadata:

```powershell
$env:TIGERQUERY_CONNECTION_STORE_FILE = 'C:\temp\tigerquery-e2e.json'
tiger-sqlcmd connections add-e2e-bootstrap --non-interactive `
  --server localhost --allow-database-create
dotnet test ItTiger.TigerQuery.Tests\ItTiger.TigerQuery.Tests.csproj `
  --filter 'FullyQualifiedName~TigerSqlCmdE2eWorkflowLiveTests'
```

For CI, provide SQL credentials through environment or mounted-file references rather
than command-line literals. A job that needs the zero-connection assertion should point
the environment override at a missing or empty job-specific store, then run the live
workflow separately after creating its authorized bootstrap. Always publish the test
log: a cleanup failure names the exact database left behind for manual investigation.

An older bootstrap profile without `ittiger.e2e.bootstrap=true` now fails live-test
resolution as invalid rather than being used. After preserving any settings still needed,
delete and recreate it with `connections add-e2e-bootstrap`; regular `add --e2e` does not
migrate it.

For containers, mount both the writable store location and any referenced secret files,
and use paths as seen inside the test container. DPAPI-protected values are not portable
across users, machines, or containers. Give the container's SQL principal permission to
create and drop only in the dedicated test environment. Do not add endpoint discovery,
prefix-based cleanup, or an orphan sweeper to make container setup more convenient.

An unconfigured runtime skip confirms only that the selected store contains no usable
bootstrap and no SQL work occurred. It does not mean the environment variable was
absent. Validate the live workflow against a reachable SQL Server through both the
regular default store and an environment-selected alternate store.
