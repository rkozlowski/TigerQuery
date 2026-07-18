using System.Collections.ObjectModel;
using System.Text.Json;

namespace ItTiger.TigerQuery.Core;

public sealed class SqlServerConnectionStore
{
    private readonly SqlServerConnectionStoreOptions options;
    private readonly IConnectionPasswordProtector passwordProtector;

    private readonly JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };

    public SqlServerConnectionStore(SqlServerConnectionStoreOptions options)
        : this(options, ConnectionPasswordProtector.CreateDefault())
    {
    }

    public SqlServerConnectionStore(
        SqlServerConnectionStoreOptions options,
        IConnectionPasswordProtector passwordProtector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(passwordProtector);

        this.options = options;
        this.passwordProtector = passwordProtector;
    }

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
    public void Add(SqlServerConnectionProfile connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var connections = Load().ToList();
        if (connections.Any(i => i.Name == connection.Name))
            throw new InvalidOperationException($"A connection named '{connection.Name}' already exists.");

        connections.Add(connection);
        Save(connections);
    }

    public bool Exists(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Load().Any(i => i.Name == name);
    }

    public SqlServerConnectionProfile? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Load().FirstOrDefault(i => i.Name == name);
    }

    /// <summary>
    /// Returns profiles matching every supplied metadata filter, preserving store order.
    /// Keys and values are compared ordinally and case-sensitively.
    /// </summary>
    /// <remarks>An empty filter collection returns all profiles.</remarks>
    /// <exception cref="ArgumentException">
    /// A filter has an empty key, an unsupported operator, a missing value for
    /// <c>Equals</c>, or a value for <c>IsSet</c>/<c>IsNotSet</c>.
    /// </exception>
    public IReadOnlyList<SqlServerConnectionProfile> QueryByMetadata(
        IEnumerable<SqlServerConnectionMetadataFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var filterList = filters.ToList();
        foreach (var filter in filterList)
            ValidateMetadataFilter(filter);

        return Load()
            .Where(profile => filterList.All(filter => MatchesMetadataFilter(profile, filter)))
            .ToList();
    }

    public void AddOrUpdate(SqlServerConnectionProfile connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var connections = Load().ToList();
        connections.RemoveAll(i => i.Name == connection.Name);
        connections.Add(connection);
        Save(connections);
    }

    public bool Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var connections = Load().ToList();
        var removed = connections.RemoveAll(i => i.Name == name) > 0;
        if (removed)
            Save(connections);

        return removed;
    }

    public async Task<IReadOnlyList<string>> GetConnectionNamesAsync(CancellationToken _)
    {
        var connections = Load().ToList().Select(n => n.Name).ToList().AsReadOnly();
        return await Task.FromResult(connections);
    }

    private static void ValidateMetadataFilter(SqlServerConnectionMetadataFilter? filter)
    {
        if (filter is null)
            throw new ArgumentException("Metadata filters must not contain null entries.", "filters");

        if (string.IsNullOrEmpty(filter.Key))
            throw new ArgumentException("A metadata filter key must not be empty.", "filters");

        switch (filter.Operator)
        {
            case SqlServerConnectionMetadataFilterOperator.Equals when filter.Value is null:
                throw new ArgumentException(
                    "An Equals metadata filter requires a value.",
                    "filters");

            case SqlServerConnectionMetadataFilterOperator.IsSet
                or SqlServerConnectionMetadataFilterOperator.IsNotSet
                when filter.Value is not null:
                throw new ArgumentException(
                    "IsSet and IsNotSet metadata filters must not have a value.",
                    "filters");

            case SqlServerConnectionMetadataFilterOperator.Equals
                or SqlServerConnectionMetadataFilterOperator.IsSet
                or SqlServerConnectionMetadataFilterOperator.IsNotSet:
                break;

            default:
                throw new ArgumentException(
                    $"Unsupported metadata filter operator: {filter.Operator}.",
                    "filters");
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
