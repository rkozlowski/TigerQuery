# TigerQuery connection-store resolution and safe E2E foundation

Status: proposed design

Scope:

- `ItTiger.TigerQuery.Core`
- `ItTiger.TigerQuery.CliCore`
- TigerCli-based host applications such as `tiger-sqlcmd` and TigerWrap
- third-party .NET applications consuming TigerQuery libraries directly
- local development, CI/CD, containers, and mixed library/tool workflows

## 1. Purpose

TigerQuery already provides a reusable SQL Server connection store with named profiles, metadata, protected credentials, copying, validation, and safe mutation.

The next step is to make that store the standard configuration and safety boundary for SQL Server E2E testing.

The design must make the safe path obvious and easy:

- a developer can configure one bootstrap E2E connection in the normal application connection store;
- a test suite can use that connection without discovering or probing SQL Server instances;
- CI/CD can provide an alternate writable store through configuration;
- TigerCli-based applications can expose a standard global store-path option without adding TigerQuery-specific concepts to TigerCli itself;
- library-only applications can use the same resolution behavior without depending on TigerCli;
- TigerWrap, `tiger-sqlcmd`, and third-party applications do not invent their own metadata keys, precedence rules, or connection-discovery logic.

The governing safety rule is:

> SQL Server availability is not authorization. E2E infrastructure may be used only through an explicitly configured TigerQuery connection store and an explicitly authorized connection profile.

## 2. Core principles

### 2.1 No discovery

TigerQuery test infrastructure must never:

- search for local SQL Server instances;
- probe `.`, `(local)`, `localhost`, LocalDB, named instances, ports, services, Docker containers, or network hosts;
- reuse a connection string found in source code, logs, history, caches, or another repository;
- choose the first reachable SQL Server;
- infer consent from successful connectivity;
- persist discovered machine-specific connection details.

When no suitable E2E connection is configured, the correct result is `NotConfigured` or an equivalent safe outcome.

### 2.2 Explicit opt-in

E2E use must be explicitly enabled through connection metadata owned by TigerQuery.

Possessing a valid connection profile is not enough. An ordinary development or production profile must never become an E2E bootstrap connection implicitly.

### 2.3 One shared contract

The following must be defined once, in TigerQuery:

- connection-store path resolution;
- standard environment-variable names;
- E2E metadata keys and meanings;
- bootstrap profile resolution;
- ambiguity handling;
- validation and authorization rules;
- safe database-creation permissions;
- behavior when configuration is missing or invalid.

Applications consume the contract. They do not redefine it.

### 2.4 Generic framework, domain contribution

TigerCli remains generic.

TigerCli should provide only the generic app-contribution mechanism for registering and processing global options.

`ItTiger.TigerQuery.CliCore` owns TigerQuery-specific global options such as:

```text
--tq-connection-store-file <path>
```

Host applications opt into the contribution.

### 2.5 Local developer convenience

Local developers should not be required to configure environment variables.

The normal developer experience uses the host application's default connection-store file in the user's profile.

Environment variables remain useful for CI/CD, containers, build agents, and automation.

## 3. Package responsibilities

## 3.1 `ItTiger.TigerQuery.Core`

Core owns all reusable connection-store and E2E contracts.

Responsibilities:

- resolve the selected connection-store path;
- define the standard environment variable for store-path override;
- accept an explicit path override from callers;
- use the host application's default store path when no override exists;
- normalize and validate the selected path;
- report which source selected the path;
- define reserved E2E metadata keys;
- resolve and validate E2E bootstrap profiles without opening SQL connections;
- reject ambiguous or invalid E2E configuration;
- support future external-value references for profile fields;
- remain independent of TigerCli and host-specific UI.

Core must work equally for:

- command-line applications;
- test libraries;
- desktop applications;
- services;
- build agents;
- containers;
- custom automation.

## 3.2 `ItTiger.TigerQuery.CliCore`

CliCore bridges TigerQuery domain behavior into TigerCli-based applications.

Responsibilities:

- contribute the TigerQuery global connection-store option;
- contribute environment-variable help metadata;
- store the parsed explicit override;
- pass that override to the Core resolver;
- provide reusable connection commands;
- provide future E2E setup and validation commands;
- allow the host application to configure an optional default E2E connection name;
- keep all TigerQuery-specific option names and semantics outside TigerCli.

CliCore must not duplicate:

- Core path precedence;
- E2E metadata rules;
- profile resolution;
- authorization checks;
- connection-store validation.

## 3.3 TigerCli

TigerCli owns only generic composition mechanics:

- app-contribution registration;
- global-option registration;
- option parsing;
- validation plumbing;
- help placement;
- localization hooks;
- contribution callbacks;
- access to parsed values.

TigerCli must not define or understand:

- TigerQuery connection stores;
- `--tq-connection-store-file`;
- E2E connection metadata;
- SQL Server concepts.

## 3.4 Host applications

Examples:

- `tiger-sqlcmd`
- TigerWrap
- third-party TigerCli-based applications

Host responsibilities:

- opt into the CliCore contribution;
- provide the application's default user-profile store path;
- optionally define a default E2E bootstrap connection name;
- pass the shared contribution state to commands and services;
- decide user-facing command grouping and branding;
- avoid independent store-path or E2E resolution logic.

## 4. Connection-store path resolution

Core should expose one reusable resolver.

Conceptually:

```csharp
public sealed class SqlServerConnectionStorePathOptions
{
    public string? ExplicitPath { get; init; }

    public required string DefaultPath { get; init; }

    public string EnvironmentVariableName { get; init; }
        = SqlServerConnectionStoreEnvironment.ConnectionStoreFile;
}
```

```csharp
public sealed class SqlServerConnectionStorePathResolution
{
    public required string Path { get; init; }

    public required SqlServerConnectionStorePathSource Source { get; init; }
}
```

```csharp
public enum SqlServerConnectionStorePathSource
{
    Explicit = 0,
    EnvironmentVariable = 1,
    ApplicationDefault = 2
}
```

Exact names may differ, but the contract should be clear.

## 4.1 Precedence

The final precedence is:

1. explicit command-line or API override;
2. TigerQuery-defined environment variable;
3. host application's default user-profile store path.

For TigerCli-based applications:

```text
global CLI option
    >
environment variable
    >
application default store
```

For library-only applications:

```text
explicit API override
    >
environment variable
    >
application default store
```

## 4.2 Failure behavior

An invalid higher-priority value must fail explicitly.

Examples:

- command-line option points to an invalid path;
- environment variable is present but empty;
- environment variable contains an invalid path;
- explicit path cannot be normalized.

The resolver must not silently fall through to a lower-priority source.

This prevents a misconfigured CI job from unexpectedly using a developer's default user-profile store.

## 4.3 Resolution must be inert

Resolving the store path must not:

- create the store file;
- open SQL connections;
- probe the filesystem beyond path normalization and required validation;
- discover SQL Server instances;
- choose a connection profile.

Store-path resolution and connection-profile resolution are separate concerns.

## 5. TigerCli global option contribution

`ItTiger.TigerQuery.CliCore` should define a TigerCli app contribution for:

```text
--tq-connection-store-file <path>
```

Recommended properties:

- global within the host application;
- optional;
- non-promptable;
- no short alias;
- no hidden fallback behavior;
- available to every command using the shared connection store;
- documented together with the standard environment variable;
- parsed once and reused everywhere.

The host application registers the contribution through TigerCli's app-contribution mechanism.

Conceptually:

```csharp
var tigerQueryContribution =
    new TigerQueryCliContribution(new TigerQueryCliContributionOptions
    {
        DefaultConnectionStoreFile = appDefaultStorePath,
        DefaultE2eConnectionName = "app-e2e"
    });

app.AddContribution(tigerQueryContribution);
```

The exact API should follow TigerCli's contribution conventions.

## 5.1 Shared resolved state

CliCore should expose a shared state/service rather than forcing commands to inspect parse results independently.

Conceptually:

```csharp
public sealed class TigerQueryCliEnvironment
{
    public string? ExplicitConnectionStoreFile { get; internal set; }

    public string? DefaultE2eConnectionName { get; init; }

    public SqlServerConnectionStorePathResolution ResolveStorePath();
}
```

Every connection and execution command must use the same resolved store.

## 6. Default E2E bootstrap connection

A host application may define an expected default E2E connection name.

Examples:

```text
tigerwrap-e2e
tigerquery-e2e
myapp-e2e
```

Requirements:

- configured by the host through CliCore;
- default value is unset;
- Core does not invent an application-specific name;
- no implicit selection occurs when the host has not configured one;
- the profile must still carry explicit TigerQuery E2E authorization metadata;
- profile name alone is not authorization.

Possible CliCore configuration:

```csharp
new TigerQueryCliContributionOptions
{
    DefaultE2eConnectionName = "tigerwrap-e2e"
}
```

Possible CLI override:

```text
--default-e2e-connection-name <name>
```

Whether that should be a user-facing global option or a host-only configuration value remains an API-design decision.

A simple initial model is:

- host configures the expected default name;
- automation may supply an explicit connection name through the E2E API or command;
- Core never selects an arbitrary marked profile.

## 7. Standard E2E metadata contract

TigerQuery Core should reserve and document namespaced metadata keys.

Initial proposal:

```text
ittiger.e2e.enabled=true
ittiger.e2e.allow-database-create=true
```

Possible future keys:

```text
ittiger.e2e.database-prefix=TigerQuery_E2E_
ittiger.e2e.owner=<application-or-suite>
ittiger.e2e.purpose=<purpose>
```

Core should expose constants rather than requiring applications to copy string literals.

Conceptually:

```csharp
public static class SqlServerE2eMetadata
{
    public const string Enabled =
        "ittiger.e2e.enabled";

    public const string AllowDatabaseCreation =
        "ittiger.e2e.allow-database-create";
}
```

## 7.1 Authorization semantics

A profile qualifies as an E2E bootstrap profile only when:

- `ittiger.e2e.enabled` is present;
- its value is valid and evaluates to `true`;
- the profile passes complete structural validation;
- any requested operation is explicitly allowed.

Database-creating workflows additionally require:

```text
ittiger.e2e.allow-database-create=true
```

Ordinary profiles are ignored, even when they are valid and connect successfully.

## 7.2 No connection during resolution

E2E profile resolution must inspect only store data and metadata.

It must not:

- connect to SQL Server;
- validate server reachability;
- test credentials;
- inspect database permissions;
- create a database.

Connectivity validation should be a separate explicit operation.

## 8. E2E resolver API

Core should provide a reusable E2E resolver with explicit outcomes.

Conceptually:

```csharp
public enum SqlServerE2eResolutionStatus
{
    NotConfigured = 0,
    Resolved = 1,
    Ambiguous = 2,
    Invalid = 3
}
```

```csharp
public sealed class SqlServerE2eConnectionResolution
{
    public required SqlServerE2eResolutionStatus Status { get; init; }

    public SqlServerConnectionProfile? Profile { get; init; }

    public IReadOnlyList<string> CandidateNames { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; }
        = Array.Empty<string>();
}
```

Possible resolver:

```csharp
public sealed class SqlServerE2eConnectionResolver
{
    public SqlServerE2eConnectionResolution Resolve(
        SqlServerConnectionStore store,
        SqlServerE2eConnectionResolutionOptions? options = null);
}
```

Options may include:

```csharp
public sealed class SqlServerE2eConnectionResolutionOptions
{
    public string? ConnectionName { get; init; }

    public bool RequireDatabaseCreationPermission { get; init; }
}
```

## 8.1 Resolution behavior

- explicit connection name:
  - profile must exist;
  - profile must be E2E-enabled;
  - requested permissions must be present;
  - otherwise return `Invalid`;
- configured default connection name:
  - same checks as explicit selection;
- no selected name:
  - zero enabled profiles returns `NotConfigured`;
  - one enabled profile may resolve only if this behavior is deliberately approved;
  - multiple enabled profiles returns `Ambiguous`;
  - never use "first profile wins."

A stricter initial design may require a name always, avoiding implicit selection completely.

## 9. CLI commands for easy developer setup

Developers should not need to know metadata keys.

CliCore should provide reusable commands such as:

```text
connection e2e enable <connection-name>
connection e2e disable <connection-name>
connection e2e show [connection-name]
connection e2e validate [connection-name]
```

For database creation:

```text
connection e2e enable <connection-name> --allow-database-create
```

Possible creation workflow:

```text
connection add <connection-name> ...
connection e2e enable <connection-name> --allow-database-create
```

Or a combined command later:

```text
connection e2e add <connection-name> ...
```

The first implementation should prefer reuse of existing connection commands over duplicating profile creation.

## 9.1 Non-interactive setup

CI/CD and automation need a fully non-interactive path.

The host application's connection command must support all required values through options or external-value references.

No secret should be forced through an interactive prompt.

This may require non-promptable options for:

- external password source;
- external connection-string source;
- server source;
- store path;
- E2E metadata authorization;
- database-creation permission.

## 9.2 Validation modes

Recommended distinction:

```text
connection e2e validate <name>
```

Validates:

- profile existence;
- required metadata;
- structural completeness;
- external-value reference shape;
- requested authorization.

It does not connect.

An explicit connectivity check may be:

```text
connection e2e validate <name> --connect
```

Connectivity remains opt-in.

## 10. External value references

For CI/CD and container use, the writable JSON connection store should be able to reference values supplied externally.

The store itself remains editable so the workflow can:

- use the bootstrap connection;
- create a new test database;
- add a database-specific profile;
- run scripts and tests;
- delete the profile;
- drop the database.

Secrets need not be stored directly in the writable JSON file.

## 10.1 Supported sources

A profile value may be supplied as:

- literal JSON value;
- environment-variable reference;
- whole-file reference;
- keyed file reference.

Conceptually:

```json
{
  "Source": "EnvironmentVariable",
  "Name": "TIGER_E2E_SQL_PASSWORD"
}
```

```json
{
  "Source": "File",
  "Path": "/run/secrets/sql-password",
  "Format": "Text"
}
```

```json
{
  "Source": "File",
  "Path": "/run/secrets/sql",
  "Format": "Json",
  "Key": "password"
}
```

## 10.2 Applicable profile values

External references may provide:

- password;
- username;
- server or SQL instance address;
- initial catalog;
- full SQL connection string;
- other future profile properties where justified.

Server address is not normally secret, but the same external-value abstraction can supply it through:

- environment variable;
- mounted configuration file;
- Kubernetes ConfigMap-mounted file;
- keyed JSON file;
- literal store value.

Using one generic external-value system avoids separate secret and non-secret reference models.

## 10.3 Full connection string versus fields

The model should support either:

- a full externally supplied connection string; or
- individually configured fields.

These modes should be mutually exclusive or have explicitly defined precedence.

A whole connection string may be useful for simple CI integration.

Individual fields are better when the workflow needs to override `InitialCatalog` for a generated test database.

## 10.4 Security behavior

External values must:

- resolve only when building the effective connection;
- never be written back into the JSON store;
- be preserved as references when profiles are copied;
- fail clearly when missing;
- be redacted in logs and diagnostics when sensitive;
- never appear in exception messages as raw values;
- never be printed by CLI inspection commands.

## 11. Main use cases

## 11.1 Normal local development

The developer creates one bootstrap connection in the normal app store:

```text
tigerwrap connection add tigerwrap-e2e ...
tigerwrap connection e2e enable tigerwrap-e2e --allow-database-create
```

After that:

- Visual Studio tests;
- Rider tests;
- `dotnet test`;
- local scripts;
- app-library workflows

can all resolve the standard E2E profile without environment variables.

No profile means no E2E access.

## 11.2 Alternate local store

A developer can isolate work with:

```text
tigerwrap connection list --tq-connection-store-file C:\temp\e2e.json
```

Useful for:

- experiments;
- isolated branches;
- reproductions;
- separate test environments;
- temporary app configurations.

The explicit path overrides the environment variable and default store.

## 11.3 CI/CD

A pipeline creates or mounts a writable runtime store and sets the TigerQuery store-path environment variable.

Example:

```text
TIGERQUERY_CONNECTION_STORE=/workspace/runtime/connections.json
```

The store may contain external references to:

- secret environment variables;
- mounted secret files;
- mounted configuration files.

No TigerCli dependency is required for library-only CI consumers.

## 11.4 Containers

A container may receive:

- writable runtime JSON store;
- read-only mounted secret files;
- mounted configuration files;
- environment variables for non-file configuration.

TigerQuery resolves the store path through the standard environment variable.

The store references external values and remains writable for temporary connection creation.

## 11.5 Library mode

A third-party application uses:

- `ItTiger.TigerQuery.Core` for store resolution and E2E authorization;
- `ItTiger.TigerQuery` for SQL execution.

Typical workflow:

1. resolve the configured store;
2. resolve the authorized bootstrap profile;
3. create a uniquely named test database;
4. copy or add a database-specific profile;
5. run schema scripts;
6. load test data;
7. execute tests;
8. remove the temporary profile;
9. drop the test database safely.

## 11.6 Tool mode

A third-party developer uses `tiger-sqlcmd` to:

1. configure or validate the bootstrap profile;
2. create the test database;
3. copy/add the database-specific profile;
4. run schema and data scripts;
5. run test queries;
6. capture CSV and error output;
7. remove the temporary profile;
8. drop the test database.

`tiger-sqlcmd` remains a CLI over reusable TigerQuery APIs.

## 11.7 Mixed mode

Mixed mode is first-class.

Examples:

- library code creates the database and runtime store;
- `tiger-sqlcmd` runs deployment scripts and exports CSV;
- library code performs assertions and cleanup.

Or:

- `tiger-sqlcmd` creates/configures the test environment;
- test code consumes the same store through Core;
- `tiger-sqlcmd` performs teardown.

Both modes share:

- store format;
- metadata contract;
- external references;
- naming rules;
- authorization checks;
- cleanup safety.

## 12. Safe database lifecycle

Future reusable E2E APIs should support safe database lifecycle operations.

Every created database should use a recognizable generated name, for example:

```text
TigerQuery_E2E_<application>_<run-id>_<random>
```

Before database creation or deletion:

- profile must be E2E-enabled;
- database creation permission must be enabled;
- name must match the approved prefix/pattern;
- cleanup must verify ownership or test provenance;
- arbitrary database names must be rejected.

Abandoned-run cleanup must never drop databases merely because they look old or are reachable.

Possible ownership evidence:

- generated name plus known prefix;
- run identifier;
- extended property inside the database;
- metadata table;
- external run manifest.

The final ownership mechanism needs separate design.

## 13. Default test behavior

A normal repository clone followed by:

```text
dotnet test
```

must be safe.

It must not:

- discover SQL Server;
- connect to SQL Server;
- create databases;
- modify existing databases;
- read unrelated cached credentials;
- use ordinary connection profiles as E2E profiles.

When E2E configuration is absent:

- E2E tests report `NotConfigured`;
- framework-specific adapters may skip them;
- no network connection is attempted.

This behavior should be protected by tests proving zero connection attempts.

## 14. Testing requirements

## 14.1 Core path resolution

Test:

- explicit path wins;
- environment variable wins over default;
- default used when no override exists;
- invalid explicit path does not fall through;
- invalid environment variable does not fall through;
- selected path is normalized;
- source is reported correctly;
- no file or SQL access occurs during resolution.

## 14.2 E2E metadata

Test:

- ordinary profile ignored;
- enabled profile accepted;
- invalid Boolean metadata rejected;
- database creation requires explicit permission;
- explicit name must still be authorized;
- ambiguity never selects the first profile;
- missing configuration returns `NotConfigured`;
- resolution opens no SQL connection.

## 14.3 CliCore contribution

Test:

- contribution registration;
- global option parsing;
- option availability across commands;
- non-promptable behavior;
- no short alias;
- CLI override passed into Core;
- environment-variable help contribution;
- host default path honored;
- shared state used by connection and execution commands.

## 14.4 Host integration

Test in `tiger-sqlcmd` and TigerWrap:

- host opts into contribution;
- no duplicate option implementation;
- same store used by connection and run commands;
- existing default behavior unchanged;
- explicit alternate store works;
- environment-variable store works;
- CLI override wins over environment variable.

## 14.5 Safety regressions

Add explicit tests proving:

- no SQL Server discovery code;
- no fallback connection string;
- no automatic localhost/LocalDB use;
- no profile selection without E2E metadata;
- no network access when E2E is not configured;
- secrets are redacted;
- external values are not persisted into the store.

## 15. Documentation requirements

Documentation should include:

- developer one-time setup;
- CI/CD setup;
- container setup;
- CLI/API precedence;
- standard environment variable;
- standard E2E metadata;
- explicit warning that access is not authorization;
- no-discovery policy;
- examples for literal, environment, and file-based values;
- mixed-mode examples;
- cleanup safety requirements;
- AI-agent guidance.

Recommended explicit instruction:

> Never discover or probe for SQL Server instances. Resolve E2E access only through TigerQuery's connection-store and E2E authorization APIs. When the resolver reports `NotConfigured`, stop or skip without opening a connection.

## 16. Phased implementation

### Phase 1: shared store-path resolution

In Core:

- standard environment-variable constant;
- explicit/environment/default resolver;
- normalized result and source;
- strict failure behavior;
- tests and documentation.

In CliCore:

- TigerCli app contribution;
- `--tq-connection-store-file`;
- environment help;
- shared state;
- host configuration for default store path.

In hosts:

- register contribution;
- remove duplicate store-path logic.

### Phase 2: E2E metadata and resolver

In Core:

- reserved metadata constants;
- resolver outcomes;
- authorization validation;
- default/explicit name handling;
- no-connect guarantees.

In CliCore:

- E2E enable/disable/show/validate commands;
- host-configured default E2E connection name.

### Phase 3: external value references

In Core:

- external-value model;
- environment source;
- file source;
- keyed-file source;
- sensitivity/redaction rules;
- full connection-string support;
- field-level support.

In CliCore:

- non-interactive configuration options;
- safe display and validation.

### Phase 4: database lifecycle helpers

Reusable APIs for:

- safe generated names;
- database creation;
- profile copy/add;
- script deployment;
- cleanup authorization;
- abandoned-run ownership checks.

### Phase 5: first-party E2E migration

- remove SQL Server discovery from TigerQuery tests;
- remove discovery from TigerWrap tests;
- use the shared resolver;
- prove default `dotnet test` is inert;
- add real `tiger-sqlcmd` external-process E2E tests;
- document local and CI workflows.

## 17. Open questions

1. Exact environment-variable name for the store path.
2. Exact public API names for path resolution.
3. Whether E2E resolution may select the only enabled profile when no name is supplied, or must always require a configured/explicit name.
4. Whether `--default-e2e-connection-name` is user-facing or host configuration only.
5. Exact CLI command hierarchy for E2E setup.
6. External-value JSON contract and compatibility strategy.
7. Supported keyed-file formats in the first version.
8. Full connection-string versus field-level precedence.
9. Database ownership marker used for safe cleanup.
10. Whether database lifecycle helpers belong entirely in Core or partly in `ItTiger.TigerQuery`.
11. How test frameworks should map `NotConfigured` to skip behavior without coupling Core to xUnit, NUnit, or MSTest.

## 18. Acceptance criteria

The design is successful when:

- TigerCli remains domain-neutral;
- CliCore contributes the TigerQuery global option;
- Core owns environment/default/explicit resolution;
- CLI overrides environment variables;
- environment variables override the application default;
- local developers need no environment variables;
- CI/CD can use environment variables and mounted files;
- E2E profiles require explicit TigerQuery metadata;
- no component discovers SQL Server;
- no unconfigured test run opens a SQL connection;
- library, tool, and mixed modes use identical contracts;
- TigerWrap and `tiger-sqlcmd` reuse the same implementation;
- third-party developers can adopt the same safe workflow without inventing their own conventions.
