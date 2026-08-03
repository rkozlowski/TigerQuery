using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.E2e;

/// <summary>Creates and cleans up one exactly owned SQL Server E2E database.</summary>
/// <remarks>
/// <para>
/// Each instance can create at most one database. Ownership is the exact name recorded
/// after that create command succeeds; the configured prefix is only a second defensive
/// cleanup guard. The class never adopts, sweeps, or deletes another database.
/// </para>
/// <para>
/// Constructing the lifecycle and inspecting its state perform no I/O. External values
/// are resolved only when an operation needs an effective connection.
/// </para>
/// </remarks>
public sealed class SqlServerE2eDatabaseLifecycle
{
    private const int MaximumDatabaseNameLength = 128;
    private const int UniqueSuffixLength = 32;

    private readonly SqlServerConnectionStore store;
    private readonly SqlServerE2eConnectionResolution bootstrapResolution;
    private readonly SqlServerE2eDatabaseLifecycleOptions options;
    private readonly ISqlServerE2eDatabaseExecutor executor;
    private readonly Func<string> uniqueSuffixFactory;
    private bool databaseWasDropped;
    private string? createdProfileName;
    private SqlServerConnectionProfile? operationalProfile;

    /// <summary>Initializes a lifecycle from an explicitly resolved bootstrap profile.</summary>
    /// <param name="store">The same store that owns the bootstrap profile and any generated copy.</param>
    /// <param name="bootstrapResolution">
    /// The result of expected-name and metadata-authorized E2E bootstrap resolution.
    /// Creation rechecks bootstrap authorization and the database-creation permission
    /// rather than trusting how the result was requested.
    /// </param>
    /// <param name="options">Optional host prefix and external-value readers.</param>
    public SqlServerE2eDatabaseLifecycle(
        SqlServerConnectionStore store,
        SqlServerE2eConnectionResolution bootstrapResolution,
        SqlServerE2eDatabaseLifecycleOptions? options = null)
        : this(
            store,
            bootstrapResolution,
            options ?? new SqlServerE2eDatabaseLifecycleOptions(),
            new TigerQueryE2eDatabaseExecutor(),
            () => Guid.NewGuid().ToString("N"))
    {
    }

    internal SqlServerE2eDatabaseLifecycle(
        SqlServerConnectionStore store,
        SqlServerE2eConnectionResolution bootstrapResolution,
        SqlServerE2eDatabaseLifecycleOptions options,
        ISqlServerE2eDatabaseExecutor executor,
        Func<string> uniqueSuffixFactory)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.bootstrapResolution = bootstrapResolution
            ?? throw new ArgumentNullException(nameof(bootstrapResolution));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.uniqueSuffixFactory = uniqueSuffixFactory
            ?? throw new ArgumentNullException(nameof(uniqueSuffixFactory));

        ValidatePrefix(options.DatabasePrefix);
    }

    /// <summary>Gets the exact database name created by this instance, or null before creation succeeds.</summary>
    /// <remarks>The record is retained after successful cleanup as lifecycle ownership history.</remarks>
    public string? CreatedDatabaseName { get; private set; }

    /// <summary>Gets whether the recorded database has been dropped successfully.</summary>
    public bool DatabaseWasDropped => databaseWasDropped;

    /// <summary>Gets the exact profile name copied by this lifecycle, if any.</summary>
    public string? CreatedProfileName => createdProfileName;

    /// <summary>Creates one uniquely named database and records its exact name on success.</summary>
    /// <param name="cancellationToken">A token observed while resolving and executing SQL.</param>
    /// <returns>The exact generated database name.</returns>
    /// <exception cref="InvalidOperationException">
    /// The bootstrap resolution is not resolved and explicitly bootstrap-authorized,
    /// database creation is not explicitly allowed, or this instance has already created
    /// a database.
    /// </exception>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (CreatedDatabaseName is not null)
            throw new InvalidOperationException("This lifecycle instance has already created a database.");

        var profile = RequireBootstrapProfile(requireDatabaseCreation: true);
        var databaseName = options.DatabasePrefix + uniqueSuffixFactory();

        if (!databaseName.StartsWith(options.DatabasePrefix, StringComparison.Ordinal)
            || databaseName.Length > MaximumDatabaseNameLength)
        {
            throw new InvalidOperationException(
                "The generated database name does not satisfy the configured prefix and length guards.");
        }

        await executor.ExecuteAsync(
            BuildConnectionString(profile, "master"),
            $"CREATE DATABASE {QuoteIdentifier(databaseName)};",
            cancellationToken);

        operationalProfile = profile;
        CreatedDatabaseName = databaseName;
        return databaseName;
    }

    /// <summary>Copies the bootstrap profile for the database created by this instance.</summary>
    /// <param name="profileName">
    /// The new store profile name, or null to use the exact generated database name.
    /// </param>
    /// <returns>The detached profile as persisted by the existing store copy path.</returns>
    /// <remarks>
    /// The normal Core copy, validation, and atomic persistence path is used. External
    /// references and protected values are preserved. A full-connection-string profile
    /// cannot be given a persisted database override without breaking strict mode
    /// separation; such lifecycles can still run setup and teardown SQL directly.
    /// </remarks>
    public SqlServerConnectionProfile AddDatabaseProfile(string? profileName = null)
    {
        var databaseName = RequireLiveOwnedDatabase();
        if (createdProfileName is not null)
            throw new InvalidOperationException("This lifecycle instance has already copied a database profile.");

        var bootstrap = RequireBootstrapProfile(requireDatabaseCreation: true);
        var targetName = profileName ?? databaseName;
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        var copy = store.Copy(
            bootstrap.Name,
            new SqlServerConnectionCopyOptions
            {
                TargetName = targetName,
                InitialCatalogOverride = databaseName
            },
            SqlServerConnectionValidationPolicy.DatabaseRequired);

        createdProfileName = copy.Name;
        return copy;
    }

    /// <summary>Runs setup SQL against the exact database created by this instance.</summary>
    /// <param name="sql">The setup script.</param>
    /// <param name="cancellationToken">A token observed during execution.</param>
    public Task RunSetupSqlAsync(string sql, CancellationToken cancellationToken = default) =>
        RunDatabaseSqlAsync(sql, cancellationToken);

    /// <summary>Runs teardown SQL against the exact database created by this instance.</summary>
    /// <param name="sql">The teardown script.</param>
    /// <param name="cancellationToken">A token observed during execution.</param>
    public Task RunTeardownSqlAsync(string sql, CancellationToken cancellationToken = default) =>
        RunDatabaseSqlAsync(sql, cancellationToken);

    /// <summary>Drops the exact database recorded by this lifecycle instance.</summary>
    /// <param name="cancellationToken">A token observed during cleanup.</param>
    /// <remarks>
    /// This overload supplies the recorded name to the guarded overload. A copied store
    /// profile is removed only after the database has been dropped successfully.
    /// </remarks>
    public Task CleanupAsync(CancellationToken cancellationToken = default) =>
        CleanupAsync(CreatedDatabaseName
            ?? throw new InvalidOperationException("This lifecycle instance has not created a database."),
            cancellationToken);

    /// <summary>Drops a target only when it is the exact live name recorded by this instance.</summary>
    /// <param name="databaseName">The asserted cleanup target.</param>
    /// <param name="cancellationToken">A token observed during cleanup.</param>
    /// <exception cref="InvalidOperationException">
    /// The target is unrecorded, belongs to another instance, was already dropped, or no
    /// longer passes the configured-prefix guard. Prefix match alone never authorizes it.
    /// </exception>
    /// <exception cref="SqlServerE2eDatabaseCleanupException">
    /// SQL cleanup failed. The exception names the exact database left behind and the
    /// ownership record remains available for an explicit retry.
    /// </exception>
    public async Task CleanupAsync(
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        var recorded = RequireLiveOwnedDatabase();

        if (!string.Equals(databaseName, recorded, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Database '{databaseName}' is not the exact database recorded by this lifecycle instance.");
        }

        if (!recorded.StartsWith(options.DatabasePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Recorded database '{recorded}' does not match the configured prefix guard.");
        }

        try
        {
            var profile = RequireBootstrapProfile(requireDatabaseCreation: true);
            // Dispose returns SqlClient connections to their pool. Remove idle sessions for
            // this exact database before DROP, without forcing or adopting active sessions.
            // An active connection still makes DROP fail and preserves retryable ownership.
            executor.ClearPool(BuildConnectionString(profile, recorded));
            await executor.ExecuteAsync(
                BuildConnectionString(profile, "master"),
                $"DROP DATABASE {QuoteIdentifier(recorded)};",
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw new SqlServerE2eDatabaseCleanupException(recorded, ex);
        }

        databaseWasDropped = true;

        if (createdProfileName is { } profileName)
        {
            store.Delete(profileName);
            createdProfileName = null;
        }
    }

    /// <summary>Detects and reports prefix-matching databases that this instance does not own.</summary>
    /// <param name="cancellationToken">A token observed during the read-only query.</param>
    /// <returns>An informational report whose candidates are never deleted automatically.</returns>
    public async Task<SqlServerE2eOrphanReport> DetectOrphansAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = RequireBootstrapProfile(requireDatabaseCreation: false);
        var prefixPattern = EscapeLikePattern(options.DatabasePrefix);
        var names = await executor.QueryNamesAsync(
            BuildConnectionString(profile, "master"),
            "SELECT [name] FROM sys.databases "
                + $"WHERE [name] LIKE N'{EscapeSqlString(prefixPattern)}%' ESCAPE N'~' "
                + "ORDER BY [name];",
            cancellationToken);

        var candidates = names
            .Where(name => name.StartsWith(options.DatabasePrefix, StringComparison.Ordinal))
            .Where(name => !string.Equals(name, CreatedDatabaseName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new SqlServerE2eOrphanReport
        {
            DatabasePrefix = options.DatabasePrefix,
            DatabaseNames = candidates
        };
    }

    private async Task RunDatabaseSqlAsync(string sql, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var databaseName = RequireLiveOwnedDatabase();
        var profile = RequireBootstrapProfile(requireDatabaseCreation: true);
        await executor.ExecuteAsync(
            BuildConnectionString(profile, databaseName),
            sql,
            cancellationToken);
    }

    private SqlServerConnectionProfile RequireBootstrapProfile(bool requireDatabaseCreation)
    {
        if (operationalProfile is not null)
            return operationalProfile;

        var selectedProfile = bootstrapResolution.Status == SqlServerE2eResolutionStatus.Resolved
            ? bootstrapResolution.Profile
            : null;
        if (selectedProfile is null)
            throw new InvalidOperationException("A resolved, explicitly selected E2E bootstrap profile is required.");

        var current = SqlServerE2eConnectionResolver.Resolve(
            store,
            new SqlServerE2eConnectionResolutionOptions
            {
                ConnectionName = selectedProfile.Name,
                RequireDatabaseCreationPermission = requireDatabaseCreation,
                ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseOptional
            });
        if (current.Status != SqlServerE2eResolutionStatus.Resolved || current.Profile is null)
        {
            throw new InvalidOperationException(
                $"Connection '{selectedProfile.Name}' is not currently authorized for this E2E operation. "
                + string.Join(" ", current.Errors));
        }

        return current.Profile;
    }

    private string RequireLiveOwnedDatabase()
    {
        if (CreatedDatabaseName is null)
            throw new InvalidOperationException("This lifecycle instance has not created a database.");
        if (databaseWasDropped)
            throw new InvalidOperationException(
                $"Database '{CreatedDatabaseName}' was already dropped by this lifecycle instance.");
        return CreatedDatabaseName;
    }

    private string BuildConnectionString(SqlServerConnectionProfile profile, string databaseName)
    {
        var builder = profile.BuildConnectionStringBuilder(options.ExternalValues);
        builder.InitialCatalog = databaseName;
        return builder.ConnectionString;
    }

    private static void ValidatePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("A nonblank E2E database prefix is required.", nameof(options));
        if (prefix.Length > MaximumDatabaseNameLength - UniqueSuffixLength)
        {
            throw new ArgumentException(
                $"The E2E database prefix cannot exceed {MaximumDatabaseNameLength - UniqueSuffixLength} characters.",
                nameof(options));
        }
    }

    private static string QuoteIdentifier(string value) =>
        "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string EscapeSqlString(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal);
}
