# TigerQuery connection-store resolution and safe E2E foundation

Status: proposed design

Scope:

- `ItTiger.TigerQuery.Core`
- `ItTiger.TigerQuery.CliCore`
- TigerCli-based host applications such as `tiger-sqlcmd` and TigerWrap
- third-party .NET applications consuming TigerQuery libraries directly
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
- **The host application owns** the decision to register the contribution, the
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
  runs during `Build()`.** Today `SqlServerConnectionCommandOptions.Store` takes an
  already-constructed `SqlServerConnectionStore`, captured by the command factories at
  build time. A store path chosen by `--tq-connection-store-file` is not known then.
  Store selection must become **deferred** before the contribution can mean anything.
  This is the single largest piece of work in the plan and it is a breaking change to a
  published CliCore API (see section 6.2 and phase 2).
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

TigerCli provides only the generic app-contribution mechanism described in section 0.

`ItTiger.TigerQuery.CliCore` owns TigerQuery-specific global options such as:

```text
--tq-connection-store-file <path>
```

Host applications opt into the contribution. There is exactly one global-option
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

Core must work equally for:

- command-line applications;
- test libraries;
- desktop applications;
- services;
- build agents;
- containers;
- custom automation.

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

### 3.4 Host applications

Examples:

- `tiger-sqlcmd`
- TigerWrap
- third-party TigerCli-based applications

These are the responsibilities of a host that has adopted the contribution. Adoption is
per-host and staged: this plan migrates `tiger-sqlcmd` only (Phase 3). TigerWrap continues
to pass a fixed `SqlServerConnectionCommandOptions.Store` until its own deferred migration,
and a third-party host may do the same indefinitely.

Host responsibilities:

- construct the CliCore contribution **exactly once**;
- register it through `TigerCliAppBuilder.AddContribution(...)`;
- provide the application's default user-profile store path to the contribution;
- optionally define a default E2E bootstrap connection name;
- pass **the same contribution-owned state object** to `SqlServerConnectionCommands.Configure(...)`,
  to its own command factories, and to any service that reads or writes connections;
- decide user-facing command grouping and branding;
- avoid independent store-path or E2E resolution logic.

A host that constructs two contribution instances, or that passes one instance to
`AddContribution` and a different one to its command factories, will silently run with an
unresolved store path. The guide's pattern — one local variable used in both places — is
the required shape.

## 4. Connection-store path resolution

Core should expose one reusable resolver.

Conceptually (names provisional):

```csharp
public sealed class SqlServerConnectionStorePathOptions
{
    /// <summary>The caller-supplied override; from the CLI option in CLI hosts.</summary>
    public string? ExplicitPath { get; init; }

    /// <summary>The host application's default store path. Required.</summary>
    public required string DefaultPath { get; init; }

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
    /// <summary>The normalized absolute path.</summary>
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

Exact names may differ, but the contract should be clear. The injected
`EnvironmentReader` is not cosmetic: without it, every precedence test must mutate
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
- the value names a directory rather than a file, or ends in a directory separator.

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

Separately from resolution, the plan needs a **store presence policy** applied when the
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

This is a behavior change relative to the current store and needs its own tests. See
open question 5.

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

Conceptually (names provisional):

```csharp
public sealed class TigerQueryCliOptions
{
    /// <summary>Set by the contribution callback; null when the option was absent.</summary>
    public string? ExplicitConnectionStoreFile { get; internal set; }

    /// <summary>Host-supplied application default. Required.</summary>
    public required string DefaultConnectionStoreFile { get; init; }

    /// <summary>Host-supplied bootstrap name; null means "no default configured".</summary>
    public string? DefaultE2eBootstrapConnectionName { get; init; }

    /// <summary>Host-supplied protector factory, or null for the Core default.</summary>
    public Func<IConnectionPasswordProtector>? PasswordProtectorFactory { get; init; }

    /// <summary>
    /// The resolution produced by the callback. Null until the callback has run,
    /// which is also the state seen on help-only runs.
    /// </summary>
    public SqlServerConnectionStorePathResolution? ResolvedStorePath { get; internal set; }

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

Following the guide's pattern exactly — one instance, used in both places:

```csharp
var tigerQuery = new TigerQueryCliContribution(new TigerQueryCliOptions
{
    DefaultConnectionStoreFile = appDefaultStorePath,
    DefaultE2eBootstrapConnectionName = "tigerwrap-e2e"
});

return TigerCliApp.CreateBuilder()
    .UseAssemblyMetadata(typeof(MyApp).Assembly)
    .AddContribution(tigerQuery)
    .UseAppResources(SqlServerConnectionCommands.CreateAppResources(MyStrings.ResourceManager))
    .AddCommandGroup("connections", group =>
    {
        SqlServerConnectionCommands.Configure(group, options =>
        {
            options.TigerQuery = tigerQuery.Options;   // same instance
            options.ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional;
        });
    })
    .AddCommand("run", () => new RunCommand(tigerQuery.Options), "…")
    .Build();
```

Registering the contribution is one opt-in; mounting the connection command group is a
second, independent one. A host may do either alone: a host with no `connections` group
can still accept `--tq-connection-store-file` for its own commands, and a host that
mounts the group without the contribution simply has no CLI override.

The contribution must be registered **at most once**. A second registration of the same
option name fails at `Build()`; so does a host that separately calls
`TigerCliAppBuilder.AddEnvironmentVariable` with the same variable name that the
contribution registers. Hosts migrating to the contribution must delete any such
existing registration.

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

CliCore exposes the contribution-owned state object as the single place every consumer
reads the store from, rather than having commands inspect parse results independently.

Every connection command, every execution command, and every host service must obtain
its store from that one object, so a single run can never read one file and write
another.

## 6. Deferred store selection

### 6.1 The problem

`SqlServerConnectionCommands.Configure(group, options => { options.Store = store; })`
takes a fully constructed store at `Build()` time and captures it inside the command
factories, the `connections` provider, the `databases` provider, and the `AsEdit` load
callback. The CliCore README states this explicitly: "`options.Store` is the injection
point for store selection, and it is deliberately the only one."

The contribution callback runs *after* `Build()`. Under the current API there is no way
for `--tq-connection-store-file` to affect which store the commands use.

### 6.2 The change

`SqlServerConnectionCommandOptions` must accept a **deferred** store selection instead of
(or in addition to) an eager instance. The minimal shape:

- add a way to supply contribution-owned state, e.g. `options.TigerQuery = tigerQuery.Options`,
  from which the group reads `Store` lazily;
- keep `options.Store` for hosts that genuinely have one fixed store and no contribution;
- make every internal capture site (`SqlServerConnectionCommandContext`, both providers,
  the `AsEdit` loader) go through the deferred accessor rather than a captured instance;
- guarantee the accessor returns **the same instance** for the lifetime of a run, so the
  file lock and in-process mutation gate behave as they do today;
- fail with a clear `InvalidOperationException` if a command reaches the accessor before
  the contribution callback has run — that indicates a host wiring bug, not a user error.

Exactly one of the eager and deferred forms may be configured; supplying both is a host
configuration error rejected during `Configure`.

This changes a published CliCore public API. `TigerQueryCliOptions` is the target
architecture; `options.Store` stays as a compatibility path so an unmigrated host keeps
building, and it is not removed at the end of Phase 3. Section 18 states the conditions
under which it can eventually go.

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
- profile name alone is not authorization;
- the failure case writes nothing — no file creation, no partial profile, no directory
  creation — and returns a validation-error outcome.

Possible CliCore configuration:

```csharp
new TigerQueryCliOptions
{
    DefaultE2eBootstrapConnectionName = "tigerwrap-e2e"
}
```

This default is host configuration, not a user-facing global
`--default-e2e-connection-name` option. The dedicated command's `--name` option is
the explicit per-invocation override.

Bootstrap identity is separate from general E2E authorization. The regular
`connections add <name>` command may support a flag that marks a newly created
profile as E2E-authorized, but that does not make the profile the bootstrap
profile. Bootstrap identity must be explicit and must not be inferred from profile
name, authorization metadata, or store ordering. The exact flag and metadata shape
can be chosen during implementation.

Note that `--name` here is an ordinary command option on the `add-e2e-bootstrap`
settings type, bindable and promptable like any other. It is unrelated to the
contributed global option and must not be confused with it.

## 8. Standard E2E metadata contract

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

### 8.1 Key and value grammar

Profile metadata is an `IReadOnlyDictionary<string, string>` compared with ordinal,
case-sensitive semantics — the same semantics the existing `list --metadata` filters
use. That means the grammar must be pinned down rather than assumed:

- **Keys are matched exactly**, lower-case as written above. `ITTIGER.E2E.ENABLED` is a
  different key and confers nothing.
- **Values are matched by a single documented rule.** The recommendation is strict:
  `true` and `false`, ordinal, lower-case, no trimming, no `1`/`yes`/`Y`. Any other value
  for a reserved key is an *authorization failure with a specific error*, never a silent
  `false`, so a typo like `True` fails loudly instead of quietly disabling E2E.
- The `ittiger.` prefix should be documented as **reserved for TigerQuery**; applications
  must not define their own keys under it. Whether Core actively rejects writes to
  reserved keys from `connections add --metadata` is open question 8.

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

E2E profile resolution must inspect only store data and metadata.

It must not:

- connect to SQL Server;
- validate server reachability;
- test credentials;
- inspect database permissions;
- create a database.

Connectivity validation should be a separate explicit operation.

## 9. E2E resolver API

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

`Errors` and `CandidateNames` are diagnostics shown to a developer fixing their setup.
They must never contain a password, a connection string, or a resolved external value;
see section 11.4.

### 9.1 Resolution behavior

- explicit connection name:
  - profile must exist;
  - profile must be E2E-enabled;
  - requested permissions must be present;
  - otherwise return `Invalid`;
- configured default connection name:
  - same checks as explicit selection;
  - a configured default that does not exist is `NotConfigured`, not `Invalid`: the host
    named a convention, and the developer simply has not created it yet;
- no selected name:
  - zero enabled profiles returns `NotConfigured`;
  - one enabled profile may resolve only if this behavior is deliberately approved;
  - multiple enabled profiles returns `Ambiguous`;
  - never use "first profile wins."

The recommended initial design is the strict one: **require a name always**, from either
the caller or the host-configured default, and never select implicitly even when exactly
one enabled profile exists. Implicit single-profile selection can be added later without
breaking anyone; removing it later would be a breaking safety regression. See open
question 3.

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

The developer creates one bootstrap connection in the normal app store:

```text
tigerwrap connections add-e2e-bootstrap
```

or, when the host has configured no default name:

```text
tigerwrap connections add-e2e-bootstrap --name tigerwrap-e2e
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
tigerwrap connections list --tq-connection-store-file C:\temp\e2e.json
```

Note the placement: the option follows the command path. `tigerwrap --tq-connection-store-file C:\temp\e2e.json connections list`
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

No TigerCli dependency is required for library-only CI consumers.

### 12.4 Containers

A container may receive:

- writable runtime JSON store;
- read-only mounted secret files;
- mounted configuration files;
- environment variables for non-file configuration.

TigerQuery resolves the store path through the standard environment variable.

The store references external values and remains writable for temporary connection creation.

### 12.5 Library mode

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

### 12.6 Tool mode

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

Test:

- ordinary profile ignored;
- enabled profile accepted;
- non-canonical Boolean metadata (`True`, `1`, `yes`, ` true `) rejected with a specific error rather than silently false;
- wrong-case metadata key confers nothing;
- database creation requires explicit permission;
- explicit name must still be authorized;
- ambiguity never selects the first profile;
- missing configuration returns `NotConfigured`;
- a configured-but-absent default name returns `NotConfigured`, not `Invalid`;
- resolution opens no SQL connection;
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
- configuring both eager and deferred forms is rejected at `Configure`;
- the host-supplied password protector still reaches the constructed store.

### 15.6 Host integration

Test in `tiger-sqlcmd`, which is the only first-party host migrated by this plan:

- host opts into contribution;
- no duplicate option implementation;
- same store used by connection and run commands;
- existing default behavior unchanged when no override is supplied;
- explicit alternate store works;
- environment-variable store works;
- CLI override wins over environment variable;
- the existing CLI test host can still inject a store, and injection interacts with
  CLI/environment overrides in a documented way (section 18).

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

The CliCore README's "One selected store" section must be rewritten when section 6 lands,
because it currently states that `options.Store` is the only injection point.

Recommended explicit instruction:

> Never discover or probe for SQL Server instances. Resolve E2E access only through TigerQuery's connection-store and E2E authorization APIs. When the resolver reports `NotConfigured`, stop or skip without opening a connection.

## 17. Phased implementation

Phases are ordered by dependency. Each carries a **difficulty rating** naming the
strength of AI coding agent that should take it:

- `Low` — localized, well-defined work with little architectural ambiguity;
- `Medium` — cross-project changes, public API design, or moderate integration risk;
- `High` — security-sensitive, lifecycle-sensitive, highly architectural, or requiring
  careful reasoning across several packages.

### Phase 1 — Core store-path resolution

**Difficulty: Medium.** New public Core API with strict precedence and failure semantics.
The logic is small and self-contained, but it is a published contract that later phases
and third parties depend on, and the "never fall through" rule is easy to get subtly
wrong.

**Scope.** Path resolution only. No CLI, no store construction, no E2E concepts.

**Tasks.**

1. Environment-variable name constant (`SqlServerConnectionStoreEnvironment`).
2. `SqlServerConnectionStorePathOptions`, `…Resolution`, `…Source`, and the resolver.
3. Injected environment reader with the process default.
4. Normalization to absolute, and the section 4.2 syntactic validation set.
5. A dedicated exception (or result) type carrying which source failed and why, in a form
   CliCore can localize.
6. Store presence policy from section 4.3, wired into store opening.
7. XML docs and Core README section.

**Depends on.** Nothing.

**Validation.** Sections 15.1 and 15.2. Assert inertness by resolving paths under a
directory that does not exist and confirming nothing is created.

**Risks / open decisions.** Final environment-variable name (open question 1); whether
the store presence policy is a behavior change too aggressive for existing users
(open question 5); result-type versus exception style for resolution failure.

### Phase 2 — CliCore deferred store selection and the TigerCli contribution

**Difficulty: High.** A breaking change to a published public API, combined with
lifecycle-sensitive callback timing across two packages, plus every internal capture site
in the command group. Getting this wrong produces a run that reads one store and writes
another, which is exactly the failure mode this plan exists to prevent.

**Scope.** All of CliCore. Two coupled workstreams that ship together because neither is
useful alone: deferred store selection (section 6) and the `ITigerCliAppContribution`
implementation (section 5).

**Tasks.**

1. `TigerQueryCliOptions` contribution state: host default path, optional bootstrap name,
   optional protector factory, callback-set explicit value and resolution, lazily
   constructed single `Store`.
2. `TigerQueryCliContribution : ITigerCliAppContribution` registering
   `--tq-connection-store-file` via `GlobalOptions.AddOptionalString` and the environment
   variable via `AddEnvironmentVariable`.
3. Callback that delegates to the Phase 1 resolver and returns a localized
   `TigerCliValidationResult.Error` on failure, using `context.Culture`.
4. Extend `SqlServerConnectionCommandOptions` with the deferred form; reject configuring
   both forms.
5. Convert `SqlServerConnectionCommandContext`, the `connections` provider, the
   `databases` provider, and the `AsEdit` loader to deferred access.
6. Guard against access before the callback with a clear wiring error.
7. CliCore resource entries for the callback's error messages (en-US, pl-PL).
8. Update the CliCore README, including the "One selected store" section.

**Depends on.** Phase 1.

**Validation.** Sections 15.4 and 15.5. Add a test that builds an app, runs it twice with
different `--tq-connection-store-file` values, and asserts each run wrote only its own
file.

**Risks / open decisions.** Compatibility strategy for `options.Store` (open question 12);
whether the callback should resolve eagerly for every command (open question 6);
completion-path behavior (open question 7); the localization gap for the option and
environment descriptions (open question 14).

### Phase 3 — `tiger-sqlcmd` registration and migration

**Difficulty: Medium.** Mechanical in shape but touches the composition root, the static
store ambient, and the existing CLI test harness. Regression risk is concentrated in "the
default path must behave exactly as it does today."

**Scope.** `tiger-sqlcmd` only. It is the single first-party host this plan migrates, and
it is where the deferred plumbing gets proven. No new user-visible behavior beyond the new
option.

TigerWrap is deliberately **not** migrated here, and not in any later phase of this plan.
It stays on the compatibility `SqlServerConnectionCommandOptions.Store` path for the whole
of phases 3–8, unchanged. Migrating it early to "finish" phase 2 or phase 3 is explicitly
not allowed: the point of routing everything through one host first is that a second host
adds integration risk without adding evidence.

**Tasks.**

1. Build the contribution once in `TigerSqlCmdApp.Build`, register it, and pass its state
   to `SqlServerConnectionCommands.Configure` and to the app-level `connections` provider.
2. Replace the `TigerSqlCmdApp.ConnectionStore` static ambient and `TryResolveConnection`
   store lookup with contribution-state access.
3. Decide and implement the test-injection story (section 18) so `CliTestRunner` keeps
   working.
4. Remove any host-side store-path logic and any host `AddEnvironmentVariable` call that
   would now collide with the contribution.
5. Host documentation and help-text review.

**Depends on.** Phase 2.

**Validation.** Section 15.6 against `tiger-sqlcmd`, plus the full existing `tiger-sqlcmd`
CLI test suite passing unchanged in its default-path behavior. TigerWrap is not exercised
by this phase; the evidence that the deferred path works comes from `tiger-sqlcmd` alone.

**Risks / open decisions.** How injected test stores interact with CLI/environment
overrides (open question 13).

### Phase 4 — E2E metadata contract and resolver in Core

**Difficulty: High.** This is the authorization boundary. Every default must be
fail-closed, the value grammar must be strict, and diagnostics must not leak. A weak
implementation here silently reintroduces "reachable means allowed."

**Scope.** Core only. No CLI surface.

**Tasks.**

1. `SqlServerE2eMetadata` constants and the reserved-prefix documentation.
2. Strict key/value grammar (section 8.1) with a specific error for malformed values.
3. `SqlServerE2eResolutionStatus`, `SqlServerE2eConnectionResolution`, resolution options,
   and the resolver.
4. Strict name-required behavior per section 9.1.
5. Structural-validation integration and permission checks.
6. Redaction review of every diagnostic string the resolver can emit.
7. Documented no-connect guarantee, enforced by test.

**Depends on.** Phase 1 (store access). Independent of phases 2 and 3, so it may run in
parallel with them.

**Validation.** Section 15.3, plus a test asserting no `SqlConnection` is constructed
during resolution.

**Risks / open decisions.** Implicit single-profile selection (open question 3);
reserved-key write rejection (open question 8); how frameworks map `NotConfigured` to
skip (open question 11).

### Phase 5 — Bootstrap CLI surface

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
4. Non-promptable options for every value automation needs (section 10.1).
5. Resources for both commands in en-US and pl-PL.
6. Host exit-kind mapping review.

**Depends on.** Phases 2, 3, and 4.

**Validation.** Command-level tests including the no-name failure writing nothing, and a
test proving an `--e2e`-authorized profile is not selected as the bootstrap.

**Risks / open decisions.** Exact flag and metadata shape for the `add` path; whether
bootstrap identity is itself a metadata key or purely a name convention resolved by the
host default (this needs settling in this phase — see open question 15).

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

### Phase 8 — First-party E2E migration and hardening

**Difficulty: High.** The phase where the safety claims are actually proven, through one
host and an external-process test surface.

**Scope.** TigerQuery's own test suite. TigerWrap's tests are out of scope and keep their
current behavior; they move only during the deferred TigerWrap migration.

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
this phase is what makes the complete E2E workflow "proven through `tiger-sqlcmd`", and is
therefore the gate on starting the deferred TigerWrap migration.

**Risks / open decisions.** Skip-mechanism coupling to xUnit (open question 11); how much
of the existing live-test surface must be rewritten rather than adapted.

### Explicitly deferred

Not in scope for any numbered phase above, and not to be added opportunistically:

- **TigerWrap migration onto `TigerQueryCliOptions` and the contribution** — see below;
- `connections e2e enable | disable | show | validate` and any other E2E lifecycle
  command family;
- a user-facing global `--default-e2e-connection-name`;
- implicit single-profile E2E selection;
- any TigerCli change made on TigerQuery's behalf;
- store formats other than the existing JSON file;
- credential providers beyond the existing protector abstraction and the external-value
  references in Phase 6.

**TigerWrap migration.** TigerWrap keeps using
`SqlServerConnectionCommandOptions.Store` for the whole of this plan. No TigerWrap
implementation code changes as part of phases 1–8, and TigerWrap is not migrated early to
unblock, complete, or validate phase 2 or phase 3.

Its migration is separate work that may begin only after **both** of the following hold:

1. this plan is fully implemented — phases 1 through 8 complete;
2. the complete E2E workflow is tested and proven end to end through `tiger-sqlcmd`,
   per the Phase 8 validation.

Doing it in that order means TigerWrap adopts a path that a real host has already run in
anger, rather than two hosts discovering the same design problems in parallel. The
migration itself then mirrors Phase 3: build the contribution once, register it, share one
`TigerQueryCliOptions` with the command group and the host's own services, and drop any
host-side store-path or environment-variable logic that would now collide.

## 18. Compatibility and rollout

**CliCore public API.** Phase 2 changes `SqlServerConnectionCommandOptions`, which is
published. `Store` is kept working for hosts that supply a fixed store, and the deferred
`TigerQuery` form is added alongside it. The two remain **mutually exclusive**: supplying
both is a host configuration error rejected during `Configure`, because a fixed store would
ignore whatever the run selected.

`TigerQueryCliOptions` is the preferred integration path and the target architecture.
`Store` is a **compatibility path** — it exists so an unmigrated host keeps building, not
because a host has a good reason to prefer it. New hosts should use the deferred form.

**When `Store` may be removed.** Not at the end of Phase 3, and not at the end of this
plan. It is retained through the full TigerQuery implementation and the entire
`tiger-sqlcmd` validation cycle, because TigerWrap depends on it for that whole period.
Removal becomes possible only once **all** of the following hold:

1. phases 1–8 are complete and `tiger-sqlcmd` has proven the deferred path end to end;
2. the deferred TigerWrap migration is finished;
3. every first-party host composes its command group through `TigerQueryCliOptions`, so
   nothing first-party still passes `Store`.

Removing it then is a breaking change to a published API and needs a version bump plus a
documented migration note, since the README has instructed hosts to use it.

**Host store injection in tests.** `CliTestRunner` calls `TigerSqlCmdApp.Build(store)`
with a temp-file store. Once the store is run-selected, that overload must mean something
precise. The recommendation is to model test injection as an override of the *application
default*, so `--tq-connection-store-file` and the environment variable still win and the
precedence tests are meaningful, plus a separate explicitly-named pinned mode for tests
that must ignore both. Silently letting an injected instance beat the CLI option would
make the CLI tests prove the opposite of the shipping behavior.

**Rollout order.** Phases 1–3 are shippable as a unit and deliver user-visible value
(`--tq-connection-store-file` plus the environment variable) with no E2E concepts at all.
They establish the new plumbing and exercise it **entirely through `tiger-sqlcmd`**: no
TigerWrap change is required to ship any of them, and none should be made. Phase 4 is
shippable next as a library-only capability. Nothing before Phase 5 changes the command
surface, so the risky CLI work lands after the plumbing has been exercised in a release.

Every phase in this plan therefore ships with exactly one migrated first-party host. The
second host follows later, on the evidence the first one produced.

## 19. Open questions

1. Exact environment-variable name for the store path. The document now uses
   `TIGERQUERY_CONNECTION_STORE_FILE` for symmetry with `--tq-connection-store-file`;
   confirm before Phase 1 ships, because renaming it later is a breaking change for CI.
2. Exact public API names for path resolution.
3. Whether E2E resolution may select the only enabled profile when no name is supplied,
   or must always require a configured/explicit name. Recommended: always require a name.
4. Whether the resolution failure surface is an exception or a result type, and how
   CliCore turns it into a localized `TigerCliValidationResult`.
5. Whether the store-presence policy in section 4.3 is acceptable, given that it changes
   existing behavior for explicitly-pathed stores that do not yet exist.
6. Whether the contribution callback should resolve eagerly on every run, failing
   commands that never touch the store, or defer resolution to first store access and
   accept later, less well-placed errors.
7. Whether TigerCli invokes contribution callbacks on shell-completion paths, and what a
   provider should do if it does not.
8. Whether Core actively rejects writes to the reserved `ittiger.` metadata prefix from
   `connections add --metadata`, or merely documents it as reserved.
9. Database ownership marker used for safe cleanup. Blocks Phase 7.
10. Whether database lifecycle helpers belong entirely in Core or partly in
    `ItTiger.TigerQuery`.
11. How test frameworks should map `NotConfigured` to skip behavior without coupling Core
    to xUnit, NUnit, or MSTest.
12. ~~Whether `SqlServerConnectionCommandOptions.Store` is retained alongside the deferred
    form or removed with a version bump.~~ **Settled:** retained alongside the deferred
    form as a compatibility path, mutually exclusive with it, and removable only under the
    conditions in section 18 — after TigerWrap has migrated and no first-party host passes
    it.
13. How the existing CLI test harness injects a store once selection is run-time, and how
    that injection ranks against the CLI option and environment variable.
14. How contributed global-option and environment-variable *descriptions* get localized,
    given that 0.9.1 accepts only literal strings at `Build()` time while the rest of the
    host's help is resource-driven. Options: accept English-only help for this one
    option; have the host pass a pre-localized description built from its default culture;
    or request `descriptionResourceKey` overloads in a future TigerCli. This is a TigerCli
    feature gap, not a TigerQuery design choice, and must not be worked around by making
    TigerCli TigerQuery-aware.
15. Whether bootstrap identity is recorded as its own metadata key or exists only as
    "the profile whose name matches the host-configured default". The former survives
    renames and is inspectable; the latter is simpler. Must be settled in Phase 5.
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
- local developers need no environment variables;
- CI/CD can use environment variables and mounted files;
- E2E profiles require explicit TigerQuery metadata, matched exactly;
- bootstrap identity is never inferred from name, authorization, or ordering alone;
- no component discovers SQL Server;
- no unconfigured test run opens a SQL connection or touches the developer's real store;
- library, tool, and mixed modes use identical contracts;
- `tiger-sqlcmd` reuses the shared implementation rather than its own, and proves the
  complete E2E workflow end to end;
- third-party developers can adopt the same safe workflow without inventing their own conventions.

TigerWrap is not part of these criteria. Its migration is deferred work gated on this
plan being complete and proven through `tiger-sqlcmd`, and the plan is accepted with
TigerWrap still on the `SqlServerConnectionCommandOptions.Store` compatibility path.
