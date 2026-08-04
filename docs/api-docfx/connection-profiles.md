# Connection profiles

The [ItTiger.TigerQuery.Core](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/)
package supplies reusable named SQL Server connection profiles. It can be used
with the TigerQuery engine or independently in another .NET application.

Install it with:

```console
dotnet add package ItTiger.TigerQuery.Core
```

## Save and resolve a profile

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
    Console.WriteLine("Connection string ready.");
else
    Console.WriteLine($"Failed: {resolution.ErrorMessage}");
```

Use [SqlServerConnectionStoreOptions](xref:ItTiger.TigerQuery.Core.SqlServerConnectionStoreOptions)
to select an explicit file, a per-user vendor store shared by applications, or
an app-specific per-user store. Use
[SqlServerConnectionValidator](xref:ItTiger.TigerQuery.Core.SqlServerConnectionValidator)
to enforce whether a database is optional or required;
`ValidateComplete` additionally checks credential presence and connection-string
compatibility, and is the validation a copy applies.

## Selecting one store

TigerQuery deliberately defines no universal default store. The host application
chooses `Shared(...)`, `AppSpecific(...)`, or an explicit `FilePath` once,
constructs a single
[SqlServerConnectionStore](xref:ItTiger.TigerQuery.Core.SqlServerConnectionStore),
and injects that same instance everywhere it is needed. A CLI application built
on `ItTiger.TigerQuery.CliCore` instead lets each run pick its own store, using
the deferred form described below.

Every operation on that instance uses the file it was constructed with. Lookup,
metadata filtering, copy, add, update, save, and delete never probe a default
location, and a missing, malformed, or inaccessible selected file is reported
rather than worked around. `SqlServerConnectionStore.FilePath` exposes the
normalized absolute path so diagnostics and tests can prove which store an
operation used.

### Letting a run choose the path

An application that wants a run to be able to select its own store resolves the
path through
[SqlServerConnectionStorePathResolver](xref:ItTiger.TigerQuery.Core.SqlServerConnectionStorePathResolver)
instead of hard-coding one. Its precedence is fixed:

1. an explicit path — the `--tq-connection-store-file` option in a CLI host, or
   the caller's own value in library code;
2. the `TIGERQUERY_CONNECTION_STORE_FILE` environment variable;
3. the application's default store location.

A source that supplies nothing is skipped. A source that supplies an unusable
value — blank, malformed, or naming a directory — fails resolution and is never
worked around by falling through to a lower-priority source, so a misconfigured
build agent reports its own mistake instead of silently using a developer's
personal store. Resolution is inert: it normalizes a string and reads the
environment, creating nothing and touching no file. The returned
`SqlServerConnectionStorePathResolution` carries the normalized absolute path
and which source chose it.

`tiger-sqlcmd` uses exactly this, through the CliCore contribution described in
[CLI integration](cli-integration.md#letting-a-run-select-the-store). Because
the environment variable is read by Core rather than by any one tool, a
mixed-mode workflow — a CLI step and library test code in the same job — can
agree on one store by setting that variable, since the library side has no
command line.

> [!IMPORTANT]
> The default Windows password protector is DPAPI-scoped to the current user
> and machine. Pointing a store path at a file created elsewhere does not make
> its protected passwords readable; supply the credentials on that machine
> instead of copying the store, or use an external value reference as described
> below.

## External profile values

A persisted connection value can be either a literal JSON string (the original
store contract) or a tagged external reference object. Existing stores therefore
load unchanged, while CI jobs and containers can keep credentials outside the
writable store. References are supported for server, database, SQL username,
SQL password, and a complete connection string.

```json
{
  "Name": "ci-fields",
  "Server": { "Source": "EnvironmentVariable", "Name": "TQ_SQL_SERVER" },
  "Database": { "Source": "File", "Path": "/config/sql.json", "Format": "Json", "Key": "database" },
  "Authentication": 1,
  "Username": { "Source": "File", "Path": "/run/secrets/sql-auth.json", "Format": "Json", "Key": "username" },
  "Password": { "Source": "File", "Path": "/run/secrets/sql-password", "Format": "Text" },
  "Encrypt": 1
}
```

The supported reference forms are deliberately explicit:

- `{"Source":"EnvironmentVariable","Name":"NAME"}` reads the named
  environment variable. An unset variable fails; a required field also rejects
  an empty or whitespace-only result.
- `{"Source":"File","Path":"path","Format":"Text"}` reads the entire
  UTF-8 text file exactly. No newline or whitespace trimming is performed.
- `{"Source":"File","Path":"path","Format":"Json","Key":"name"}`
  reads an exact, case-sensitive top-level property from a JSON object. The
  property must exist and be a JSON string; nested paths are not interpreted.

Relative file paths are resolved by the normal .NET file APIs against the
process working directory at effective-connection build time.

Unknown sources, incompatible properties, unreadable files, malformed JSON,
missing keys, and non-string keyed values fail clearly. Extra object properties
are tolerated so newer writers can extend the reference contract, but every
known discriminator and required source-specific property remains strict.

References are resolved only by `BuildConnectionStringBuilder`,
`BuildConnectionString`, or `SqlServerConnectionResolver` when an effective
connection is requested. Loading, validation, E2E authorization, copying,
editing, `show`, and `list` do not read the referenced environment or files.
Resolution never replaces a reference or writes its result back to the store,
and `Copy` preserves the reference object.

Library tests and hosts can inject deterministic readers without changing the
process environment:

```csharp
var effective = profile.BuildConnectionStringBuilder(
    new SqlServerExternalValueResolutionOptions
    {
        EnvironmentReader = name => testEnvironment[name],
        FileReader = path => testFiles[path]
    });
```

### Full connection-string mode

A profile may instead supply only a complete connection string:

```json
{
  "Name": "ci-full",
  "ConnectionString": {
    "Source": "EnvironmentVariable",
    "Name": "TQ_SQL_CONNECTION_STRING"
  }
}
```

Full-string mode and field mode are strictly mutually exclusive. A profile that
combines `ConnectionString` with server, database, authentication, encryption,
credentials, pooling, or free-form options fails validation; neither side takes
precedence. A legacy/plain string is also accepted by the Core model for
compatibility, although CLI setup accepts only reference objects for a complete
connection string so a secret is never required on the command line.

### Diagnostics and sensitivity

Passwords and complete literal connection strings are sensitive. Resolver
failures, exceptions, logs, and CLI inspection output never include their raw
values. `connection show` and `connection list` render references by source
description (environment-variable name or file path/key) and never resolve
them. Server, database, and username references use the same behavior even
though those destination fields are normally non-sensitive. File paths and
environment-variable names are intentionally displayable; treat the locator
itself as potentially sensitive when naming secrets in your environment.

## Copying a connection

`Copy` duplicates a saved profile under a new name **inside the same store**. It
exists so that an application deriving a connection from one a user already
approved never rebuilds a connection string or hand-copies properties:

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
```

- Every persisted field is preserved by default, carried through the profile's
  own JSON contract so that fields added in later releases copy automatically.
- External-value references remain references; copying never reads them and
  never persists a resolved value.
- Only the profile name, the initial catalog, and the named metadata entries can
  be overridden. `InitialCatalogOverride` is `null` to preserve, `""` to clear,
  or a database name to replace. Unrelated metadata survives untouched, and
  removals are applied before assignments.
- `Copy` is an instance method with no destination parameter, which is what makes
  cross-store copying impossible.
- Copy is never an upsert: a missing source, an existing target name, an invalid
  metadata mutation, or a profile that fails validation throws and leaves the
  store unchanged.
- The returned profile is detached and already persisted, so it can be resolved,
  edited, and deleted through the ordinary APIs.

See [SqlServerConnectionCopyOptions](xref:ItTiger.TigerQuery.Core.SqlServerConnectionCopyOptions).

## Concurrent and interrupted writes

`Add`, `AddOrUpdate`, `Copy`, `Delete`, and `Save` are coordinated by normalized
file path and replace the store in one step: content is written to a
same-directory temporary file, flushed to disk, and then atomically moved over
the destination.

- Concurrent mutations cannot lose one another's updates. The guarantee is
  absolute within a process; across processes it is provided by a sibling
  `<file>.lock` and holds for processes that use this library.
- A failed mutation leaves the previous file intact rather than truncated and
  removes only its own temporary artifact.
- Readers never see a partially written file, so `Load` is not coordinated.
- `SqlServerConnectionStoreOptions.MutationTimeout` bounds the wait and defaults
  to 15 seconds; exceeding it throws `TimeoutException` without mutating
  anything.

## Metadata

Applications can attach namespaced, non-secret string metadata without
affecting the generated SQL connection string:

```csharp
var profile = store.Find("local")!;
profile.SetMetadata("yourvendor.yourapp.role", "automation-host");
store.AddOrUpdate(profile);
```

Metadata comparison is ordinal and case-sensitive. Queries combine filters
with AND semantics and preserve store order. Do not store passwords, tokens,
or other secrets in metadata.

The exact lowercase `ittiger.e2e.*` namespace is reserved for TigerQuery. Generic
profile mutations and copy overrides reject both known and unknown keys in that
namespace. TigerQuery-owned creation operations may write the canonical E2E
keys, while reads tolerate unknown reserved keys written by a newer version.
The exact authorization grammar is:

```text
ittiger.e2e.enabled=true
ittiger.e2e.bootstrap=true
ittiger.e2e.allow-database-create=true
```

Keys and values are ordinal and case-sensitive: for example, `True`, `1`, and
surrounding whitespace are invalid flag values. Server reachability and a valid
ordinary profile do not authorize E2E work. Bootstrap selection is strictly by
an explicit caller name or the host-configured default name, and the selected
profile must also carry exact `ittiger.e2e.bootstrap=true` authorization. The
name identifies the expected profile; metadata authorizes it as bootstrap; both
are required. TigerQuery never discovers SQL Server instances, infers a
bootstrap from store order, or selects the sole authorized profile.

A copy preserves all metadata, then applies the removals and assignments in
`SqlServerConnectionCopyOptions` by exact key. Empty keys, null values, and a key
that appears in both the assignment and removal collections are rejected.

See [SqlServerConnectionProfile](xref:ItTiger.TigerQuery.Core.SqlServerConnectionProfile)
and [SqlServerConnectionMetadataFilter](xref:ItTiger.TigerQuery.Core.SqlServerConnectionMetadataFilter).

## Password protection

SQL-password profiles do not persist plain-text passwords by default:

- On Windows, the default protector uses DPAPI for the current user.
- On other operating systems, the default non-persisting protector clears the
  plain password before saving.
- The no-op protector is intended only for tests or externally secured stores.

Encrypted DPAPI data does not roam to other machines or users. Supply an
[IConnectionPasswordProtector](xref:ItTiger.TigerQuery.Core.IConnectionPasswordProtector)
when an application needs an explicit strategy.

Protection applies to the profile passed to `Add`, `AddOrUpdate`, or `Save`;
unprotection happens on `Load`. Profiles a mutation merely carries along keep
their stored representation byte-for-byte, so an unrelated add or delete never
re-encrypts another profile's password.

`Copy` duplicates the stored `EncryptedPassword` and `PasswordEncryption`
exactly. The password is never decrypted, reconstructed, logged, or returned to
the caller as plaintext, and the copy succeeds even when the current user cannot
decrypt the blob — whether it is usable afterwards follows the normal resolver
and protector behavior.
