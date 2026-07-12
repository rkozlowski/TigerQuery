# ItTiger.TigerQuery.Core

Reusable **SQL Server connection-profile** support for TigerQuery-family tools and any .NET application that wants named, saved connections instead of raw connection strings.

Intended consumers are tool and application developers: define profiles once (server, authentication, encryption, pooling, …), store them in a JSON file, and resolve them to `Microsoft.Data.SqlClient` connection strings by name.

## Key types

- `SqlServerConnectionProfile` — a named profile with first-class options (server, database, authentication, encryption, trust, application intent, timeouts, pooling) plus a free-form options escape hatch; builds a `SqlConnectionStringBuilder` / connection string.
- `SqlServerConnectionStore` / `SqlServerConnectionStoreOptions` — JSON file storage with `Shared(vendor)` (machine-wide per-user store shared across tools) and `AppSpecific(vendor, app)` locations, or any explicit `FilePath`.
- `SqlServerConnectionResolver` / `SqlServerConnectionResolution` — name → connection string with clean failure messages.
- `SqlServerConnectionValidator` / `SqlServerConnectionValidationPolicy` — profile validation (e.g. database required vs. optional).
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

For a real shared store, prefer `SqlServerConnectionStoreOptions.Shared("YourVendor")` (per-user application-data location on Windows, `~/.config` elsewhere) so multiple tools see the same connections.

## Password protection and platforms

SQL-password profiles never store plain-text passwords by default:

- **Windows**: `DpapiConnectionPasswordProtector` encrypts the password at rest with **DPAPI (current user)**. DPAPI is Windows-only; encrypted values do not roam to other machines or users.
- **Other operating systems**: there is no DPAPI. `ConnectionPasswordProtector.CreateDefault()` falls back to `NonPersistingConnectionPasswordProtector`, which simply never saves the password — profiles still work, but the password must be supplied per session.
- `NoOpConnectionPasswordProtector` performs no protection at all and is intended for tests or externally secured stores.

The store constructor accepts an explicit `IConnectionPasswordProtector` when you need to choose the strategy yourself.

## Related packages

- [ItTiger.TigerQuery.CliCore](https://www.nuget.org/packages/ItTiger.TigerQuery.CliCore/) — ready-made TigerCli `connections` commands (list/show/add/edit/delete) built on this package.
- [ItTiger.TigerQuery](https://www.nuget.org/packages/ItTiger.TigerQuery/) — the standalone sqlcmd-compatible script engine; independent of this package and easy to combine with it.

## Links

- Project page: https://www.ittiger.net/projects/tigerquery/
- Repository: https://github.com/rkozlowski/TigerQuery
- License: [MIT](https://github.com/rkozlowski/TigerQuery/blob/main/LICENSE)

An open-source project by **IT Tiger** — https://www.ittiger.net/
