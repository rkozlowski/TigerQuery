# ItTiger.TigerQuery.Core

Reusable **SQL Server connection-profile** support for TigerQuery-family tools and any .NET application that wants named, saved connections instead of raw connection strings.

Intended consumers are tool and application developers: define profiles once (server, authentication, encryption, pooling, …), store them in a JSON file, and resolve them to `Microsoft.Data.SqlClient` connection strings by name.

## Key types

- `SqlServerConnectionProfile` — a named profile with first-class options (server, database, authentication, encryption, trust, application intent, timeouts, pooling), external-value references, a free-form options escape hatch, and optional namespaced application metadata; builds a `SqlConnectionStringBuilder` / connection string.
- `SqlServerConnectionValue` / `SqlServerExternalValueReference` / `SqlServerExternalValueResolutionOptions` — legacy-compatible literal strings plus lazy environment, whole-file, and keyed-JSON values with injectable readers.
- `SqlServerConnectionStore` / `SqlServerConnectionStoreOptions` — JSON file storage with `Shared(vendor)` (a per-user vendor store shared across tools) and `AppSpecific(vendor, app)` locations, or any explicit `FilePath`; `QueryByMetadata(...)` applies reusable metadata filters and `Copy(...)` duplicates a saved connection inside the same store.
- `SqlServerConnectionCopyOptions` — the controlled overrides a copy may apply: target name, initial catalog, and selected metadata entries.
- `SqlServerConnectionStorePathResolver` / `SqlServerConnectionStorePathOptions` / `SqlServerConnectionStorePathResolution` — the standard precedence for *which* store file to use: explicit path, then the `TIGERQUERY_CONNECTION_STORE_FILE` environment variable (`SqlServerConnectionStoreEnvironment`), then the application default; reports the winning source and never silently falls back.
- `SqlServerConnectionResolver` / `SqlServerConnectionResolution` — name → connection string with clean failure messages.
- `SqlServerE2eMetadata` / `SqlServerE2eConnectionResolver` — the reserved metadata that authorizes general E2E use and bootstrap use, and the resolver that requires both the expected name and bootstrap authorization without opening a connection.
- `SqlServerConnectionValidator` / `SqlServerConnectionValidationPolicy` — profile validation (e.g. database required vs. optional); `ValidateComplete(...)` also checks credential presence and connection-string compatibility.
- `IConnectionPasswordProtector` — password-at-rest strategy: `DpapiConnectionPasswordProtector`, `NonPersistingConnectionPasswordProtector`, `NoOpConnectionPasswordProtector`, and `ConnectionPasswordProtector.CreateDefault()`.
- `SqlServerDatabaseLister` — async database enumeration for a profile.

## Installation

```
dotnet add package ItTiger.TigerQuery.Core
```

## Quick start

```csharp
using ItTiger.TigerQuery.Core;

var store = new SqlServerConnectionStore(
    new SqlServerConnectionStoreOptions { FilePath = "connections.json" });

if (!store.Exists("local"))
{
    store.Add(new SqlServerConnectionProfile
    {
        Name = "local",
        Server = "localhost",
        Authentication = AuthenticationType.Integrated,
        Encrypt = EncryptOption.Mandatory,
        TrustServerCertificate = true
    });
}

var resolution = SqlServerConnectionResolver.Resolve(store, "local");
if (resolution.IsSuccess)
    Console.WriteLine($"Connection string ready ({resolution.ConnectionString!.Length} chars).");
else
    Console.WriteLine($"Failed: {resolution.ErrorMessage}");
```

Applications can attach namespaced, non-secret string metadata without affecting the
generated SQL connection string:

```csharp
var profile = store.Find("local")!;
profile.SetMetadata("yourvendor.yourapp.role", "automation-host");
store.AddOrUpdate(profile);
```

Metadata is opaque to TigerQuery, uses ordinal key comparison, and can be removed with
`profile.RemoveMetadata(key)`. Do not use it for passwords, tokens, or other secrets.

Profiles can be queried with ordinal, case-sensitive metadata predicates. Filters use
AND semantics and results retain their order in the store:

```csharp
var automationProfiles = store.QueryByMetadata(
[
    new SqlServerConnectionMetadataFilter
    {
        Key = "yourvendor.yourapp.role",
        Operator = SqlServerConnectionMetadataFilterOperator.Equals,
        Value = "automation-host"
    },
    new SqlServerConnectionMetadataFilter
    {
        Key = "yourvendor.yourapp.enabled",
        Operator = SqlServerConnectionMetadataFilterOperator.IsSet
    }
]);
```

For a real shared store, prefer `SqlServerConnectionStoreOptions.Shared("YourVendor")` (per-user application-data location on Windows, `~/.config` elsewhere) so multiple tools see the same connections.

## External values for CI and containers

Server, database, SQL username, SQL password, and a complete connection string
can be stored as explicit references instead of literals. Existing JSON strings
remain literals, so old stores need no migration.

```json
{
  "Server": { "Source": "EnvironmentVariable", "Name": "TQ_SQL_SERVER" },
  "Password": { "Source": "File", "Path": "/run/secrets/sql-password", "Format": "Text" }
}
```

A keyed file uses `"Format":"Json"` plus an exact, case-sensitive top-level
`"Key"`; its value must be a JSON string. Text files are read whole and are not
trimmed. References resolve only while building the effective connection, are
preserved by copy/edit, and are never written back as resolved values. Missing
variables/files, malformed JSON, absent keys, and unknown sources fail without
including resolved secret material in errors.

Profiles use either a full `ConnectionString` value or the individual fields,
never both. The validator rejects mixed mode rather than choosing precedence.
Literal passwords and full connection strings are sensitive; inspection and
diagnostics redact them. Reference descriptions display their environment name
or file path/key without reading the source, so choose locators with the
understanding that those names and paths are visible.

## Copying a saved connection

`Copy` duplicates an existing profile under a new name inside the **same** store. It is
the supported way to derive a connection — a scratch database, a per-run test database,
a second catalog on the same server — from one that a user already approved, instead of
rebuilding a connection string or hand-copying properties:

```csharp
var copy = store.Copy("bootstrap", new SqlServerConnectionCopyOptions
{
    TargetName = "run-42",
    InitialCatalogOverride = "ScratchDb",
    MetadataToSet = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["yourvendor.yourapp.role"] = "scratch"
    },
    MetadataToRemove = ["yourvendor.yourapp.owner"]
});

// The result is an ordinary saved profile.
var resolution = SqlServerConnectionResolver.Resolve(store, copy.Name);
store.Delete(copy.Name);
```

What the operation guarantees:

- **Everything is preserved by default.** Server, authentication mode, user name, the
  protected password, encryption and certificate trust, timeouts, pooling, free-form
  `Options`, and every metadata entry are carried over. The copy is taken from the
  profile's own JSON contract, not from a hand-written property list, so a field added
  to `SqlServerConnectionProfile` in a later release is copied without a caller change.
- **Only three things can be overridden**: the profile name, the initial catalog
  (`null` preserves, `""` clears, anything else replaces), and the metadata entries you
  name. Unrelated metadata survives untouched.
- **Same store, always.** `Copy` is an instance method with no destination parameter, so
  a copy cannot cross into another store.
- **No plaintext.** The stored `EncryptedPassword` and `PasswordEncryption` are
  duplicated exactly as they sit on disk. The password is never decrypted, reconstructed,
  logged, or returned to the caller, and the copy succeeds even when the current user
  cannot decrypt the blob. Neither the source nor any unrelated profile is re-protected,
  so their stored ciphertext does not change.
- **It is not an upsert.** A missing source, an existing target name (compared ordinally
  and case-sensitively), an invalid metadata mutation, or a profile that fails validation
  throws and leaves the store exactly as it was.

Validation uses `SqlServerConnectionValidationPolicy.DatabaseOptional` unless you pass a
policy; no SQL connection is opened.

## One selected store, no fallback

There is deliberately no universal default store. An application picks `Shared(...)`,
`AppSpecific(...)`, or an explicit `FilePath` **once**, constructs a single
`SqlServerConnectionStore`, and injects that instance everywhere. A CLI application
built on `ItTiger.TigerQuery.CliCore` lets the run choose the path instead, through
`TigerQueryCliOptions`, and still ends up with exactly one store per run.

Every operation on that instance — `Load`, `Find`, `Exists`, `QueryByMetadata`, `Add`,
`AddOrUpdate`, `Copy`, `Save`, `Delete` — uses the file it was constructed with. Nothing
probes a default location when the selected file is missing, malformed, or inaccessible;
that condition is reported, never worked around. `store.FilePath` exposes the normalized
absolute path so diagnostics and tests can prove which store an operation used.

## Choosing which store file to use

Picking the store file **once** is the application's job, but the precedence it should
follow is standard, so `SqlServerConnectionStorePathResolver` defines it in one place:

1. an explicit path the caller supplied — a command-line option, a test fixture, an API argument;
2. the `TIGERQUERY_CONNECTION_STORE_FILE` environment variable;
3. the host application's own default location.

```csharp
var resolution = SqlServerConnectionStorePathResolver.Resolve(
    new SqlServerConnectionStorePathOptions
    {
        ExplicitFilePath = pathFromCommandLine,      // null when not supplied
        DefaultFilePath = SqlServerConnectionStoreOptions.AppSpecific("YourVendor", "your-tool").FilePath
    });

if (!resolution.IsSuccess)
    return Fail(resolution.ErrorMessage);           // never fall back on your own

var store = new SqlServerConnectionStore(
    new SqlServerConnectionStoreOptions { FilePath = resolution.FilePath! });
```

`resolution.Source` reports which of the three sources won, so diagnostics can say *why*
a particular file was used.

**A higher-priority source that speaks decides the outcome.** A source supplying no value
is skipped; a source supplying an unusable one fails. An environment variable set to an
empty string is a configuration error, not an unset variable, and neither it nor a
malformed explicit path quietly falls through to your default — that is what stops a
mis-pointed build agent from writing to a developer's personal store.

Only syntactic validation is applied: blank values, values that cannot be normalized to
an absolute path, and values with no file-name component. Resolving **touches nothing** —
no file is created, no directory is probed, no connection is opened — so it is safe to
call before you know whether a store will be used. Whether an absent store file is an
error is a policy for the code that opens the store, not for path resolution.

Set `EnvironmentReader` to supply the environment yourself; tests use it to cover
precedence without mutating process-global state. Set `EnvironmentVariableName` only if
your application must coexist with an established variable of its own.

## Authorizing a connection for end-to-end testing

Test infrastructure that creates databases and runs scripts needs a SQL Server to work
against, and the dangerous way to get one is to look for it. TigerQuery does the opposite:

> **Reachability is not authorization.** A connection may be used by E2E infrastructure
> only because someone deliberately marked it, in a store the application already selected.

Nothing here searches for instances. `.`, `(local)`, `localhost`, LocalDB, named
instances, ports, services, and containers are never tried, a reachable server is never
taken as consent, and "the first profile that works" is not a selection rule. A machine
with no marked profile resolves to `NotConfigured`, and a test suite skips.

Three reserved metadata keys carry E2E and bootstrap authorization:

```text
ittiger.e2e.enabled=true                 # this profile may be used for E2E work
ittiger.e2e.bootstrap=true               # this expected profile may act as bootstrap
ittiger.e2e.allow-database-create=true   # …and E2E work may create databases through it
```

Use the `SqlServerE2eMetadata` constants rather than the literals. The grammar is exact,
because profile metadata is compared ordinally:

- keys match as written, lower-case — `ITTIGER.E2E.ENABLED` is a different key and grants
  nothing;
- values are the literal `true` and `false`. `True`, `1`, `yes`, and `" true "` are **not**
  accepted spellings, and none of them means `false` — they are reported as malformed, so
  a typo fails loudly instead of quietly withdrawing an authorization its author believed
  they had written;
- the `ittiger.e2e.` prefix is reserved for TigerQuery. Keep application metadata under your
  own prefix; `SqlServerE2eMetadata.IsReservedKey(...)` tells you which is which.

```csharp
var profile = store.Find("tiger-sqlcmd-e2e")!;
SqlServerE2eMetadata.AuthorizeNewBootstrapProfile(
    profile,
    allowDatabaseCreation: true);
store.AddOrUpdate(profile);
```

`AuthorizeNewProfile(...)` writes general E2E authorization and never writes the
bootstrap flag. `AuthorizeNewBootstrapProfile(...)` is the TigerQuery-owned path that
writes both authorization flags and the optional database-creation permission. Generic
metadata APIs reject all `ittiger.e2e.*` writes.

### Resolving the bootstrap connection

`SqlServerE2eConnectionResolver` turns a store into one of four explicit outcomes:

```csharp
var resolution = SqlServerE2eConnectionResolver.Resolve(store,
    new SqlServerE2eConnectionResolutionOptions
    {
        ConnectionName = nameFromTheCaller,          // null when the caller named nothing
        DefaultConnectionName = "tiger-sqlcmd-e2e",  // your application's convention
        RequireDatabaseCreationPermission = true
    });

switch (resolution.Status)
{
    case SqlServerE2eResolutionStatus.Resolved:
        UseIt(resolution.Profile!);
        break;
    case SqlServerE2eResolutionStatus.NotConfigured:
        Skip();                                      // the normal state of a fresh clone
        break;
    default:
        Fail(resolution.Errors);                     // Ambiguous or Invalid
        break;
}
```

**Name and metadata have separate, required roles.** The caller's `ConnectionName`, or the
host's `DefaultConnectionName`, identifies the expected profile. Exact
`ittiger.e2e.bootstrap=true` metadata authorizes that selected profile to act as bootstrap.
Both are required. A store holding exactly one authorized bootstrap profile still resolves
nothing when no name is supplied, and an expected name without the bootstrap flag is
`Invalid`; store order never supplies either decision.

The four outcomes:

| Status | When |
| --- | --- |
| `Resolved` | one profile matched the name, is marked `enabled=true` and `bootstrap=true`, holds every requested permission, and passes `ValidateComplete`. The only status carrying a `Profile`. |
| `NotConfigured` | no name was supplied; or the host's `DefaultConnectionName` names a profile that does not exist yet. A skip, not a fault. |
| `Ambiguous` | several profiles share the requested name, or no name was supplied and several profiles are authorized. Never settled by taking the first. |
| `Invalid` | a name the *caller* supplied does not exist; the profile is not authorized; reserved metadata is malformed; a requested permission is missing; the profile fails validation; or the store file could not be read. |

Bootstrap profiles created by older TigerQuery builds do not contain
`ittiger.e2e.bootstrap=true` and now resolve as `Invalid`. After preserving any settings
you still need, delete and recreate them with `connection add-e2e-bootstrap`, or upgrade
them through `AuthorizeNewBootstrapProfile(...)` and persist the profile. Generic metadata
mutation is intentionally not a migration path.

`ConnectionName` and `DefaultConnectionName` are separate because they fail differently: a
caller who names a missing profile made a mistake and gets `Invalid`, while a host
convention nobody has set up yet is just an unconfigured machine and gets `NotConfigured`.
A present-but-blank name is an error rather than an absence, for the same reason a
present-but-empty store-path variable is.

Resolving reads the store and nothing else. It opens no connection, contacts no server,
tests no credentials, and creates nothing — so calling it on every test run, including the
runs that will skip, is safe. `Errors` and `CandidateNames` are meant to be printed and
never carry a password or a connection string.

## Concurrent and interrupted writes

Mutating operations are coordinated by normalized file path and replace the file in one
step — the new content is written to a same-directory temporary file, flushed to disk,
and then atomically moved over the destination:

- Concurrent mutations cannot lose one another's updates. The guarantee is absolute
  within a process; across processes it is provided by a sibling `<file>.lock` and holds
  for processes that use this library.
- A mutation that fails at any point leaves the previous file intact rather than
  truncated, and removes only its own temporary artifact.
- Readers never observe a partially written file, so `Load` is not coordinated.
- `SqlServerConnectionStoreOptions.MutationTimeout` (15 seconds by default) bounds the
  wait; exceeding it throws `TimeoutException` without mutating anything.

## Password protection and platforms

SQL-password profiles never store plain-text passwords by default:

- **Windows**: `DpapiConnectionPasswordProtector` encrypts the password at rest with **DPAPI (current user)**. DPAPI is Windows-only; encrypted values do not roam to other machines or users.
- **Other operating systems**: there is no DPAPI. `ConnectionPasswordProtector.CreateDefault()` falls back to `NonPersistingConnectionPasswordProtector`, which simply never saves the password — profiles still work, but the password must be supplied per session.
- `NoOpConnectionPasswordProtector` performs no protection at all and is intended for tests or externally secured stores.

The store constructor accepts an explicit `IConnectionPasswordProtector` when you need to choose the strategy yourself.

Protection is applied to the profile you supply to `Add`, `AddOrUpdate`, or `Save`, and
unprotection happens on `Load`. Profiles that a mutation merely carries along — and the
source of a `Copy` — keep their stored representation byte-for-byte, so an unrelated
`Add` or `Delete` never re-encrypts anyone else's password.

## Related packages

- [ItTiger.TigerQuery.CliCore](https://www.nuget.org/packages/ItTiger.TigerQuery.CliCore/) — ready-made TigerCli `connection` commands (list/show/add/edit/delete/clone-e2e) built on this package.
- [ItTiger.TigerQuery](https://www.nuget.org/packages/ItTiger.TigerQuery/) — the standalone sqlcmd-compatible script engine; independent of this package and easy to combine with it.

## Links

- Project page: https://www.ittiger.net/projects/tigerquery/
- Repository: https://github.com/rkozlowski/TigerQuery
- License: [MIT](https://github.com/rkozlowski/TigerQuery/blob/main/LICENSE)

An open-source project by **IT Tiger** — https://www.ittiger.net/
