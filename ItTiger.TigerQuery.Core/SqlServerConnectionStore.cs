using System.Collections.ObjectModel;
using System.Text.Json;

namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Persists named SQL Server connection profiles as an indented JSON array in one
/// selected file.
/// </summary>
/// <remarks>
/// <para>
/// Every operation uses the file named by the options this instance was constructed
/// with, resolved once to an absolute path and exposed as <see cref="FilePath"/>.
/// Nothing in the store probes another location or falls back to a default when the
/// selected file is missing, malformed, or inaccessible: an application that selects
/// a store selects it for lookup, filtering, copy, add, update, save, and delete
/// alike. There is deliberately no universal default store; a host chooses
/// <see cref="SqlServerConnectionStoreOptions.Shared"/>,
/// <see cref="SqlServerConnectionStoreOptions.AppSpecific"/>, or an explicit
/// <see cref="SqlServerConnectionStoreOptions.FilePath"/> once and injects the
/// resulting instance everywhere.
/// </para>
/// <para>
/// Mutating operations (<see cref="Add"/>, <see cref="AddOrUpdate"/>,
/// <see cref="Copy"/>, <see cref="Delete"/>, and <see cref="Save"/>) are
/// coordinated by normalized path and replace the file in one step. Concurrent
/// mutations therefore cannot lose one another's updates within a process, and a
/// mutation that fails leaves the previous file intact rather than truncated. See
/// <see cref="SqlServerConnectionStoreOptions.MutationTimeout"/> for the wait budget
/// and the guarantee's exact boundaries. Reads are not coordinated because the
/// atomic replacement never exposes a partially written file.
/// </para>
/// <para>
/// Returned profiles are detached mutable objects, so changes require an explicit
/// <see cref="AddOrUpdate"/> or <see cref="Save"/> call.
/// </para>
/// <para>
/// Existing JSON without metadata remains compatible. Empty metadata is omitted,
/// and metadata keys are written in ordinal order.
/// </para>
/// </remarks>
public sealed class SqlServerConnectionStore
{
    private readonly IConnectionPasswordProtector passwordProtector;
    private readonly ConnectionStoreFile storeFile;

    private readonly JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };

    /// <summary>Initializes a store with the platform-default password protector.</summary>
    /// <param name="options">The JSON file location.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public SqlServerConnectionStore(SqlServerConnectionStoreOptions options)
        : this(options, ConnectionPasswordProtector.CreateDefault())
    {
    }

    /// <summary>Initializes a store with an explicit password strategy.</summary>
    /// <param name="options">The JSON file location.</param>
    /// <param name="passwordProtector">
    /// The strategy invoked around serialization and deserialization.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> or <paramref name="passwordProtector"/> is
    /// <see langword="null"/>.
    /// </exception>
    public SqlServerConnectionStore(
        SqlServerConnectionStoreOptions options,
        IConnectionPasswordProtector passwordProtector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(passwordProtector);

        this.passwordProtector = passwordProtector;
        storeFile = new ConnectionStoreFile(options.FilePath, options.MutationTimeout);
    }

    /// <summary>Gets the absolute path of the JSON file this store operates on.</summary>
    /// <remarks>
    /// The path is normalized once during construction, so a relative
    /// <see cref="SqlServerConnectionStoreOptions.FilePath"/> is resolved against the
    /// working directory at that moment rather than at each call. Diagnostics and
    /// tests can use this value to prove which store an operation used; it names a
    /// file and never reveals profile contents or secrets.
    /// </remarks>
    public string FilePath => storeFile.FilePath;

    /// <summary>
    /// Set by tests to observe or fail a write after the temporary file exists and
    /// before the destination is replaced.
    /// </summary>
    internal Action<string>? WriteFaultHook
    {
        get => storeFile.WriteFaultHook;
        set => storeFile.WriteFaultHook = value;
    }

    /// <summary>Loads and unprotects all profiles in store order.</summary>
    /// <returns>
    /// A newly materialized list, or an empty list when the file does not exist
    /// or contains JSON <see langword="null"/>.
    /// </returns>
    /// <remarks>Each loaded profile is passed to the configured password protector.</remarks>
    public IReadOnlyList<SqlServerConnectionProfile> Load()
    {
        var list = LoadPersisted();

        foreach (var profile in list)
            passwordProtector.UnprotectAfterLoad(profile);

        return list;
    }

    /// <summary>Protects and writes a complete replacement profile collection.</summary>
    /// <param name="connections">The profiles to persist in enumeration order.</param>
    /// <remarks>
    /// The sequence is materialized once. The configured protector may mutate
    /// password fields on the supplied profile objects before serialization.
    /// Missing parent directories are created, and the file is replaced atomically
    /// under the store's mutation coordination.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="connections"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Mutation coordination for <see cref="FilePath"/> could not be acquired within
    /// <see cref="SqlServerConnectionStoreOptions.MutationTimeout"/>.
    /// </exception>
    public void Save(IEnumerable<SqlServerConnectionProfile> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var list = connections.ToList();

        foreach (var profile in list)
            passwordProtector.ProtectForSave(profile);

        using var scope = storeFile.EnterMutationScope();
        SavePersisted(list);
    }

    // The at-rest read/write pair. Nothing here unprotects or re-protects a password,
    // so a mutation that only adds, renames, or removes an entry leaves every other
    // profile's stored ciphertext byte-for-byte unchanged.
    private List<SqlServerConnectionProfile> LoadPersisted()
    {
        var json = storeFile.ReadAllTextOrNull();
        if (json is null)
            return [];

        return JsonSerializer.Deserialize<List<SqlServerConnectionProfile>>(json) ?? [];
    }

    private void SavePersisted(List<SqlServerConnectionProfile> connections)
    {
        var json = JsonSerializer.Serialize(connections, jsonSerializerOptions);
        storeFile.WriteAtomic(json);
    }

    /// <summary>
    /// Adds a new connection. Throws when a connection with the same name already
    /// exists; this is a pure add, not an upsert.
    /// </summary>
    /// <param name="connection">The profile to append.</param>
    /// <remarks>Profile-name matching is ordinal and case-sensitive.</remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="connection"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A profile with exactly the same <see cref="SqlServerConnectionProfile.Name"/>
    /// already exists.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Mutation coordination for <see cref="FilePath"/> could not be acquired within
    /// <see cref="SqlServerConnectionStoreOptions.MutationTimeout"/>.
    /// </exception>
    public void Add(SqlServerConnectionProfile connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var scope = storeFile.EnterMutationScope();

        var connections = LoadPersisted();
        if (connections.Any(i => i.Name == connection.Name))
            throw new InvalidOperationException($"A connection named '{connection.Name}' already exists.");

        passwordProtector.ProtectForSave(connection);
        connections.Add(connection);
        SavePersisted(connections);
    }

    /// <summary>Determines whether an exactly named profile exists.</summary>
    /// <param name="name">The nonblank, case-sensitive profile name.</param>
    /// <returns><see langword="true"/> when a matching profile exists.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is null, empty, or whitespace. The parameter name is
    /// <c>name</c>.
    /// </exception>
    public bool Exists(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Load().Any(i => i.Name == name);
    }

    /// <summary>Finds the first exactly named profile.</summary>
    /// <param name="name">The nonblank, case-sensitive profile name.</param>
    /// <returns>A detached mutable profile, or <see langword="null"/> when absent.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is null, empty, or whitespace. The parameter name is
    /// <c>name</c>.
    /// </exception>
    public SqlServerConnectionProfile? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Load().FirstOrDefault(i => i.Name == name);
    }

    /// <summary>
    /// Returns profiles matching every supplied metadata filter, preserving store order.
    /// Keys and values are compared ordinally and case-sensitively.
    /// </summary>
    /// <param name="filters">The metadata predicates combined using AND semantics.</param>
    /// <returns>Matching detached profiles in their persisted order.</returns>
    /// <remarks>
    /// An empty filter collection returns all profiles. <c>Equals</c> requires
    /// the key to exist with the exact value; <c>IsSet</c> requires the key to
    /// exist even when its value is empty; <c>IsNotSet</c> requires absence.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="filters"/> is <see langword="null"/>. The parameter name is
    /// <c>filters</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A filter entry is null, has an empty key or unsupported operator, lacks a
    /// value for <c>Equals</c>, or supplies a value for <c>IsSet</c>/<c>IsNotSet</c>.
    /// The parameter name is <c>filter</c>.
    /// </exception>
    public IReadOnlyList<SqlServerConnectionProfile> QueryByMetadata(
        IEnumerable<SqlServerConnectionMetadataFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var filterList = filters.ToList();
        foreach (var filter in filterList)
            ValidateMetadataFilter(filter);

        var matches = Load()
            .Where(profile => filterList.All(filter => MatchesMetadataFilter(profile, filter)))
            .ToList();
        return matches;
    }

    /// <summary>Replaces every exactly named profile and appends the supplied profile.</summary>
    /// <param name="connection">The profile to persist.</param>
    /// <remarks>
    /// Matching is ordinal and case-sensitive. Replacement moves the profile to
    /// the end of store order.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="connection"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Mutation coordination for <see cref="FilePath"/> could not be acquired within
    /// <see cref="SqlServerConnectionStoreOptions.MutationTimeout"/>.
    /// </exception>
    public void AddOrUpdate(SqlServerConnectionProfile connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var scope = storeFile.EnterMutationScope();

        var connections = LoadPersisted();
        connections.RemoveAll(i => i.Name == connection.Name);
        passwordProtector.ProtectForSave(connection);
        connections.Add(connection);
        SavePersisted(connections);
    }

    /// <summary>
    /// Copies an existing profile inside this store under a new name, preserving
    /// every persisted setting that is not explicitly overridden.
    /// </summary>
    /// <param name="sourceName">The nonblank, case-sensitive name of the profile to copy.</param>
    /// <param name="options">The target name and the controlled overrides to apply.</param>
    /// <param name="validationPolicy">
    /// The policy the resulting profile is validated against;
    /// <see cref="SqlServerConnectionValidationPolicy.DatabaseOptional"/> when
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The detached profile exactly as it was persisted. It can be resolved, edited,
    /// and later removed through the ordinary <see cref="Find"/>,
    /// <see cref="AddOrUpdate"/>, and <see cref="Delete"/> APIs.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The operation is same-store by construction: it is an instance method with no
    /// destination parameter, so a copy cannot silently cross stores. Source lookup,
    /// duplicate-name detection, validation, and persistence all happen while one
    /// mutation scope is held, so a concurrent mutation cannot slip a conflicting
    /// name in between the check and the write.
    /// </para>
    /// <para>
    /// The copy is taken from the at-rest representation, so the source's protected
    /// password is duplicated exactly as stored. The password is never decrypted,
    /// reconstructed, logged, or handed to the caller as plaintext, and the copy
    /// succeeds even when the current user cannot decrypt the blob; whether the copy
    /// is usable afterwards follows the normal resolver and protector behavior.
    /// Neither the source nor any unrelated profile is re-protected, so their stored
    /// ciphertext is unchanged.
    /// </para>
    /// <para>
    /// Every persisted field is carried by the profile's own JSON contract rather
    /// than a hand-written property list, so a field added in a later release is
    /// copied without a caller change. Overrides are limited to the profile name,
    /// the initial catalog, and the named metadata entries; all other metadata is
    /// preserved. Copy is never an upsert and never opens a SQL connection.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="sourceName"/> is null, empty, or whitespace (parameter name
    /// <c>sourceName</c>), or <paramref name="options"/> has a blank target name or
    /// invalid metadata mutations (parameter name <c>options</c>).
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The source profile does not exist, a profile already uses
    /// <see cref="SqlServerConnectionCopyOptions.TargetName"/>, or the resulting
    /// profile fails validation. The store is left unchanged in every case.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Mutation coordination for <see cref="FilePath"/> could not be acquired within
    /// <see cref="SqlServerConnectionStoreOptions.MutationTimeout"/>.
    /// </exception>
    public SqlServerConnectionProfile Copy(
        string sourceName,
        SqlServerConnectionCopyOptions options,
        SqlServerConnectionValidationPolicy? validationPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(nameof(options));

        var policy = validationPolicy ?? SqlServerConnectionValidationPolicy.DatabaseOptional;

        using var scope = storeFile.EnterMutationScope();

        var connections = LoadPersisted();

        var source = connections.FirstOrDefault(i => i.Name == sourceName)
            ?? throw new InvalidOperationException(
                $"A connection named '{sourceName}' was not found.");

        if (connections.Any(i => i.Name == options.TargetName))
        {
            throw new InvalidOperationException(
                $"A connection named '{options.TargetName}' already exists.");
        }

        var copy = source.ClonePersisted();
        copy.Name = options.TargetName;

        if (options.InitialCatalogOverride is { } initialCatalog)
            copy.Database = initialCatalog.Length == 0 ? null : initialCatalog;

        foreach (var key in options.MetadataToRemove)
            copy.RemoveMetadata(key);

        foreach (var (key, value) in options.MetadataToSet)
            copy.SetMetadata(key, value);

        var errors = SqlServerConnectionValidator.ValidateComplete(copy, policy);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"The copied connection '{options.TargetName}' is not valid: {string.Join(" ", errors)}");
        }

        connections.Add(copy);
        SavePersisted(connections);

        return copy;
    }

    // Trusted assemblies use this after database creation or for an explicitly
    // non-owning clone. Keeping it internal prevents generic store callers from granting
    // drop ownership by passing a Boolean to an otherwise ordinary copy operation.
    internal SqlServerConnectionProfile CopyForE2eSession(
        string sourceName,
        string targetName,
        string databaseName,
        Guid sessionId,
        bool allowDatabaseDrop)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        if (sessionId == Guid.Empty)
            throw new ArgumentException("A non-empty E2E session ID is required.", nameof(sessionId));

        using var scope = storeFile.EnterMutationScope();
        var connections = LoadPersisted();
        var source = connections.FirstOrDefault(i => i.Name == sourceName)
            ?? throw new InvalidOperationException($"A connection named '{sourceName}' was not found.");
        if (connections.Any(i => i.Name == targetName))
            throw new InvalidOperationException($"A connection named '{targetName}' already exists.");

        var copy = source.ClonePersisted();
        copy.Name = targetName;
        if (copy.ConnectionStringValue is null)
            copy.Database = databaseName;
        else
            copy.InitialCatalogOverride = databaseName;

        SqlServerE2eMetadata.ConfigureSessionProfile(
            copy,
            sessionId,
            databaseName,
            allowDatabaseDrop);

        var errors = SqlServerConnectionValidator.ValidateComplete(
            copy,
            SqlServerConnectionValidationPolicy.DatabaseRequired);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"The copied connection '{targetName}' is not valid: {string.Join(" ", errors)}");
        }

        connections.Add(copy);
        SavePersisted(connections);
        return copy;
    }

    /// <summary>Deletes every profile with an exact name match.</summary>
    /// <param name="name">The nonblank, case-sensitive profile name.</param>
    /// <returns><see langword="true"/> when at least one profile was deleted.</returns>
    /// <remarks>The file is rewritten only when a profile is removed.</remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is null, empty, or whitespace. The parameter name is
    /// <c>name</c>.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Mutation coordination for <see cref="FilePath"/> could not be acquired within
    /// <see cref="SqlServerConnectionStoreOptions.MutationTimeout"/>.
    /// </exception>
    public bool Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var scope = storeFile.EnterMutationScope();

        var connections = LoadPersisted();
        var protectedProfile = connections.FirstOrDefault(i => i.Name == name);
        var dropFlag = protectedProfile is null
            ? SqlServerE2eFlagState.Absent
            : SqlServerE2eMetadata.ReadFlag(
                protectedProfile,
                SqlServerE2eMetadata.AllowDatabaseDrop);
        if (protectedProfile is not null
            && dropFlag is SqlServerE2eFlagState.True or SqlServerE2eFlagState.Malformed)
        {
            var session = protectedProfile.Metadata.TryGetValue(
                SqlServerE2eMetadata.SessionId,
                out var storedSession)
                ? storedSession
                : "<guid>";
            throw new InvalidOperationException(
                $"Connection '{name}' owns an E2E database and cannot be deleted normally. "
                + $"Use 'tiger-sqlcmd e2e drop --connection {name} --session-id {session}' "
                + $"or 'tiger-sqlcmd e2e cleanup --session-id {session}'.");
        }
        var removed = connections.RemoveAll(i => i.Name == name) > 0;
        if (removed)
            SavePersisted(connections);

        return removed;
    }

    // Only the E2E lifecycle may bypass the ordinary owning-record deletion guard, and
    // only after it has applied the database ownership rules.
    internal bool DeleteE2eSessionConnection(string name, Guid sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (sessionId == Guid.Empty)
            throw new ArgumentException("A non-empty E2E session ID is required.", nameof(sessionId));

        using var scope = storeFile.EnterMutationScope();
        var connections = LoadPersisted();
        var profile = connections.FirstOrDefault(i => i.Name == name);
        if (profile is null)
            return false;

        var expectedSession = sessionId.ToString("D");
        if (SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Enabled)
                != SqlServerE2eFlagState.True
            || SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Bootstrap)
                != SqlServerE2eFlagState.False
            || !profile.Metadata.TryGetValue(SqlServerE2eMetadata.SessionId, out var storedSession)
            || !string.Equals(storedSession, expectedSession, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Connection '{name}' is not a non-bootstrap E2E connection owned by session '{expectedSession}'.");
        }

        connections.Remove(profile);
        SavePersisted(connections);
        return true;
    }

    /// <summary>Loads profile names in store order for provider-based selection.</summary>
    /// <param name="_">
    /// A compatibility cancellation token; the current synchronous file load does
    /// not observe it.
    /// </param>
    /// <returns>A task containing a read-only list of profile names.</returns>
    public async Task<IReadOnlyList<string>> GetConnectionNamesAsync(CancellationToken _)
    {
        var connections = Load().ToList().Select(n => n.Name).ToList().AsReadOnly();
        return await Task.FromResult(connections);
    }

    private static void ValidateMetadataFilter(SqlServerConnectionMetadataFilter? filter)
    {
        if (filter is null)
            throw new ArgumentException("Metadata filters must not contain null entries.", nameof(filter));

        if (string.IsNullOrEmpty(filter.Key))
            throw new ArgumentException("A metadata filter key must not be empty.", nameof(filter));

        switch (filter.Operator)
        {
            case SqlServerConnectionMetadataFilterOperator.Equals when filter.Value is null:
                throw new ArgumentException(
                    "An Equals metadata filter requires a value.",
                    nameof(filter));

            case SqlServerConnectionMetadataFilterOperator.IsSet
                or SqlServerConnectionMetadataFilterOperator.IsNotSet
                when filter.Value is not null:
                throw new ArgumentException(
                    "IsSet and IsNotSet metadata filters must not have a value.",
                    nameof(filter));

            case SqlServerConnectionMetadataFilterOperator.Equals
                or SqlServerConnectionMetadataFilterOperator.IsSet
                or SqlServerConnectionMetadataFilterOperator.IsNotSet:
                break;

            default:
                throw new ArgumentException(
                    $"Unsupported metadata filter operator: {filter.Operator}.",
                    nameof(filter));
        }
    }

    private static bool MatchesMetadataFilter(
        SqlServerConnectionProfile profile,
        SqlServerConnectionMetadataFilter filter)
    {
        var exists = profile.Metadata.TryGetValue(filter.Key, out var actualValue);

        return filter.Operator switch
        {
            SqlServerConnectionMetadataFilterOperator.Equals =>
                exists && string.Equals(actualValue, filter.Value, StringComparison.Ordinal),
            SqlServerConnectionMetadataFilterOperator.IsSet => exists,
            SqlServerConnectionMetadataFilterOperator.IsNotSet => !exists,
            _ => false
        };
    }
}
