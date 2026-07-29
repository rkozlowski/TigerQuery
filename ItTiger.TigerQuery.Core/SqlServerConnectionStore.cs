using System.Collections.ObjectModel;
using System.Text.Json;

namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Persists named SQL Server connection profiles as an indented JSON array.
/// </summary>
/// <remarks>
/// <para>
/// Operations load or rewrite the complete file and are not synchronized; callers
/// must coordinate concurrent access. Returned profiles are detached mutable
/// objects, so changes require an explicit <see cref="AddOrUpdate"/> or
/// <see cref="Save"/> call.
/// </para>
/// <para>
/// Existing JSON without metadata remains compatible. Empty metadata is omitted,
/// and metadata keys are written in ordinal order.
/// </para>
/// </remarks>
public sealed class SqlServerConnectionStore
{
    private readonly SqlServerConnectionStoreOptions options;
    private readonly IConnectionPasswordProtector passwordProtector;

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

        this.options = options;
        this.passwordProtector = passwordProtector;
    }

    /// <summary>Loads and unprotects all profiles in store order.</summary>
    /// <returns>
    /// A newly materialized list, or an empty list when the file does not exist
    /// or contains JSON <see langword="null"/>.
    /// </returns>
    /// <remarks>Each loaded profile is passed to the configured password protector.</remarks>
    public IReadOnlyList<SqlServerConnectionProfile> Load()
    {
        if (!File.Exists(options.FilePath))
            return [];

        var json = File.ReadAllText(options.FilePath);
        var list = JsonSerializer.Deserialize<List<SqlServerConnectionProfile>>(json) ?? [];

        foreach (var profile in list)
            passwordProtector.UnprotectAfterLoad(profile);

        return list;
    }

    /// <summary>Protects and writes a complete replacement profile collection.</summary>
    /// <param name="connections">The profiles to persist in enumeration order.</param>
    /// <remarks>
    /// The sequence is materialized once. The configured protector may mutate
    /// password fields on the supplied profile objects before serialization.
    /// Missing parent directories are created.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="connections"/> is <see langword="null"/>.
    /// </exception>
    public void Save(IEnumerable<SqlServerConnectionProfile> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var list = connections.ToList();

        foreach (var profile in list)
            passwordProtector.ProtectForSave(profile);

        var directory = Path.GetDirectoryName(options.FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(list, jsonSerializerOptions);
        File.WriteAllText(options.FilePath, json);
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
    public void Add(SqlServerConnectionProfile connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var connections = Load().ToList();
        if (connections.Any(i => i.Name == connection.Name))
            throw new InvalidOperationException($"A connection named '{connection.Name}' already exists.");

        connections.Add(connection);
        Save(connections);
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
    public void AddOrUpdate(SqlServerConnectionProfile connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var connections = Load().ToList();
        connections.RemoveAll(i => i.Name == connection.Name);
        connections.Add(connection);
        Save(connections);
    }

    /// <summary>Deletes every profile with an exact name match.</summary>
    /// <param name="name">The nonblank, case-sensitive profile name.</param>
    /// <returns><see langword="true"/> when at least one profile was deleted.</returns>
    /// <remarks>The file is rewritten only when a profile is removed.</remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is null, empty, or whitespace. The parameter name is
    /// <c>name</c>.
    /// </exception>
    public bool Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var connections = Load().ToList();
        var removed = connections.RemoveAll(i => i.Name == name) > 0;
        if (removed)
            Save(connections);

        return removed;
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
