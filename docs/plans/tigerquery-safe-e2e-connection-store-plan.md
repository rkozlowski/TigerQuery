# TigerQuery connection-store resolution and safe E2E foundation

Status: Phases 1–5 completed; Phases 6–8 proposed

Scope:

- `ItTiger.TigerQuery.Core`
- `ItTiger.TigerQuery.CliCore`
- `tiger-sqlcmd`
- contracts implemented and exercised within this repository
- local development, CI/CD, containers, and mixed library/tool workflows

## 0. Normative external reference

The CLI integration in this plan is governed by the TigerCli app-contribution model:

> **[TigerCli — App contributions and global options](https://github.com/rkozlowski/TigerCli/blob/main/docs/guides/app-contributions.md)** (`docs/guides/app-contributions.md` in the TigerCli repository).

That guide is **normative** for everything in sections 5 and 6 and for phases 2 and 3.
An implementation agent must read it before writing CliCore or host code. Where this
plan and the guide disagree, the guide wins and this plan is the document that must be
corrected.

The API described there ships in **ItTiger.TigerCli 0.9.1**, which every project in this
repository already references. No TigerCli change is required to start, and no TigerCli
change may be introduced to accommodate TigerQuery.

### 0.1 The 0.9.1 contribution surface, as actually shipped

Verified against the `ItTiger.TigerCli` 0.9.1 reference assembly and XML documentation:

| Member | Namespace `ItTiger.TigerCli.Commands` |
| --- | --- |
| `ITigerCliAppContribution` | `void Configure(TigerCliAppContributionBuilder builder)` |
| `TigerCliAppBuilder.AddContribution(ITigerCliAppContribution)` | host opt-in; contribution is configured during `Build()` |
| `TigerCliAppContributionBuilder.GlobalOptions` | returns `TigerCliGlobalOptionBuilder` |
| `TigerCliAppContributionBuilder.AddEnvironmentVariable(string name, string description)` | help metadata only, surfaced by `--help-env` |
| `TigerCliGlobalOptionBuilder.AddOptionalString(string name, string valueName, string description, Func<TigerCliGlobalOptionContext, string?, TigerCliValidationResult> apply)` | the only contributed-option shape in 0.9.1 |
| `TigerCliGlobalOptionContext` | `Culture`, `InteractionMode` |
| `TigerCliValidationResult` | `IsValid`, `ErrorMessage`, `Success()`, `Error(string)` |

### 0.2 Division of ownership required by the guide

- **TigerCli owns** metadata registration, option-name validation, parsing, duplicate
  and reserved-name collision detection, help rendering and placement, and invoking
  each contributed callback exactly once per command run.
- **CliCore (the contributing library) owns** the option name, value placeholder,
  description, semantics, validation logic, and the destination state that receives the
  value.
- **Core owns** environment-variable reading, precedence, path resolution and
  normalization, and all domain validation.
- **`tiger-sqlcmd` owns** the decision to register the contribution, the
  application default store path, the optional default bootstrap name, and the wiring
  of contribution-owned state into its command factories and services.

### 0.3 Constraints this plan must respect

Taken directly from the guide and the 0.9.1 XML documentation:

1. A contributed global option is an **optional string** with exactly **one canonical
   `--` long name**. No short name, no alias, no prompting, no "required" form, and no
   TigerCli-side environment-variable lookup.
2. `AddEnvironmentVariable` is **help metadata only**. "TigerCli displays the name and
   description ... it does not read, parse, or apply the variable." The library performs
   the actual lookup.
3. The callback is invoked **once per command run**, **with `null` when the option is
   absent**, and **before command binding, prompting, and validation**. Returning
   `TigerCliValidationResult.Error(...)` halts the run before binding.
4. **Help rendering does not invoke callbacks.** Any state a help path needs must be
   available without the callback having run.
5. Supplying the option **more than once is an error**; TigerCli does not take the last
   value. Supplying it **without a value is an argument error**.
6. Placement follows TigerCli's grammar: the option appears **after the command path and
   any required positional arguments**, in the command's normal options area.
   `my-app --acme-config x project` is invalid; `my-app project --acme-config x` is
   valid. "App-wide in meaning" does not make it valid before the command path.
   Values beginning with `-` require the `--name=value` form.
7. Duplicate, reserved, or conflicting contributed option and environment-variable names
   **fail during `Build()`**, not at run time.

### 0.4 Constraints the guide implies that this plan previously got wrong

These were discovered while reconciling the plan with the shipped API and are now
reflected in the phase plan:

- **Contributions cannot add commands, groups, providers, or resources.** In 0.9.1 a
  contribution may add optional string global options and environment-variable help
  metadata, and nothing else. `connections add-e2e-bootstrap` must therefore be mounted
  through the existing `SqlServerConnectionCommands.Configure(group, ...)` entry point,
  not through the contribution. The contribution and the command group are two separate
  opt-ins that the host performs.
- **The contribution callback runs after `Build()`, but `SqlServerConnectionCommands.Configure`
  runs during `Build()`.** Phase 2 solved this by adding deferred store selection through
  `TigerQueryCliOptions`; command factories capture an accessor to that state rather than
  an already-constructed store. The remaining fixed
  `SqlServerConnectionCommandOptions.Store` property is cleanup, not a supported
  integration path (see section 6.2).
- **Contributed option and environment-variable descriptions cannot be localized through
  TigerCli's resource pipeline in 0.9.1.** `AddOptionalString` and `AddEnvironmentVariable`
  take literal description strings and offer no `descriptionResourceKey` overload, and
  `Configure` runs at `Build()` time, before `--culture` is resolved. Validation error
  messages *can* be localized because the callback receives `TigerCliGlobalOptionContext.Culture`.
  See open question 14.
- **`--tq-connection-store-file` is not a command-setting option.** It must not appear on
  `SqlServerConnectionSettings` or any other `TigerCliSettings` type, must not be bound,
  prompted, or provider-backed, and must not be duplicated per command.

## 1. Purpose

TigerQuery already provides a reusable SQL Server connection store with named profiles,
metadata, protected credentials, copying, validation, and safe mutation.

The next step is to make that store the standard configuration and safety boundary for
SQL Server E2E testing.

The design must make the safe path obvious and easy:

- a developer can configure one bootstrap E2E connection in the normal application connection store;
- a test suite can use that connection without discovering or probing SQL Server instances;
- CI/CD can provide an alternate writable store through configuration;
- `tiger-sqlcmd` exposes a standard global store-path option without adding
  TigerQuery-specific concepts to TigerCli itself;
- library-only workflows in this repository use the same resolution behavior without
  depending on TigerCli;
- `tiger-sqlcmd` and TigerQuery's library/test workflows do not invent separate metadata
  keys, precedence rules, or connection-discovery logic.

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

TigerCli provides only the generic app-contribution mechanism described in section 0.

`ItTiger.TigerQuery.CliCore` owns TigerQuery-specific global options such as:

```text
--tq-connection-store-file <path>
```

`tiger-sqlcmd` opts into the contribution. There is exactly one global-option
mechanism in play — TigerCli app contributions. No parallel mechanism (ambient statics,
pre-parsed `args` scanning, a TigerQuery-specific pre-pass over the command line, or an
environment variable consulted by TigerCli) may be introduced.

### 2.5 Local developer convenience

Local developers should not be required to configure environment variables.

The normal developer experience uses the host application's default connection-store file in the user's profile.

Environment variables remain useful for CI/CD, containers, build agents, and automation.

## 3. Package responsibilities

### 3.1 `ItTiger.TigerQuery.Core`

Core owns all reusable connection-store and E2E contracts.

Responsibilities:

- resolve the selected connection-store path;
- define the standard environment variable name for the store-path override, **and read it**;
- accept an explicit path override from callers;
- use the host application's default store path when no override exists;
- normalize and validate the selected path;
- report which source selected the path;
- define reserved E2E metadata keys and their value grammar;
- resolve and validate E2E bootstrap profiles without opening SQL connections;
- reject ambiguous or invalid E2E configuration;
- support future external-value references for profile fields;
- remain independent of TigerCli and host-specific UI.

Within this repository, Core must work equally for command-line execution, test-library
use, build agents, containers, and custom automation.

Core must not reference `ItTiger.TigerCli`, must not know the option name
`--tq-connection-store-file`, and must not produce CLI-formatted messages.

### 3.2 `ItTiger.TigerQuery.CliCore`

CliCore bridges TigerQuery domain behavior into TigerCli-based applications.

Responsibilities:

- implement `ITigerCliAppContribution`;
- contribute the TigerQuery global connection-store option and its help text;
- contribute environment-variable help metadata for the Core-defined variable name;
- hold the parsed explicit override in contribution-owned state;
- pass that override to the Core resolver and expose the single selected store;
- provide reusable connection commands, including the deferred-store wiring they need;
- provide the bootstrap creation command;
- allow the host application to configure the application default store path and an
  optional default E2E bootstrap connection name;
- keep all TigerQuery-specific option names and semantics outside TigerCli.

CliCore must not duplicate:

- Core path precedence;
- environment-variable reading;
- E2E metadata rules;
- profile resolution;
- authorization checks;
- connection-store validation.

CliCore's callback may *invoke* Core validation and surface its result as a
`TigerCliValidationResult`; it must not reimplement it.

### 3.3 TigerCli

TigerCli owns only generic composition mechanics:

- app-contribution registration;
- global-option registration and name validation;
- option parsing and argument errors (missing value, repeated occurrence);
- collision detection at `Build()`;
- help placement and rendering, including `--help-env`;
- interaction-mode and culture resolution;
- contribution callback invocation;
- validation plumbing (`TigerCliValidationResult`).

TigerCli must not define, read, or understand:

- TigerQuery connection stores;
- `--tq-connection-store-file`;
- `TIGERQUERY_CONNECTION_STORE_FILE` or any other TigerQuery environment variable;
- E2E connection metadata;
- SQL Server concepts.

### 3.4 `tiger-sqlcmd`

Phase 3 completed the repository's host integration. Its responsibilities are:

- construct one `TigerQueryCliOptions` and one CliCore contribution **exactly once**;
- register it through `TigerCliAppBuilder.AddContribution(...)`;
- provide the application's default user-profile store path to the contribution;
- optionally define a default E2E bootstrap connection name;
- pass **the same contribution-owned state object** to `SqlServerConnectionCommands.Configure(...)`,
  to its own command factories, and to any service that reads or writes connections;
- decide user-facing command grouping and branding;
- avoid independent store-path or E2E resolution logic.

The same `TigerQueryCliOptions` instance is constructor-injected into both command
factories and captured by the app-level connection provider. It is also supplied to
`SqlServerConnectionCommands.Configure`. This replaces the old static ambient store and
ensures one run cannot resolve one store and use another.

## 4. Connection-store path resolution

Core exposes one reusable resolver. The completed Phase 1 contract is:

```csharp
public sealed class SqlServerConnectionStorePathOptions
{
    /// <summary>The caller-supplied override; from the CLI option in CLI hosts.</summary>
    public string? ExplicitFilePath { get; init; }

    /// <summary>The host application's default store path. Required.</summary>
    public required string DefaultFilePath { get; init; }

    public string EnvironmentVariableName { get; init; }
        = SqlServerConnectionStoreEnvironment.ConnectionStoreFile;

    /// <summary>
    /// Environment lookup, injected so tests need not mutate process environment.
    /// Defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>.
    /// </summary>
    public Func<string, string?>? EnvironmentReader { get; init; }
}
```

```csharp
public sealed class SqlServerConnectionStorePathResolution
{
    public bool IsSuccess { get; }
    public string? FilePath { get; }
    public SqlServerConnectionStorePathSource Source { get; }
    public SqlServerConnectionStorePathError Error { get; }
    public string? AttemptedValue { get; }
    public string? ErrorMessage { get; }
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

The injected `EnvironmentReader` is not cosmetic: without it, every precedence test must mutate
process-global state, which is unsafe alongside the existing parallel test suites.

### 4.1 Precedence

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

### 4.2 Failure behavior

An invalid higher-priority value must fail explicitly.

"Invalid" means, precisely:

- the value is empty or whitespace;
- the value cannot be normalized to an absolute path (illegal characters, malformed
  root, path too long for the platform);
- the normalized value has no file-name component, including a root or a path ending in
  a directory separator.

The resolver must not silently fall through to a lower-priority source when a
higher-priority source is present but invalid. A present-and-empty environment variable
is an error, not an absent variable.

This prevents a misconfigured CI job from unexpectedly using a developer's default
user-profile store.

**Relative paths.** A relative explicit or environment value is normalized against the
process working directory at resolution time and reported as absolute. This is
deliberate but surprising, because the working directory differs between a shell run,
an IDE test run, and a container entrypoint. The resolution result must always report
the absolute path so diagnostics can show what was actually chosen.

### 4.3 Resolution is inert; existence is a separate policy

Resolving the store path must not:

- create the store file or its directory;
- open SQL connections;
- probe the filesystem beyond the normalization and syntactic validation in section 4.2;
- discover SQL Server instances;
- choose a connection profile.

Store-path resolution and connection-profile resolution are separate concerns, and
neither touches the disk.

Separately from the completed Phase 1 resolver, the plan still needs a **store presence
policy** applied when the
store is first opened, because today a missing file simply reads as an empty store. That
is right for the application default (a first-run developer has no store yet) and wrong
for an explicit override (a CI job pointing at the wrong path would silently see zero
connections and report "not configured" instead of "your path is wrong"). Recommended
policy:

| Source | File missing, read operation | File missing, `connections add` / `add-e2e-bootstrap` |
| --- | --- | --- |
| `ApplicationDefault` | behave as empty store (current behavior) | create |
| `EnvironmentVariable` | **error**, naming the variable and the resolved path | create |
| `Explicit` | **error**, naming the option and the resolved path | create |

This was not implemented in Phase 1. It is a behavior change relative to the current
store and needs its own implementation phase and tests after open question 5 is settled.

### 4.4 Credential portability across store paths

The default password protector is DPAPI-based on Windows and is scoped to the current
user and machine. A store file created on a developer workstation and copied to a build
agent or container **cannot be decrypted there**, and the failure will surface as a
connection error rather than as a configuration error.

Consequences for this plan:

- moving the store path does not move the ability to read protected passwords;
- CI and container stores must use external value references (section 10) or a
  non-persisting protector rather than copied DPAPI blobs;
- documentation must state this explicitly, because "just point the env var at a
  checked-in store" is the obvious wrong thing for a user to try;
- the store-presence and resolution work must not attempt to paper over it by silently
  falling back.

Protector selection remains a host concern and is orthogonal to path selection; the
deferred store factory in section 6.2 must let the host keep supplying its protector.

## 5. TigerCli global option contribution

`ItTiger.TigerQuery.CliCore` defines a TigerCli app contribution for:

```text
--tq-connection-store-file <path>
```

Properties, all of which follow from section 0.3 rather than being TigerQuery choices:

- optional string, one canonical long name, no short name, no alias;
- never prompted, never bound to a settings type, never provider-backed;
- app-wide in meaning, but written **after the command path and required positionals**;
- absent → callback receives `null`;
- repeated → TigerCli argument error (never last-value-wins);
- value omitted → TigerCli argument error;
- documented together with the standard environment variable through
  `AddEnvironmentVariable`, which is help metadata only;
- applied once per run, before command binding.

### 5.1 Contribution shape

The completed Phase 2 shape is:

```csharp
public sealed class TigerQueryCliOptions
{
    /// <summary>Set by the contribution callback; null when the option was absent.</summary>
    public string? ExplicitConnectionStoreFile { get; private set; }

    /// <summary>Host-supplied application default. Required.</summary>
    public required string DefaultConnectionStoreFile { get; init; }

    /// <summary>Host-supplied bootstrap name; null means "no default configured".</summary>
    public string? DefaultE2eBootstrapConnectionName { get; init; }

    /// <summary>Host-supplied protector factory, or null for the Core default.</summary>
    public Func<IConnectionPasswordProtector>? PasswordProtectorFactory { get; init; }

    /// <summary>Injected environment lookup, or null for the process environment.</summary>
    public Func<string, string?>? EnvironmentReader { get; init; }

    /// <summary>
    /// The resolution produced by the callback. Null until the callback has run,
    /// which is also the state seen on help-only runs.
    /// </summary>
    public SqlServerConnectionStorePathResolution? ResolvedStorePath { get; private set; }

    /// <summary>
    /// The single store for this run, constructed lazily from
    /// <see cref="ResolvedStorePath"/> on first access.
    /// </summary>
    public SqlServerConnectionStore Store { get; }
}
```

```csharp
public sealed class TigerQueryCliContribution : ITigerCliAppContribution
{
    public TigerQueryCliContribution(TigerQueryCliOptions options);

    public TigerQueryCliOptions Options { get; }

    public void Configure(TigerCliAppContributionBuilder builder)
    {
        builder.GlobalOptions.AddOptionalString(
            name: "--tq-connection-store-file",
            valueName: "path",
            description: "Use a specific TigerQuery connection-store file.",
            apply: (context, value) => Apply(context, value));

        builder.AddEnvironmentVariable(
            SqlServerConnectionStoreEnvironment.ConnectionStoreFile,
            "Selects the TigerQuery connection-store file when "
            + "--tq-connection-store-file is not supplied.");
    }
}
```

### 5.2 Host registration

Phase 3 follows the guide's pattern exactly — one shared options instance and one
contribution, used everywhere:

```csharp
var connections = new TigerQueryCliOptions
{
    DefaultConnectionStoreFile = TigerSqlCmdApp.DefaultConnectionStoreFile
};
var tigerQuery = new TigerQueryCliContribution(connections);

return TigerCliApp.CreateBuilder()
    .UseAssemblyMetadata(typeof(TigerSqlCmdApp).Assembly)
    .AddContribution(tigerQuery)
    .UseAppResources(SqlServerConnectionCommands.CreateAppResources(MyStrings.ResourceManager))
    .AddCommandGroup("connections", group =>
    {
        SqlServerConnectionCommands.Configure(group, options =>
        {
            options.TigerQuery = connections;   // same instance
            options.ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional;
        });
    })
    .ConfigureProviders(providers =>
        providers.Add("connections", context =>
            connections.Store.GetConnectionNamesAsync(context.CancellationToken)))
    .SetDefaultCommand(() => new TigerSqlCmdQueryCommand(connections))
    .AddCommand("run", () => new TigerSqlCmdCommand(connections), "…")
    .Build();
```

Registering the contribution and mounting the connection command group remain separate
composition steps. `tiger-sqlcmd` performs both and passes the same state to its provider
and command factories.

The contribution must be registered **at most once**. A second registration of the same
option name fails at `Build()`; so would a separate
call to `TigerCliAppBuilder.AddEnvironmentVariable` with the same variable name that the
contribution registers. `tiger-sqlcmd` has no such duplicate registration.

### 5.3 Callback lifecycle and validation timing

The callback should **resolve the store path completely**, not merely record the raw
string. It has everything it needs: the explicit value (or `null`), the host default from
its own options, and Core's environment reader. Resolution is inert (section 4.3), so
doing it in the callback is cheap and makes misconfiguration fail before command binding
with a clean validation error rather than deep inside a handler.

```csharp
private TigerCliValidationResult Apply(TigerCliGlobalOptionContext context, string? value)
{
    try
    {
        Options.ExplicitConnectionStoreFile = value;
        Options.ResolvedStorePath = SqlServerConnectionStorePathResolver.Resolve(
            new SqlServerConnectionStorePathOptions
            {
                ExplicitPath = value,
                DefaultPath = Options.DefaultConnectionStoreFile
            });
        return TigerCliValidationResult.Success();
    }
    catch (SqlServerConnectionStorePathException ex)
    {
        return TigerCliValidationResult.Error(Localize(ex, context.Culture));
    }
}
```

Consequences to design for, all of which need tests:

- **Every command run pays the resolution, including commands that never touch the
  store.** A malformed `TIGERQUERY_CONNECTION_STORE_FILE` therefore fails `tiger-sqlcmd run`
  as well as `connections list`. That is the intended fail-fast behavior; it must be a
  deliberate, documented decision rather than an accident. See open question 6.
- **The store itself is still constructed lazily**, on first access to `Options.Store`.
  Constructing `SqlServerConnectionStore` in the callback would be acceptable today but
  couples every run to store construction cost and to protector initialization.
- **Help runs never invoke the callback**, so `ResolvedStorePath` is `null` during help
  rendering. No help text may depend on it. Help that wants to mention the default store
  path must use `DefaultConnectionStoreFile`, which is known at `Build()` time.
- **The callback mutates per-run state on an object created at `Build()` time.** An app
  instance run more than once in the same process (the test host does this) re-enters the
  callback and must overwrite, not accumulate. The state object is not thread-safe and
  parallel in-process runs against one app instance are unsupported; the existing
  `TigerCliAppCollection` non-parallel test collection already assumes this.
- **Validation messages are localized by CliCore** using `context.Culture` and CliCore's
  own `ResourceManager`. TigerCli does not localize them.

### 5.4 Shared resolved state

CliCore exposes the contribution-owned state object as the single place every in-scope
code path reads the store from, rather than having commands inspect parse results
independently.

Every connection command, every execution command, and every `tiger-sqlcmd` service must
obtain its store from that one object, so a single run can never read one file and write
another.

## 6. Deferred store selection

### 6.1 The problem Phase 2 solved

Before Phase 2, `SqlServerConnectionCommands.Configure(group, options =>
{ options.Store = store; })` took a fully constructed store at `Build()` time and captured it inside the command
factories, the `connections` provider, the `databases` provider, and the `AsEdit` load
callback.

Because the contribution callback runs *after* `Build()`, that eager model could not let
`--tq-connection-store-file` affect which store the commands used.

### 6.2 The change

Phase 2 added deferred store selection through
`SqlServerConnectionCommandOptions.TigerQuery`. The implemented shape:

- add a way to supply contribution-owned state, e.g. `options.TigerQuery = tigerQuery.Options`,
  from which the group reads `Store` lazily;
- make every internal capture site (`SqlServerConnectionCommandContext`, both providers,
  the `AsEdit` loader) go through the deferred accessor rather than a captured instance;
- guarantee the accessor returns **the same instance** for the lifetime of a run, so the
  file lock and in-process mutation gate behave as they do today;
- fail with a clear `InvalidOperationException` if a command reaches the accessor before
  the contribution callback has run — that indicates a host wiring bug, not a user error.

The committed implementation temporarily retains the eager `options.Store` form and
rejects configuring it together with `options.TigerQuery`. The post-Phase 3 review finds
no current in-repository consumer that needs the eager form. It should now be removed as
cleanup, together with its eager-path tests and documentation, leaving
`TigerQueryCliOptions` as the sole connection-store injection model. No compatibility API
needs to be preserved: the current changes are unpublished and there are no other current
consumers in this plan's scope.

### 6.3 Provider and completion timing

`group.AddProvider("connections", …)` and the `databases` provider run during prompting,
which is step 6 of the run pipeline — after the contribution callback. Deferred access is
therefore safe for prompting.

Any code path that enumerates providers *outside* a normal run — shell completion being
the obvious candidate — may not have invoked contribution callbacks. Before relying on
deferred access there, confirm TigerCli's behavior; if callbacks do not run, the provider
must degrade to the application-default store rather than throw. See open question 7.

## 7. Default E2E bootstrap connection

A host application may define a default E2E bootstrap connection name. Bootstrap
profile creation uses a dedicated command:

```text
tiger-sqlcmd connections add-e2e-bootstrap [--name <name>]
```

The bootstrap name is resolved as follows:

- when `--name` is provided, use it;
- otherwise, use the host-configured default E2E bootstrap connection name;
- when neither exists, fail clearly without modifying the connection store.

Examples:

```text
tigerquery-e2e
tiger-sqlcmd-e2e
```

Requirements:

- configured by the host through CliCore;
- default value is unset;
- Core does not invent an application-specific name;
- no implicit selection occurs when the host has not configured one;
- the profile must still carry explicit TigerQuery E2E authorization metadata;
- profile name alone is not authorization;
- the failure case writes nothing — no file creation, no partial profile, no directory
  creation — and returns a validation-error outcome.

Possible CliCore configuration:

```csharp
new TigerQueryCliOptions
{
    DefaultE2eBootstrapConnectionName = "tiger-sqlcmd-e2e"
}
```

This default is host configuration, not a user-facing global
`--default-e2e-connection-name` option. The dedicated command's `--name` option is
the explicit per-invocation override.

Bootstrap identity is separate from general E2E authorization. The regular
`connections add <name>` command may support a flag that marks a newly created
profile as E2E-authorized, but that does not make the profile the bootstrap
profile. The resolver selects a bootstrap profile only by the caller's explicit name or
the host-configured default name. Authorization metadata, a hard-coded naming convention,
and store ordering never select one. Phase 5 settled the regular-add shape as the
non-promptable `--e2e` switch, with a separate non-promptable
`--allow-database-create` switch that requires `--e2e`. The bootstrap command always
supplies E2E authorization and accepts the same separate database-creation permission
switch.

Note that `--name` here is an ordinary command option on the `add-e2e-bootstrap`
settings type, bindable and promptable like any other. It is unrelated to the
contributed global option and must not be confused with it.

## 8. Standard E2E metadata contract

Phase 4 implemented the Core-owned reserved metadata contract:

```text
ittiger.e2e.enabled=true
ittiger.e2e.allow-database-create=true
```

Possible future TigerQuery-owned keys include:

```text
ittiger.e2e.database-prefix=TigerQuery_E2E_
ittiger.e2e.owner=<application-or-suite>
ittiger.e2e.purpose=<purpose>
```

Core exposes constants rather than requiring applications to copy string literals:

```csharp
public static class SqlServerE2eMetadata
{
    public const string ReservedKeyPrefix = "ittiger.";
    public const string Enabled = "ittiger.e2e.enabled";
    public const string AllowDatabaseCreation = "ittiger.e2e.allow-database-create";
    public const string True = "true";
    public const string False = "false";

    public static bool IsReservedKey(string key);
    public static SqlServerE2eFlagState ReadFlag(
        SqlServerConnectionProfile profile,
        string key);
}

public enum SqlServerE2eFlagState
{
    Absent = 0,
    True = 1,
    False = 2,
    Malformed = 3
}
```

### 8.1 Key and value grammar

Profile metadata is an `IReadOnlyDictionary<string, string>` compared with ordinal,
case-sensitive semantics — the same semantics the existing `list --metadata` filters
use. That means the grammar must be pinned down rather than assumed:

- **Keys are matched exactly**, lower-case as written above. `ITTIGER.E2E.ENABLED` is a
  different key and confers nothing.
- **Known reserved Boolean values are exact.** `true` and `false` are ordinal,
  lower-case literals; there is no trimming and no `1`/`yes`/`Y`. Any other value for a
  known reserved E2E Boolean flag is malformed and makes the named profile `Invalid`.
  This applies to every known flag even when the current request does not require the
  permission represented by that flag. For example, malformed
  `ittiger.e2e.allow-database-create` invalidates a read-only resolution too.
- **The entire lowercase `ittiger.*` namespace is reserved for TigerQuery-owned
  metadata.** Applications must keep their metadata under another prefix. Generic
  metadata commands and generic metadata-mutation APIs must reject setting or removing
  every reserved key. Only TigerQuery-owned E2E/bootstrap operations may write those
  keys. This is a binding constraint on Phase 5, not an open design question.
- **Unknown reserved keys are tolerated when reading.** A resolver that encounters an
  `ittiger.*` key it does not understand ignores it, so stores written by newer
  TigerQuery versions remain readable. Read tolerance does not grant generic callers
  permission to write the key.

### 8.2 Authorization semantics

A profile qualifies as an E2E bootstrap profile only when:

- `ittiger.e2e.enabled` is present;
- its value is exactly `true` per section 8.1;
- the profile passes complete structural validation;
- any requested operation is explicitly allowed.

Database-creating workflows additionally require:

```text
ittiger.e2e.allow-database-create=true
```

Ordinary profiles are ignored, even when they are valid and connect successfully.

### 8.3 No connection during resolution

The completed resolver inspects only the selected store data and metadata. Its only I/O
is reading that store; an absent store is read as empty and resolution never writes it.

It must not:

- discover or enumerate SQL Server instances, services, ports, LocalDB installations, or
  containers;
- construct a `SqlConnection`;
- connect to SQL Server;
- probe any server or endpoint;
- validate server reachability;
- test credentials;
- inspect database permissions;
- create a database;
- create or modify a store file, profile, directory, or metadata value.

Connectivity validation should be a separate explicit operation.

## 9. E2E resolver API

Phase 4 added the following reusable Core API with explicit outcomes:

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

    public IReadOnlyList<string> CandidateNames { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];

    public string? RequestedName { get; init; }
}
```

```csharp
public static class SqlServerE2eConnectionResolver
{
    public static SqlServerE2eConnectionResolution Resolve(
        SqlServerConnectionStore store,
        SqlServerE2eConnectionResolutionOptions? options = null);
}
```

```csharp
public sealed class SqlServerE2eConnectionResolutionOptions
{
    public string? ConnectionName { get; init; }

    public string? DefaultConnectionName { get; init; }

    public bool RequireDatabaseCreationPermission { get; init; }

    public SqlServerConnectionValidationPolicy? ValidationPolicy { get; init; }
}
```

`Errors` and `CandidateNames` are diagnostics shown to a developer fixing their setup.
They must never contain a password, a connection string, or a resolved external value;
see section 11.4. Only `Resolved` carries a detached profile; every refusal carries no
profile. `RequestedName` preserves the attempted explicit/default name, and
`CandidateNames` reports the distinct names of E2E-enabled profiles as diagnostics, never
as a list from which the resolver may choose. A null store is a caller error; user and
store configuration failures are returned as status values.

### 9.1 Resolution behavior

The implemented selection contract is strict and name-based:

1. Use `ConnectionName` when the caller supplied it.
2. Otherwise use the host's `DefaultConnectionName`.
3. Otherwise select nothing. Zero or one authorized profile returns `NotConfigured`; one
   authorized profile is never selected implicitly. Several authorized profiles return
   `Ambiguous`, still without selecting one.

Names are matched ordinally and case-sensitively. A present-but-blank explicit or default
name is `Invalid` and never falls through. A missing explicitly named profile is
`Invalid`; a missing host-default profile is `NotConfigured`, because the host convention
has not been configured on that machine. More than one stored profile with the selected
name is `Ambiguous`, including duplicates introduced by a hand edit or direct `Save`; the
resolver never takes the first match.

After finding exactly one named profile, the resolver requires
`ittiger.e2e.enabled=true`, checks any requested database-creation permission, and runs
complete structural validation. The default validation policy is `DatabaseOptional`;
callers may request another policy. Malformed values for either known reserved E2E flag
make the profile `Invalid` regardless of which permission the request needs. Unknown
future `ittiger.*` keys are ignored while reading. An absent store is `NotConfigured`; an
existing unreadable or malformed store is `Invalid`.

## 10. CLI commands for easy developer setup

CLI support should reuse existing connection commands wherever practical. The
regular `connections add <name>` command should be able to create an
E2E-authorized connection without requiring developers to know metadata keys.

The only dedicated command needed in the first phase is:

```text
connections add-e2e-bootstrap [--name <name>]
```

Enable, disable, show, validate, and other lifecycle commands are **deferred** to a
later phase and are explicitly out of scope for the initial implementation. The
implementation should choose the smallest command and option design consistent with
these requirements.

Both the reuse and the new command are mounted through
`SqlServerConnectionCommands.Configure`, not through the TigerCli contribution, which
cannot add commands (section 0.4).

### 10.1 Non-interactive setup

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

The store path is already non-promptable by construction: contributed global options are
never prompted (section 0.3). The remaining options are ordinary command options and must
be marked non-promptable individually.

## 11. External value references

For CI/CD and container use, the writable JSON connection store should be able to reference values supplied externally.

The store itself remains editable so the workflow can:

- use the bootstrap connection;
- create a new test database;
- add a database-specific profile;
- run scripts and tests;
- delete the profile;
- drop the database.

Secrets need not be stored directly in the writable JSON file. This is also the answer
to the DPAPI portability problem in section 4.4.

### 11.1 Supported sources

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

The JSON shape must be **forward- and backward-compatible with existing stores**: a value
that is a plain string keeps meaning a literal, and a store written by an older TigerQuery
must still load. An unknown `Source` value must fail with a clear error rather than being
treated as a literal.

### 11.2 Applicable profile values

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

### 11.3 Full connection string versus fields

The model should support either:

- a full externally supplied connection string; or
- individually configured fields.

These modes should be mutually exclusive or have explicitly defined precedence.

A whole connection string may be useful for simple CI integration.

Individual fields are better when the workflow needs to override `InitialCatalog` for a generated test database.

The recommendation is **mutually exclusive**, rejected at validation time: a profile that
supplies both a full connection string and individual fields is invalid. Precedence rules
between the two are the kind of thing that produces a "why is it connecting to the wrong
database" incident.

### 11.4 Security behavior

External values must:

- resolve only when building the effective connection;
- never be written back into the JSON store;
- be preserved as references when profiles are copied;
- fail clearly when missing;
- be redacted in logs and diagnostics when sensitive;
- never appear in exception messages as raw values;
- never be printed by CLI inspection commands.

`connections show` and `list` must render a reference as its *description* — source kind
plus variable name or file path — and never its resolved value, including when the
reference resolves successfully. A file path may itself be sensitive in some
environments; treat it as displayable but note it in documentation.

## 12. Main use cases

### 12.1 Normal local development

The developer creates one bootstrap connection in the normal application store:

```text
tiger-sqlcmd connections add-e2e-bootstrap
```

or, when the host has configured no default name:

```text
tiger-sqlcmd connections add-e2e-bootstrap --name tiger-sqlcmd-e2e
```

After that:

- Visual Studio tests;
- Rider tests;
- `dotnet test`;
- local scripts;
- app-library workflows

can all resolve the standard E2E profile without environment variables.

No profile means no E2E access.

### 12.2 Alternate local store

A developer can isolate work with:

```text
tiger-sqlcmd connections list --tq-connection-store-file C:\temp\e2e.json
```

Note the placement: the option follows the command path. `tiger-sqlcmd --tq-connection-store-file C:\temp\e2e.json connections list`
is invalid (section 0.3, rule 6). For the default command with a positional query, the
option follows the positional:

```text
tiger-sqlcmd "select 1" --tq-connection-store-file C:\temp\e2e.json
```

Useful for:

- experiments;
- isolated branches;
- reproductions;
- separate test environments;
- temporary app configurations.

The explicit path overrides the environment variable and default store.

### 12.3 CI/CD

A pipeline creates or mounts a writable runtime store and sets the TigerQuery store-path environment variable.

Example:

```text
TIGERQUERY_CONNECTION_STORE_FILE=/workspace/runtime/connections.json
```

The store may contain external references to:

- secret environment variables;
- mounted secret files;
- mounted configuration files.

No TigerCli dependency is required for the library-only CI workflow.

### 12.4 Containers

A container may receive:

- writable runtime JSON store;
- read-only mounted secret files;
- mounted configuration files;
- environment variables for non-file configuration.

TigerQuery resolves the store path through the standard environment variable.

The store references external values and remains writable for temporary connection creation.

### 12.5 Library mode

TigerQuery's library and test workflows use:

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

### 12.6 Tool mode

A developer uses `tiger-sqlcmd` to:

1. configure or validate the bootstrap profile;
2. create the test database;
3. copy/add the database-specific profile;
4. run schema and data scripts;
5. run test queries;
6. capture CSV and error output;
7. remove the temporary profile;
8. drop the test database.

`tiger-sqlcmd` remains a CLI over reusable TigerQuery APIs.

### 12.7 Mixed mode

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

Mixed mode is also where store-path agreement matters most: the library side must
resolve the same path the tool side used. In practice this means the environment
variable, not the CLI option, is the mixed-mode coordination mechanism, because the
library side has no command line. Documentation should say so directly.

## 13. Safe database lifecycle

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

The final ownership mechanism needs separate design and is the gate on this phase
starting; see open question 9.

## 14. Default test behavior

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
- use ordinary connection profiles as E2E profiles;
- read or write the developer's real user-profile connection store.

When E2E configuration is absent:

- E2E tests report `NotConfigured`;
- framework-specific adapters may skip them;
- no network connection is attempted.

This behavior should be protected by tests proving zero connection attempts.

## 15. Testing requirements

### 15.1 Core path resolution

Test:

- explicit path wins;
- environment variable wins over default;
- default used when no override exists;
- invalid explicit path does not fall through;
- present-but-empty environment variable is an error, not an absence;
- invalid environment variable does not fall through;
- selected path is normalized to absolute, including relative input;
- source is reported correctly for all three sources;
- no file, directory, or SQL access occurs during resolution;
- environment reading goes through the injected reader, so no test mutates process state.

### 15.2 Store presence policy

Test, per section 4.3:

- missing default store reads as empty;
- missing explicit store errors on read and names the option;
- missing environment-variable store errors on read and names the variable;
- `add` and `add-e2e-bootstrap` create the file for every source;
- a failed `add-e2e-bootstrap` (no name available) creates nothing.

### 15.3 E2E metadata

Phase 4 validation completed:

- ordinary profile ignored;
- enabled profile accepted;
- non-canonical Boolean metadata (`True`, `1`, `yes`, ` true `) rejected with a specific error rather than silently false;
- malformed `allow-database-create` rejected even when database creation is not requested;
- wrong-case metadata key confers nothing;
- unknown future `ittiger.*` metadata tolerated when reading;
- database creation requires explicit permission;
- explicit name wins over the host default;
- explicit name must still be authorized;
- no name never selects a sole authorized profile;
- duplicate selected names and multiple unnamed candidates return `Ambiguous` and never
  select the first profile;
- no name with zero or one authorized profile returns `NotConfigured`;
- an explicitly named absent profile returns `Invalid`;
- a configured-but-absent default name returns `NotConfigured`, not `Invalid`;
- present-but-blank explicit/default names return `Invalid` without fallback;
- structural validation and optional database requirements are applied;
- an absent store returns `NotConfigured` without creating it, while an unreadable store
  returns `Invalid`;
- resolution performs no SQL Server discovery or probing, constructs no `SqlConnection`,
  contacts no server, and writes nothing;
- diagnostics contain no secrets.

### 15.4 CliCore contribution

Test:

- contribution registration succeeds and the option appears in help;
- global option parses and reaches contribution state;
- callback runs with `null` when the option is absent;
- callback runs before command binding;
- validation error from the callback stops the run and maps to the usage/validation exit category;
- repeated occurrence is an argument error, not last-value-wins;
- missing value is an argument error;
- option after the command path and positionals is valid; before the command path is invalid;
- `--name=value` form works for values beginning with `-`;
- option is never prompted, has no short alias, and is absent from settings types;
- environment-variable help appears in `--help-env`;
- `--help` renders without invoking the callback;
- host default path is used when neither override is present;
- duplicate registration of the option or env-var name fails at `Build()`;
- the same state object serves connection commands, providers, and host commands;
- running one built app twice re-applies rather than accumulates state.

### 15.5 Deferred store selection

Test:

- commands, both providers, and the `AsEdit` loader all observe the run-selected store;
- the accessor returns the same instance throughout a run;
- reaching the accessor before the callback throws a wiring error, not a silent default;
- after cleanup, `SqlServerConnectionCommandOptions` exposes no eager `Store` injection
  path and requires the shared `TigerQueryCliOptions` model;
- the host-supplied password protector still reaches the constructed store.

### 15.6 Host integration

Test in `tiger-sqlcmd`, the repository host covered by this plan:

- host opts into contribution;
- no duplicate option implementation;
- same store used by connection and run commands;
- existing default behavior unchanged when no override is supplied;
- explicit alternate store works;
- environment-variable store works;
- CLI override wins over environment variable;
- the existing CLI test host injects its temporary file as the application default;
- CLI and environment overrides both outrank that injected application default.

### 15.7 Safety regressions

Add explicit tests proving:

- no SQL Server discovery code;
- no fallback connection string;
- no automatic localhost/LocalDB use;
- no profile selection without E2E metadata;
- no network access when E2E is not configured;
- secrets are redacted;
- external values are not persisted into the store;
- the unconfigured test run never touches the real user-profile store path.

## 16. Documentation requirements

Documentation should include:

- developer one-time setup;
- CI/CD setup;
- container setup;
- CLI/API precedence;
- standard environment variable;
- **global-option placement rules and the `--name=value` form**;
- standard E2E metadata, including exact key case and value grammar;
- **DPAPI portability warning for copied stores** (section 4.4);
- explicit warning that access is not authorization;
- no-discovery policy;
- examples for literal, environment, and file-based values;
- mixed-mode examples, including that the environment variable is the coordination
  mechanism;
- cleanup safety requirements;
- AI-agent guidance.

The CliCore README's "One selected store" section was rewritten in Phase 2 for the shared
`TigerQueryCliOptions` model. The post-Phase 3 cleanup must remove its remaining fixed
`SqlServerConnectionCommandOptions.Store` documentation.

Recommended explicit instruction:

> Never discover or probe for SQL Server instances. Resolve E2E access only through TigerQuery's connection-store and E2E authorization APIs. When the resolver reports `NotConfigured`, stop or skip without opening a connection.

## 17. Phased implementation

Phases are ordered by dependency. Each carries a **difficulty rating** naming the
strength of AI coding agent that should take it:

- `Low` — localized, well-defined work with little architectural ambiguity;
- `Medium` — cross-project changes, public API design, or moderate integration risk;
- `High` — security-sensitive, lifecycle-sensitive, highly architectural, or requiring
  careful reasoning across several packages.

### Phase 1 — Core store-path resolution — **Completed**

**Difficulty: Medium.** New public Core API with strict precedence and failure semantics.
The logic is small and self-contained, but it is a contract that later phases depend on,
and the "never fall through" rule is easy to get subtly wrong.

**Scope.** Path resolution only. No CLI, no store construction, no E2E concepts.

**Completed work.**

1. Environment-variable name constant (`SqlServerConnectionStoreEnvironment`).
2. `SqlServerConnectionStorePathOptions`, `…Resolution`, `…Source`, and the resolver.
3. Injected environment reader with the process default.
4. Normalization to absolute, and the section 4.2 syntactic validation set.
5. A result type carrying success or failure, selected source, rejected value, and error
   code/message in a form CliCore can localize.
6. XML docs, Core README documentation, and exhaustive resolver tests.

**Depends on.** Nothing.

**Validation completed.** Section 15.1, including inert resolution under a directory that
does not exist. The store-presence policy in section 4.3 was not part of the committed
implementation and remains a separate open design item.

**Settled outcomes.** The variable is `TIGERQUERY_CONNECTION_STORE_FILE`; the public API
names in section 4 are final; resolution returns `SqlServerConnectionStorePathResolution`
rather than throwing for a user-supplied unusable path. The store-presence policy remains
open question 5.

### Phase 2 — CliCore deferred store selection and the TigerCli contribution — **Completed**

**Difficulty: High.** A public API change combined with
lifecycle-sensitive callback timing across two packages, plus every internal capture site
in the command group. Getting this wrong produces a run that reads one store and writes
another, which is exactly the failure mode this plan exists to prevent.

**Scope.** All of CliCore. Two coupled workstreams that ship together because neither is
useful alone: deferred store selection (section 6) and the `ITigerCliAppContribution`
implementation (section 5).

**Completed work.**

1. `TigerQueryCliOptions` contribution state: host default path, optional bootstrap name,
   optional protector factory, callback-set explicit value and resolution, lazily
   constructed single `Store`.
2. `TigerQueryCliContribution : ITigerCliAppContribution` registering
   `--tq-connection-store-file` via `GlobalOptions.AddOptionalString` and the environment
   variable via `AddEnvironmentVariable`.
3. Callback that delegates to the Phase 1 resolver and returns a localized
   `TigerCliValidationResult.Error` on failure, using `context.Culture`.
4. Extended `SqlServerConnectionCommandOptions` with the deferred `TigerQuery` form; the
   committed implementation rejects configuring it together with the eager `Store` form.
5. Convert `SqlServerConnectionCommandContext`, the `connections` provider, the
   `databases` provider, and the `AsEdit` loader to deferred access.
6. Guard against access before the callback with a clear wiring error.
7. CliCore resource entries for the callback's error messages (en-US, pl-PL).
8. Update the CliCore README, including the "One selected store" section.

**Depends on.** Phase 1.

**Validation completed.** Sections 15.4 and 15.5, including a test that builds an app, runs it twice with
different `--tq-connection-store-file` values, and asserts each run wrote only its own
file.

**Settled and remaining outcomes.** The callback resolves on every command run and the
store remains lazy. The post-Phase 3 review settles `options.Store` for removal as cleanup
(open question 12). Completion-path behavior remains open question 7. The inability to
localize contribution descriptions remains an unresolved TigerCli gap (open question 14).

### Phase 3 — `tiger-sqlcmd` registration and migration — **Completed**

**Difficulty: Medium.** Mechanical in shape but touches the composition root, the static
store ambient, and the existing CLI test harness. Regression risk is concentrated in "the
default path must behave exactly as it does today."

**Scope.** `tiger-sqlcmd` only. This phase proves the deferred connection-store plumbing
and precedence through the repository's real host. It does not prove the E2E metadata,
authorization, database-lifecycle, or external-process workflow planned for Phases 4–8.

**Final wiring.**

1. `TigerSqlCmdApp.Build` creates one shared `TigerQueryCliOptions` and one
   `TigerQueryCliContribution`, then registers that contribution once.
2. The same options instance is passed to `SqlServerConnectionCommands.Configure`, the
   app-level `connections` provider, and both command factories.
3. `TigerSqlCmdCommand` and `TigerSqlCmdQueryCommand` receive the options through
   constructor injection and resolve saved connections from the run-selected store.
4. The static ambient `TigerSqlCmdApp.ConnectionStore` was removed.
   `TryResolveConnection` now receives the shared options explicitly.
5. `CliTestRunner` injects a temporary file as the application-default path, not as a
   higher-priority fixed store.
6. The contribution owns the option, environment help metadata, precedence, and path
   validation; `tiger-sqlcmd` has no duplicate store-path mechanism.

**Depends on.** Phase 2.

**Validation completed.** Host tests prove CLI > environment > application default,
including that the injected test path is only the application default and loses to both
overrides. They also prove that the connection commands, provider, default query command,
and `run` command observe the run-selected store. The existing default-path behavior is
unchanged.

**Settled outcome.** Open question 13 is settled: test injection acts as the
application-default path and does not outrank CLI or environment overrides.

### Phase 4 — E2E metadata contract and resolver in Core — **Completed**

**Difficulty: High.** This is the authorization boundary. Every default must be
fail-closed, the value grammar must be strict, and diagnostics must not leak. A weak
implementation here silently reintroduces "reachable means allowed."

**Scope.** Core only. No CLI surface.

**Completed work.**

1. `SqlServerE2eMetadata`, `SqlServerE2eFlagState`, the two initial E2E flag constants,
   strict `true`/`false` parsing, and ordinal `ittiger.*` namespace classification.
2. `SqlServerE2eResolutionStatus`, `SqlServerE2eConnectionResolution`,
   `SqlServerE2eConnectionResolutionOptions`, and the static resolver API shown in
   section 9.
3. Strict name-based selection: explicit name, then host default, with no implicit sole
   profile selection; duplicate names and multiple unnamed candidates are `Ambiguous`.
4. Distinct missing-name outcomes: an explicit missing name is `Invalid`; a missing host
   default is `NotConfigured`; blank supplied names are `Invalid` and do not fall through.
5. Exact authorization and permission checks, including rejection of every malformed
   known E2E flag even when the current request does not require that flag.
6. Forward-compatible reading that tolerates unknown future `ittiger.*` keys while
   retaining the entire namespace for TigerQuery ownership.
7. Complete structural validation with `DatabaseOptional` as the default policy and an
   option for callers to request a different validation policy.
8. Redacted failure diagnostics, candidate-name diagnostics, and refusal results that
   never carry a profile.
9. An inert resolver that loads only the selected store and performs no SQL Server
   discovery, probing, `SqlConnection` construction, connection attempt, or write.

**Depends on.** Phase 1 (store access). Phases 1–3 are already complete.

**Validation completed.** Section 15.3, including positive-control tests that resolution
constructs no `SqlConnection`, never contacts a reachable endpoint named by the profile,
and leaves an absent store absent.

**Settled outcomes.** Open questions 3 and 15 are closed: resolution is always name-based,
never selects a sole authorized profile implicitly, and does not use a bootstrap-identity
metadata key. Open question 8 is closed: all `ittiger.*` keys are TigerQuery-owned,
generic metadata write paths must reject them, and only TigerQuery-owned E2E/bootstrap
operations may write them. Unknown reserved keys remain tolerated on reads for forward
compatibility. Framework-specific mapping of `NotConfigured` to skip remains open
question 11.

### Phase 5 — Bootstrap CLI surface — **Completed**

**Difficulty: Medium.** Small command surface, but it is the place where bootstrap
identity and general E2E authorization must stay distinct, and where the
"fail without modifying the store" rule is enforced.

**Scope.** `connections add-e2e-bootstrap [--name <name>]` and the E2E-authorization flag
on the regular `connections add`. Nothing else. Enable, disable, show, and validate stay
deferred.

**Tasks.**

1. `add-e2e-bootstrap` command mounted through `SqlServerConnectionCommands.Configure`.
2. Name resolution: `--name` wins, then the host-configured default, then a clean failure
   that writes nothing.
3. E2E-authorization flag on `connections add`, writing the metadata without conferring
   bootstrap identity.
4. Enforce the binding reserved-write policy from section 8.1: generic metadata commands
   and APIs reject every `ittiger.*` set/remove request, while these TigerQuery-owned
   E2E/bootstrap paths may write the reserved keys they own. Read and filter paths remain
   forward-compatible with unknown reserved keys.
5. Non-promptable options for every value automation needs (section 10.1).
6. Resources for both commands in en-US and pl-PL.
7. Host exit-kind mapping review.

**Depends on.** Phases 2, 3, and 4.

**Validation.** Command-level tests including the no-name failure writing nothing, a test
proving an `--e2e`-authorized profile is not selected as the bootstrap, generic metadata
set/remove rejection for known and unknown `ittiger.*` keys, and successful writes through
the TigerQuery-owned E2E/bootstrap path.

**Settled outcome.** Regular `connections add` uses non-promptable `--e2e` and
`--allow-database-create` switches; the latter requires the former. The dedicated
bootstrap command always writes E2E authorization and shares the database-creation
permission switch. Neither path writes a bootstrap-identity key, and strict name-based
selection remains unchanged.

### Phase 6 — External value references

**Difficulty: High.** A persisted-format change plus secret handling. Compatibility,
redaction, and copy semantics all have to be right simultaneously, and mistakes are
either data-format breaks or credential leaks.

**Scope.** Core value model plus the CliCore options needed to configure it
non-interactively.

**Tasks.**

1. External-value model and JSON contract with literal-string backward compatibility.
2. Environment, whole-file, and keyed-file sources.
3. Resolution at effective-connection build time only; never written back.
4. Copy semantics preserving references.
5. Sensitivity classification and redaction across logs, exceptions, `show`, and `list`.
6. Mutually exclusive full-connection-string versus field mode, rejected at validation.
7. Non-interactive CliCore options and safe display.

**Depends on.** Phases 1–3 (a selected store) and Phase 4 (profiles worth protecting).

**Validation.** Section 15.7 redaction and non-persistence tests, plus round-trip tests
against stores written by the previous format version.

**Risks / open decisions.** JSON contract and compatibility (open question 6 in the
original numbering, now 16); keyed-file formats (open question 17); whether file-path
values are themselves sensitive.

### Phase 7 — Safe database lifecycle

**Difficulty: High.** Destructive operations against real servers, gated by an ownership
model that does not exist yet.

**Scope.** Reusable APIs for creating and dropping test databases safely. **Do not start
this phase until the ownership mechanism is designed and written down** — the current
plan explicitly defers that decision.

**Tasks.**

1. Ownership-marker design (naming, run identifier, in-database evidence) as a written
   decision.
2. Safe generated names and pattern enforcement.
3. Creation gated on `allow-database-create`.
4. Profile copy/add for the generated database.
5. Script deployment helpers.
6. Cleanup authorization requiring positive ownership evidence, never age or
   reachability.
7. Abandoned-run cleanup as a separate, explicitly invoked operation.

**Depends on.** Phase 4, and on the ownership design.

**Validation.** Live tests behind the E2E gate, plus offline tests proving that
non-matching names and unauthorized profiles are rejected before any command is sent.

**Risks / open decisions.** Ownership marker (open question 9); whether these helpers live
in Core or `ItTiger.TigerQuery` (open question 10); drop-safety under partial failure.

### Phase 8 — Repository E2E migration and hardening

**Difficulty: High.** The phase where the safety claims are actually proven, through one
host and an external-process test surface.

**Scope.** TigerQuery's own test suite and `tiger-sqlcmd` external-process coverage.

**Tasks.**

1. Remove SQL Server discovery from TigerQuery tests.
2. Move them onto the shared resolver and the `NotConfigured` skip path.
3. Prove default `dotnet test` is inert, including that it never touches the real
   user-profile store path.
4. Add real `tiger-sqlcmd` external-process E2E tests.
5. Document local and CI workflows end to end.

**Depends on.** Phases 4 and 7 for the interesting cases; the inertness work can start
after Phase 4.

**Validation.** Section 15.7 in full, run on a machine with SQL Server installed and
reachable — the proof that matters is that a reachable server changes nothing. Completing
this phase is what will make the complete E2E workflow proven through `tiger-sqlcmd`.
Phase 3 proved only connection-store composition and precedence.

**Risks / open decisions.** Skip-mechanism coupling to xUnit (open question 11); how much
of the existing live-test surface must be rewritten rather than adapted.

### Explicitly deferred

Not in scope for any numbered phase above, and not to be added opportunistically:

- `connections e2e enable | disable | show | validate` and any other E2E lifecycle
  command family;
- a user-facing global `--default-e2e-connection-name`;
- any TigerCli change made on TigerQuery's behalf;
- store formats other than the existing JSON file;
- credential providers beyond the existing protector abstraction and the external-value
  references in Phase 6.

## 18. Repository cleanup and rollout

**Remove `SqlServerConnectionCommandOptions.Store`.** The committed Phase 2 implementation
added `TigerQueryCliOptions` alongside the eager `Store` property and made the two mutually
exclusive. After Phase 3, every in-scope production composition path uses
`TigerQueryCliOptions`; only tests exercise the eager branch. There are no other current
consumers for which this repository must preserve source compatibility, and these changes
have not yet been published.

The conclusion is therefore to remove `SqlServerConnectionCommandOptions.Store` now as
post-Phase 3 cleanup, remove the eager accessor branch and its tests/documentation, and
leave `TigerQueryCliOptions` as the sole connection-store injection model. This is not a
compatibility path and must not be carried through Phases 4–8.

**Host store injection in tests.** This is settled. `CliTestRunner` passes a temporary
file path to `TigerSqlCmdApp.Build(defaultConnectionStoreFile, environmentReader)`. That
path replaces only the application default. The contribution still applies the production
precedence: CLI > environment > application default. No pinned test mode that outranks
the CLI or environment is needed.

**Rollout order.** Phases 1–4 are complete and deliver
`--tq-connection-store-file`, `TIGERQUERY_CONNECTION_STORE_FILE`, and the shared deferred
store plumbing, plus the Core E2E metadata/authorization contract and inert bootstrap
resolver. The host tests prove store composition and precedence through `tiger-sqlcmd`;
they do not prove the complete E2E workflow. Phase 5 is next and adds the bootstrap command
surface under the binding reserved-write policy; Phase 8 supplies the end-to-end proof
after the remaining safety contracts exist.

## 19. Open questions

1. ~~Exact environment-variable name for the store path.~~ **Settled in Phase 1:**
   `TIGERQUERY_CONNECTION_STORE_FILE`.
2. ~~Exact public API names for path resolution.~~ **Settled in Phase 1:** the names shown
   in section 4 are implemented.
3. ~~Selection behavior when no E2E bootstrap name is supplied.~~ **Settled in Phase 4:**
   selection requires the caller's explicit name or the host-configured default name; a
   sole authorized profile is not selected implicitly.
4. ~~Whether the resolution failure surface is an exception or a result type.~~ **Settled
   in Phases 1–2:** Core returns `SqlServerConnectionStorePathResolution`; CliCore maps a
   failed result to a localized `TigerCliValidationResult.Error`.
5. Whether the store-presence policy in section 4.3 is acceptable, given that it changes
   existing behavior for explicitly-pathed stores that do not yet exist.
6. ~~Whether the contribution callback should resolve eagerly on every run.~~ **Settled
   in Phase 2:** path resolution runs in the callback on every command run; store
   construction remains lazy.
7. Whether TigerCli invokes contribution callbacks on shell-completion paths, and what a
   provider should do if it does not.
8. ~~Whether generic metadata paths reject writes to reserved `ittiger.*` keys.~~
   **Settled with Phase 4:** the entire namespace is TigerQuery-owned. Generic metadata
   commands and APIs must reject reserved-key writes; only TigerQuery-owned E2E/bootstrap
   operations may perform them. Unknown reserved keys remain tolerated when reading.
9. Database ownership marker used for safe cleanup. Blocks Phase 7.
10. Whether database lifecycle helpers belong entirely in Core or partly in
    `ItTiger.TigerQuery`.
11. How test frameworks should map `NotConfigured` to skip behavior without coupling Core
    to xUnit, NUnit, or MSTest.
12. ~~Whether `SqlServerConnectionCommandOptions.Store` is retained alongside the deferred
    form.~~ **Settled after Phase 3:** remove it as repository cleanup and leave
    `TigerQueryCliOptions` as the sole injection model. No compatibility API is required.
13. ~~How the existing CLI test harness injects a store and how that injection ranks.~~
    **Settled in Phase 3:** injection supplies the application-default path; CLI and
    environment overrides retain higher precedence.
14. How contributed global-option and environment-variable *descriptions* get localized,
    given that 0.9.1 accepts only literal strings at `Build()` time while the rest of the
    host's help is resource-driven. Options: accept English-only help for this one
    option; have the host pass a pre-localized description built from its default culture;
    or request `descriptionResourceKey` overloads in a future TigerCli. This is a TigerCli
    feature gap, not a TigerQuery design choice, and must not be worked around by making
    TigerCli TigerQuery-aware.
15. ~~Whether bootstrap identity is recorded as its own metadata key.~~ **Settled in
    Phase 4:** it is not. The resolver selects only the caller's explicit name or the
    host-configured default name; authorization metadata expresses permission, not
    bootstrap identity.
16. External-value JSON contract and compatibility strategy.
17. Supported keyed-file formats in the first version.
18. Full connection-string versus field-level precedence, if the mutual-exclusion
    recommendation in section 11.3 is rejected.

## 20. Acceptance criteria

The design is successful when:

- TigerCli remains domain-neutral, with no TigerQuery names, concepts, or environment
  variables anywhere in it;
- CliCore contributes the TigerQuery global option through `ITigerCliAppContribution` and
  no other mechanism;
- the contributed option obeys every constraint in section 0.3, including placement,
  repetition, and non-promptability;
- Core owns environment reading, precedence, and explicit/default resolution;
- CLI overrides environment variables;
- environment variables override the application default;
- an invalid higher-priority source never falls through to a lower one;
- one run reads and writes exactly one store file;
- `TigerQueryCliOptions` is the sole connection-store injection model after the
  post-Phase 3 cleanup;
- local developers need no environment variables;
- CI/CD can use environment variables and mounted files;
- E2E profiles require explicit TigerQuery metadata, matched exactly;
- bootstrap selection uses only an explicit caller name or host default name and never
  infers identity from sole authorization or store ordering;
- malformed known reserved E2E flags invalidate the profile even when the current request
  does not require the affected permission;
- the entire `ittiger.*` namespace is TigerQuery-owned; generic metadata writes reject it,
  TigerQuery-owned E2E/bootstrap operations are its only writers, and unknown reserved
  keys are tolerated when reading;
- no component discovers SQL Server;
- no unconfigured test run opens a SQL connection or touches the developer's real store;
- library, tool, and mixed modes use identical contracts;
- `tiger-sqlcmd` reuses the shared implementation rather than its own;
- Phase 8 proves the complete E2E workflow end to end through `tiger-sqlcmd`.
