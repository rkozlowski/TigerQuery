using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.E2e;

/// <summary>Creates, clones, drops, and cleans up durable session-scoped E2E resources.</summary>
public sealed class SqlServerE2eSessionLifecycle
{
    private readonly SqlServerConnectionStore store;
    private readonly string bootstrapConnectionName;
    private readonly SqlServerE2eDatabaseLifecycleOptions options;
    private readonly ISqlServerE2eDatabaseExecutor executor;
    private readonly Func<string> uniqueSuffixFactory;

    /// <summary>Initializes the lifecycle over one selected store and bootstrap name.</summary>
    /// <param name="store">The authoritative connection store.</param>
    /// <param name="bootstrapConnectionName">The exact expected bootstrap profile name.</param>
    /// <param name="options">Optional external-value readers.</param>
    public SqlServerE2eSessionLifecycle(
        SqlServerConnectionStore store,
        string bootstrapConnectionName,
        SqlServerE2eDatabaseLifecycleOptions? options = null)
        : this(
            store,
            bootstrapConnectionName,
            options ?? new SqlServerE2eDatabaseLifecycleOptions(),
            new TigerQueryE2eDatabaseExecutor(),
            () => Guid.NewGuid().ToString("N"))
    {
    }

    internal SqlServerE2eSessionLifecycle(
        SqlServerConnectionStore store,
        string bootstrapConnectionName,
        SqlServerE2eDatabaseLifecycleOptions options,
        ISqlServerE2eDatabaseExecutor executor,
        Func<string> uniqueSuffixFactory)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapConnectionName);
        this.bootstrapConnectionName = bootstrapConnectionName;
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.uniqueSuffixFactory = uniqueSuffixFactory
            ?? throw new ArgumentNullException(nameof(uniqueSuffixFactory));
    }

    /// <summary>Creates one database and its inseparable owning connection record.</summary>
    public async Task<SqlServerE2eCreateResult> CreateAsync(
        Guid sessionId,
        string? databaseNamePart = null,
        string? connectionNamePart = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        var bootstrap = ResolveBootstrap(requireDatabaseCreation: true);
        var suffix = uniqueSuffixFactory();
        var databaseName = SqlServerE2eNames.Database(databaseNamePart, suffix);
        var connectionName = SqlServerE2eNames.Connection(connectionNamePart, suffix);

        await executor.ExecuteAsync(
            BuildConnectionString(bootstrap, "master"),
            $"CREATE DATABASE {QuoteIdentifier(databaseName)};",
            cancellationToken).ConfigureAwait(false);

        try
        {
            store.CopyForE2eSession(
                bootstrap.Name,
                connectionName,
                databaseName,
                sessionId,
                allowDatabaseDrop: true);
        }
        catch (Exception persistenceFailure)
        {
            try
            {
                SqlServerE2eNames.ValidateDroppableDatabase(databaseName);
                await executor.ExecuteAsync(
                    BuildConnectionString(bootstrap, "master"),
                    $"DROP DATABASE {QuoteIdentifier(databaseName)};",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackFailure)
            {
                throw new SqlServerE2eCreateException(
                    databaseName,
                    connectionName,
                    rollbackSucceeded: false,
                    persistenceFailure,
                    rollbackFailure);
            }

            throw new SqlServerE2eCreateException(
                databaseName,
                connectionName,
                rollbackSucceeded: true,
                persistenceFailure);
        }

        return new SqlServerE2eCreateResult(databaseName, connectionName);
    }

    /// <summary>Clones a source connection for an existing database without owning it.</summary>
    public SqlServerConnectionProfile CloneForExistingDatabase(
        string sourceConnection,
        string databaseName,
        Guid sessionId,
        string? connectionNamePart = null)
    {
        ValidateSessionId(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        var connectionName = SqlServerE2eNames.Connection(
            connectionNamePart,
            uniqueSuffixFactory());
        return store.CopyForE2eSession(
            sourceConnection,
            connectionName,
            databaseName,
            sessionId,
            allowDatabaseDrop: false);
    }

    /// <summary>Drops or detaches one exact connection authorized by protected metadata.</summary>
    public async Task<SqlServerE2eDropResult> DropAsync(
        string connectionName,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ValidateSessionId(sessionId);
        var profile = store.Find(connectionName)
            ?? throw new InvalidOperationException($"A connection named '{connectionName}' was not found.");
        var databaseName = ValidateSessionProfile(profile, sessionId, out var allowDrop);

        if (!allowDrop)
        {
            store.DeleteE2eSessionConnection(connectionName, sessionId);
            return new SqlServerE2eDropResult(
                connectionName,
                databaseName,
                SqlServerE2eDropDisposition.ConnectionRemoved);
        }

        SqlServerE2eNames.ValidateDroppableDatabase(databaseName);
        var bootstrap = ResolveBootstrap(requireDatabaseCreation: false);
        var master = BuildConnectionString(bootstrap, "master");
        var existing = await executor.QueryNamesAsync(
            master,
            $"SELECT [name] FROM sys.databases WHERE [name] = N'{EscapeSqlString(databaseName)}';",
            cancellationToken).ConfigureAwait(false);

        var exactMatch = existing.Any(
            name => string.Equals(name, databaseName, StringComparison.Ordinal));
        if (existing.Count > 0 && !exactMatch)
        {
            throw new InvalidOperationException(
                $"Database '{databaseName}' could not be confirmed absent because SQL Server returned a non-exact name match.");
        }

        var disposition = SqlServerE2eDropDisposition.DatabaseAbsentAndConnectionRemoved;
        if (exactMatch)
        {
            executor.ClearPool(BuildConnectionString(bootstrap, databaseName));
            await executor.ExecuteAsync(
                master,
                $"DROP DATABASE {QuoteIdentifier(databaseName)};",
                cancellationToken).ConfigureAwait(false);
            disposition = SqlServerE2eDropDisposition.DatabaseDroppedAndConnectionRemoved;
        }

        store.DeleteE2eSessionConnection(connectionName, sessionId);
        return new SqlServerE2eDropResult(connectionName, databaseName, disposition);
    }

    /// <summary>Cleans every protected non-bootstrap connection for one exact session.</summary>
    public async Task<SqlServerE2eCleanupResult> CleanupAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        var canonicalSession = sessionId.ToString("D");
        var candidates = store.Load()
            .Where(profile =>
                SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Enabled)
                    == SqlServerE2eFlagState.True
                && SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Bootstrap)
                    == SqlServerE2eFlagState.False
                && profile.Metadata.TryGetValue(SqlServerE2eMetadata.SessionId, out var storedSession)
                && string.Equals(storedSession, canonicalSession, StringComparison.Ordinal))
            .Select(profile => profile.Name)
            .ToList();

        var items = new List<SqlServerE2eCleanupItemResult>();
        foreach (var connectionName in candidates)
        {
            try
            {
                var result = await DropAsync(connectionName, sessionId, cancellationToken)
                    .ConfigureAwait(false);
                items.Add(new SqlServerE2eCleanupItemResult(
                    result.ConnectionName,
                    result.DatabaseName,
                    result.Disposition,
                    null));
            }
            catch (Exception ex)
            {
                items.Add(new SqlServerE2eCleanupItemResult(
                    connectionName,
                    null,
                    null,
                    ex.Message));
            }
        }

        return new SqlServerE2eCleanupResult(items);
    }

    private SqlServerConnectionProfile ResolveBootstrap(bool requireDatabaseCreation)
    {
        var resolution = SqlServerE2eConnectionResolver.Resolve(
            store,
            new SqlServerE2eConnectionResolutionOptions
            {
                ConnectionName = bootstrapConnectionName,
                RequireDatabaseCreationPermission = requireDatabaseCreation,
                ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional
            });
        if (resolution.Status != SqlServerE2eResolutionStatus.Resolved || resolution.Profile is null)
        {
            throw new InvalidOperationException(
                $"Connection '{bootstrapConnectionName}' is not authorized as the E2E bootstrap. "
                + string.Join(" ", resolution.Errors));
        }

        return resolution.Profile;
    }

    private static string ValidateSessionProfile(
        SqlServerConnectionProfile profile,
        Guid sessionId,
        out bool allowDrop)
    {
        if (SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Enabled)
                != SqlServerE2eFlagState.True)
            throw InvalidProfile(profile, $"{SqlServerE2eMetadata.Enabled}=true");
        if (SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Bootstrap)
                != SqlServerE2eFlagState.False)
            throw InvalidProfile(profile, $"{SqlServerE2eMetadata.Bootstrap}=false");

        var expectedSession = sessionId.ToString("D");
        if (!profile.Metadata.TryGetValue(SqlServerE2eMetadata.SessionId, out var storedSession)
            || !string.Equals(storedSession, expectedSession, StringComparison.Ordinal))
            throw InvalidProfile(profile, $"an exact {SqlServerE2eMetadata.SessionId} match");
        if (!profile.Metadata.TryGetValue(SqlServerE2eMetadata.DatabaseName, out var databaseName)
            || string.IsNullOrWhiteSpace(databaseName))
            throw InvalidProfile(profile, $"a nonblank {SqlServerE2eMetadata.DatabaseName}");

        allowDrop = SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.AllowDatabaseDrop) switch
        {
            SqlServerE2eFlagState.True => true,
            SqlServerE2eFlagState.False => false,
            _ => throw InvalidProfile(
                profile,
                $"strict {SqlServerE2eMetadata.AllowDatabaseDrop}=true|false")
        };
        return databaseName;
    }

    private static InvalidOperationException InvalidProfile(
        SqlServerConnectionProfile profile,
        string requirement) =>
        new($"Connection '{profile.Name}' does not satisfy protected E2E requirement: {requirement}.");

    private string BuildConnectionString(SqlServerConnectionProfile profile, string databaseName)
    {
        var builder = profile.BuildConnectionStringBuilder(options.ExternalValues);
        builder.InitialCatalog = databaseName;
        return builder.ConnectionString;
    }

    private static void ValidateSessionId(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("A non-empty E2E session ID is required.", nameof(sessionId));
    }

    private static string QuoteIdentifier(string value) =>
        "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string EscapeSqlString(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}

/// <summary>The names created by one atomic E2E create workflow.</summary>
public sealed record SqlServerE2eCreateResult(string DatabaseName, string ConnectionName);

/// <summary>The safe action completed by an E2E drop operation.</summary>
public enum SqlServerE2eDropDisposition
{
    /// <summary>Only the non-owning connection was removed.</summary>
    ConnectionRemoved,

    /// <summary>The owned database was dropped, then its connection record was removed.</summary>
    DatabaseDroppedAndConnectionRemoved,

    /// <summary>The owned database was confirmed absent, then its connection record was removed.</summary>
    DatabaseAbsentAndConnectionRemoved
}

/// <summary>The outcome for one exact E2E connection.</summary>
public sealed record SqlServerE2eDropResult(
    string ConnectionName,
    string DatabaseName,
    SqlServerE2eDropDisposition Disposition);

/// <summary>One success or failure reported during session cleanup.</summary>
public sealed record SqlServerE2eCleanupItemResult(
    string ConnectionName,
    string? DatabaseName,
    SqlServerE2eDropDisposition? Disposition,
    string? Error)
{
    /// <summary>Gets whether this item was cleaned completely.</summary>
    public bool IsSuccess => Error is null;
}

/// <summary>The complete, continue-on-error result of session cleanup.</summary>
public sealed record SqlServerE2eCleanupResult(IReadOnlyList<SqlServerE2eCleanupItemResult> Items)
{
    /// <summary>Gets whether every matching connection was cleaned completely.</summary>
    public bool IsComplete => Items.All(item => item.IsSuccess);
}
