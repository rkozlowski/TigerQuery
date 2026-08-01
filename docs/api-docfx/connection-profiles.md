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
and injects that same instance everywhere it is needed — including into
`SqlServerConnectionCommandOptions.Store` for the reusable CLI commands.

Every operation on that instance uses the file it was constructed with. Lookup,
metadata filtering, copy, add, update, save, and delete never probe a default
location, and a missing, malformed, or inaccessible selected file is reported
rather than worked around. `SqlServerConnectionStore.FilePath` exposes the
normalized absolute path so diagnostics and tests can prove which store an
operation used.

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
