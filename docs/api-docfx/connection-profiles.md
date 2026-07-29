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
to enforce whether a database is optional or required.

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
