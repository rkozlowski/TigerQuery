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
}
