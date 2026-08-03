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

## Running the repository live test

The Phase 7 live test is inert unless `TIGERQUERY_CONNECTION_STORE_FILE` names an
isolated E2E store. That store must contain the explicitly selected
`tiger-sqlcmd-e2e` bootstrap profile with both E2E flags set to the exact lower-case
value `true`. `NotConfigured` produces an xUnit runtime skip; malformed, ambiguous,
or unauthorized configuration fails. The test never falls back to a user-profile
store or probes a server.
