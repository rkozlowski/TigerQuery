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

Creation is refused unless the selected profile currently has both
`ittiger.e2e.enabled=true` and
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

The normal test suite is deliberately inert. When
`TIGERQUERY_CONNECTION_STORE_FILE` is absent, the test-only resolver returns
`NotConfigured` and xUnit runtime-skips live tests without constructing a store,
discovering or probing SQL Server, opening a connection, or creating a database. It
never falls back to the shared user-profile store. `Invalid` and `Ambiguous` are test
failures rather than skips.

For a local live run, create an isolated store and its explicitly named
`tiger-sqlcmd-e2e` bootstrap profile. The profile must carry both exact permissions:
`ittiger.e2e.enabled=true` and `ittiger.e2e.allow-database-create=true`.

```powershell
$env:TIGERQUERY_CONNECTION_STORE_FILE = 'C:\temp\tigerquery-e2e.json'
tiger-sqlcmd connections add-e2e-bootstrap --non-interactive `
  --server localhost --allow-database-create
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

For CI, set the standard store variable to a job-specific writable path, create the
bootstrap non-interactively, and provide SQL credentials through environment or mounted
file references rather than command-line literals. Run the ordinary suite first with
the gate absent to preserve the zero-connection check, then run the gated filter in a
separate job or step where the isolated store and reachable SQL Server are intentional.
Always publish the test log: a cleanup failure names the exact database left behind for
manual investigation.

For containers, mount both the writable store location and any referenced secret files,
and use paths as seen inside the test container. DPAPI-protected values are not portable
across users, machines, or containers. Give the container's SQL principal permission to
create and drop only in the dedicated test environment. Do not add endpoint discovery,
prefix-based cleanup, or an orphan sweeper to make container setup more convenient.

An unconfigured runtime skip confirms only that the default path is inert. Phase 8 is
complete only after the gated workflow has actually passed against a reachable SQL
Server.
