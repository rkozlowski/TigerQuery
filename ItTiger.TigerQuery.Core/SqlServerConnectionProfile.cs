using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Represents a mutable named SQL Server connection profile that can be persisted
/// by <see cref="SqlServerConnectionStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Loading or finding a profile returns a detached mutable object. Changes are
/// persisted only after calling <see cref="SqlServerConnectionStore.AddOrUpdate"/>
/// or <see cref="SqlServerConnectionStore.Save"/>.
/// </para>
/// <para>
/// Application metadata is independent of the generated connection string and
/// must not contain secrets.
/// </para>
/// </remarks>
public sealed class SqlServerConnectionProfile
{
    private readonly Dictionary<string, string> metadata = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, string> readOnlyMetadata;
    private SqlServerConnectionValue serverValue = SqlServerConnectionValue.Literal(string.Empty);

    /// <summary>Initializes an empty mutable connection profile.</summary>
    public SqlServerConnectionProfile()
    {
        readOnlyMetadata = new ReadOnlyDictionary<string, string>(metadata);
    }

    /// <summary>Gets or sets the store-unique profile name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the literal SQL Server host, instance, or endpoint.</summary>
    /// <remarks>
    /// Assigning a literal replaces any <see cref="ServerValue"/> reference. When the
    /// persisted value is external this compatibility property returns an empty string;
    /// use <see cref="ServerValue"/> to inspect the reference.
    /// </remarks>
    [JsonIgnore]
    public string Server
    {
        get => ServerValue.LiteralValue ?? string.Empty;
        set => ServerValue = SqlServerConnectionValue.Literal(value ?? string.Empty);
    }

    /// <summary>Gets or sets the literal or external server value.</summary>
    [JsonPropertyName(nameof(Server))]
    public SqlServerConnectionValue ServerValue
    {
        get => serverValue;
        set => serverValue = value ?? SqlServerConnectionValue.Literal(string.Empty);
    }

    /// <summary>
    /// Gets or sets the optional initial database.
    /// </summary>
    /// <remarks>Null, empty, or whitespace values produce a server-level connection.</remarks>
    [JsonIgnore]
    public string? Database
    {
        get => DatabaseValue?.LiteralValue;
        set => DatabaseValue = value is null ? null : SqlServerConnectionValue.Literal(value);
    }

    /// <summary>Gets or sets the optional literal or external initial catalog.</summary>
    [JsonPropertyName(nameof(Database))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SqlServerConnectionValue? DatabaseValue { get; set; }

    /// <summary>Gets or sets the authentication mechanism.</summary>
    public AuthenticationType Authentication { get; set; }

    /// <summary>Gets or sets the literal SQL login name used by SQL-password authentication.</summary>
    [JsonIgnore]
    public string? Username
    {
        get => UsernameValue?.LiteralValue;
        set => UsernameValue = value is null ? null : SqlServerConnectionValue.Literal(value);
    }

    /// <summary>Gets or sets the optional literal or external SQL login name.</summary>
    [JsonPropertyName(nameof(Username))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SqlServerConnectionValue? UsernameValue { get; set; }

    /// <summary>Gets or sets the persisted protected-password value.</summary>
    /// <remarks>
    /// Its interpretation is identified by <see cref="PasswordEncryption"/>.
    /// Applications normally let an <see cref="IConnectionPasswordProtector"/> manage it.
    /// </remarks>
    public string? EncryptedPassword { get; set; }

    /// <summary>Gets or sets how <see cref="EncryptedPassword"/> is protected.</summary>
    public PasswordEncryptionType PasswordEncryption { get; set; } = PasswordEncryptionType.NotApplicable;

    /// <summary>Gets or sets the in-memory plain-text password.</summary>
    /// <remarks>
    /// This property is excluded from JSON. The active password protector may set
    /// or clear it during store load/save operations.
    /// </remarks>
    [JsonIgnore]
    public string? PlainPassword { get; set; }

    /// <summary>
    /// Gets or sets an explicitly persisted literal or external password value.
    /// </summary>
    /// <remarks>
    /// CLI commands accept only the external-reference form. Existing prompted-password
    /// behavior continues to use <see cref="PlainPassword"/> and
    /// <see cref="EncryptedPassword"/> so old stores and password protectors remain
    /// compatible.
    /// </remarks>
    [JsonPropertyName("Password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SqlServerConnectionValue? PasswordValue { get; set; }

    /// <summary>
    /// Gets or sets the complete connection string used instead of all field-based
    /// settings.
    /// </summary>
    [JsonPropertyName("ConnectionString")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SqlServerConnectionValue? ConnectionStringValue { get; set; }

    // Used only by dedicated E2E cloning so an unresolved complete connection-string
    // reference can be retargeted without resolving or rewriting the reference.
    [JsonInclude]
    [JsonPropertyName("InitialCatalogOverride")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? InitialCatalogOverride { get; set; }

    /// <summary>Gets or sets the transport encryption policy.</summary>
    public EncryptOption Encrypt { get; set; }

    /// <summary>
    /// Gets or sets whether to trust the server certificate. Null leaves it unset; it is always
    /// excluded under <see cref="EncryptOption.Strict"/>.
    /// </summary>
    public bool? TrustServerCertificate { get; set; }

    /// <summary>Gets or sets the optional read-write or read-only application intent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApplicationIntentOption? ApplicationIntent { get; set; }

    /// <summary>Gets or sets the optional connection timeout in seconds.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ConnectTimeout { get; set; }

    /// <summary>Gets or sets whether multi-subnet failover is enabled.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MultiSubnetFailover { get; set; }

    /// <summary>Gets or sets whether security-sensitive information remains available after opening.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PersistSecurityInfo { get; set; }

    /// <summary>Gets or sets whether SqlClient connection pooling is enabled.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Pooling { get; set; }

    /// <summary>Gets or sets the optional minimum connection-pool size.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinPoolSize { get; set; }

    /// <summary>Gets or sets the optional maximum connection-pool size.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxPoolSize { get; set; }

    /// <summary>
    /// Gets or sets free-form connection-string options supplied through the <c>--opt key=value</c>
    /// escape hatch. Applied through <see cref="SqlConnectionStringBuilder"/> so its
    /// own validation and normalization handle unknown keys and conflicts.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Options { get; set; }

    /// <summary>
    /// Gets opaque, application-owned string metadata. Keys are compared ordinally and are
    /// not normalized or interpreted by the shared connection library. Do not store
    /// secrets in metadata.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Metadata => readOnlyMetadata;

    /// <summary>Adds or replaces one application-owned metadata value.</summary>
    /// <param name="key">The non-empty, case-sensitive key.</param>
    /// <param name="value">The opaque string value; an empty value is permitted.</param>
    /// <remarks>
    /// Keys and values are not trimmed or normalized. Keys in TigerQuery's reserved
    /// <c>ittiger.e2e.*</c> namespace are rejected; only TigerQuery-owned operations may
    /// write them. Call
    /// <see cref="SqlServerConnectionStore.AddOrUpdate"/> to persist the change.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is null or empty. The parameter name is <c>key</c>.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="value"/> is <see langword="null"/>. The parameter name is <c>value</c>.
    /// </exception>
    public void SetMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        ThrowIfReservedMetadataKey(key);

        metadata[key] = value;
    }

    /// <summary>Removes one application-owned metadata value, if present.</summary>
    /// <param name="key">The non-empty, case-sensitive key.</param>
    /// <returns><see langword="true"/> when a value was removed.</returns>
    /// <remarks>
    /// Keys in TigerQuery's reserved <c>ittiger.e2e.*</c> namespace are rejected; only
    /// TigerQuery-owned operations may remove them. Call
    /// <see cref="SqlServerConnectionStore.AddOrUpdate"/> to persist the change.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is null or empty. The parameter name is <c>key</c>.
    /// </exception>
    public bool RemoveMetadata(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ThrowIfReservedMetadataKey(key);
        return metadata.Remove(key);
    }

    /// <summary>Writes metadata on behalf of a TigerQuery-owned operation.</summary>
    internal void SetReservedMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!SqlServerE2eMetadata.IsReservedKey(key))
            throw new ArgumentException("A TigerQuery-owned metadata key is required.", nameof(key));

        metadata[key] = value;
    }

    internal void ClearReservedMetadata()
    {
        foreach (var key in metadata.Keys.Where(SqlServerE2eMetadata.IsReservedKey).ToList())
            metadata.Remove(key);
    }

    private static void ThrowIfReservedMetadataKey(string key)
    {
        if (SqlServerE2eMetadata.IsReservedKey(key))
        {
            throw new ArgumentException(
                $"Metadata keys beginning with '{SqlServerE2eMetadata.ReservedKeyPrefix}' are reserved for TigerQuery.",
                nameof(key));
        }
    }

    // The serialization proxy keeps the public view read-only, omits empty metadata,
    // and writes keys in ordinal order for stable, reviewable store files.
    [JsonInclude]
    [JsonPropertyName(nameof(Metadata))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private Dictionary<string, string>? SerializedMetadata
    {
        get
        {
            if (metadata.Count == 0)
                return null;

            var sorted = new Dictionary<string, string>(metadata.Count, StringComparer.Ordinal);
            foreach (var (key, value) in metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
                sorted.Add(key, value);

            return sorted;
        }
        set
        {
            metadata.Clear();
            if (value is null)
                return;

            foreach (var (key, metadataValue) in value)
            {
                if (key.Length == 0 || metadataValue is null)
                    throw new System.Text.Json.JsonException(
                        "Metadata keys must be non-empty and metadata values must be strings.");

                metadata.Add(key, metadataValue);
            }
        }
    }

    /// <summary>
    /// Produces an independent copy of everything this profile persists, including
    /// the protected password representation and every metadata entry.
    /// </summary>
    /// <remarks>
    /// The clone is produced through the profile's own JSON contract rather than a
    /// hand-written property list, so a field added to the profile in a later
    /// release is carried automatically. <see cref="PlainPassword"/> is excluded
    /// from that contract and is therefore never part of a clone; the copy carries
    /// only the at-rest representation.
    /// </remarks>
    /// <returns>A detached profile with no shared mutable state.</returns>
    internal SqlServerConnectionProfile ClonePersisted()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<SqlServerConnectionProfile>(json)
            ?? throw new InvalidOperationException(
                "A connection profile could not be cloned through its own JSON contract.");
    }

    /// <summary>Builds a SqlClient connection-string builder from the current profile.</summary>
    /// <returns>A new builder that the caller may modify independently.</returns>
    /// <remarks>
    /// <para>
    /// A null or whitespace database is omitted. Strict encryption omits
    /// <see cref="TrustServerCertificate"/>. Entries in <see cref="Options"/> are
    /// applied last and may override first-class properties.
    /// </para>
    /// <para>
    /// SQL-password authentication uses <see cref="PlainPassword"/>, not the
    /// persisted <see cref="EncryptedPassword"/>.
    /// </para>
    /// </remarks>
    /// <param name="externalValues">
    /// Optional readers used only for external references; null uses the process
    /// environment and UTF-8 file reads.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A free-form option name or value is rejected by
    /// <see cref="SqlConnectionStringBuilder"/>.
    /// </exception>
    public SqlConnectionStringBuilder BuildConnectionStringBuilder(
        SqlServerExternalValueResolutionOptions? externalValues = null)
    {
        if (ConnectionStringValue is not null)
        {
            if (HasIndividualConnectionSettings())
            {
                throw new InvalidOperationException(
                    "A full connection string cannot be combined with individual connection fields.");
            }

            var connectionString = SqlServerExternalValueResolver.Resolve(
                ConnectionStringValue,
                "connection string",
                allowEmpty: false,
                externalValues);
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                if (InitialCatalogOverride is not null)
                    builder.InitialCatalog = InitialCatalogOverride;
                return builder;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                throw new SqlServerExternalValueException(
                    "The configured full connection string is not valid.");
            }
        }

        if (Authentication != AuthenticationType.SqlPassword
            && (UsernameValue?.IsReference == true || PasswordValue?.IsReference == true))
        {
            throw new InvalidOperationException(
                "External username and password references require SQL password authentication.");
        }

        if (PasswordValue is not null
            && (!string.IsNullOrEmpty(PlainPassword) || !string.IsNullOrEmpty(EncryptedPassword)))
        {
            throw new InvalidOperationException(
                "A password value cannot be combined with a protected or in-memory password.");
        }

        var server = SqlServerExternalValueResolver.Resolve(
            ServerValue,
            "server",
            allowEmpty: false,
            externalValues);
        var database = DatabaseValue is null
            ? null
            : SqlServerExternalValueResolver.Resolve(
                DatabaseValue,
                "database",
                allowEmpty: true,
                externalValues);
        var username = Authentication != AuthenticationType.SqlPassword || UsernameValue is null
            ? null
            : SqlServerExternalValueResolver.Resolve(
                UsernameValue,
                "username",
                allowEmpty: false,
                externalValues);
        var password = Authentication != AuthenticationType.SqlPassword
            ? null
            : PasswordValue is null
                ? PlainPassword
                : SqlServerExternalValueResolver.Resolve(
                    PasswordValue,
                    "password",
                    allowEmpty: false,
                    externalValues);

        var hasExternalValues = ServerValue.IsReference
            || DatabaseValue?.IsReference == true
            || UsernameValue?.IsReference == true
            || PasswordValue?.IsReference == true;

        try
        {
            return BuildFieldConnectionStringBuilder(server, database, username, password);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            if (hasExternalValues || GetSensitiveLiteralValues().Count > 0)
            {
                throw new SqlServerExternalValueException(
                    "The configured connection fields are not valid.");
            }

            throw;
        }
    }

    /// <summary>Builds the SqlClient connection string represented by this profile.</summary>
    /// <param name="externalValues">
    /// Optional readers used only for external references; null uses the process
    /// environment and UTF-8 file reads.
    /// </param>
    /// <returns>The normalized connection string.</returns>
    /// <exception cref="ArgumentException">
    /// A free-form option name or value is rejected by
    /// <see cref="SqlConnectionStringBuilder"/>.
    /// </exception>
    public string BuildConnectionString(
        SqlServerExternalValueResolutionOptions? externalValues = null) =>
        BuildConnectionStringBuilder(externalValues).ConnectionString;

    internal void ValidateConnectionStringCompatibility()
    {
        if (ConnectionStringValue is not null)
        {
            if (!ConnectionStringValue.IsReference && !HasIndividualConnectionSettings())
                _ = BuildConnectionStringBuilder();
            return;
        }

        if (PasswordValue is not null
            && (!string.IsNullOrEmpty(PlainPassword) || !string.IsNullOrEmpty(EncryptedPassword)))
        {
            return;
        }

        _ = BuildFieldConnectionStringBuilder(
            ServerValue.IsReference ? "validation-server" : ServerValue.LiteralValue ?? string.Empty,
            DatabaseValue?.IsReference == true ? "validation-database" : DatabaseValue?.LiteralValue,
            UsernameValue?.IsReference == true ? "validation-user" : UsernameValue?.LiteralValue,
            PasswordValue?.IsReference == true ? "validation-password" : PasswordValue?.LiteralValue ?? PlainPassword);
    }

    /// <summary>Gets whether this profile uses complete connection-string mode.</summary>
    [JsonIgnore]
    public bool UsesFullConnectionString => ConnectionStringValue is not null;

    /// <summary>Returns the safe server display without resolving a reference.</summary>
    public string DescribeServer() => ServerValue.Describe();

    /// <summary>Returns the safe database display without resolving a reference.</summary>
    public string? DescribeDatabase() => DatabaseValue?.Describe();

    /// <summary>Returns the safe username display without resolving a reference.</summary>
    public string? DescribeUsername() => UsernameValue?.Describe();

    /// <summary>Returns a safe full-connection-string description.</summary>
    public string? DescribeConnectionString() => ConnectionStringValue?.Describe(sensitive: true);

    internal bool HasIndividualConnectionSettings()
    {
        return IsConfigured(ServerValue)
            || DatabaseValue is not null
            || UsernameValue is not null
            || PasswordValue is not null
            || !string.IsNullOrEmpty(PlainPassword)
            || !string.IsNullOrEmpty(EncryptedPassword)
            || PasswordEncryption != PasswordEncryptionType.NotApplicable
            || Authentication != AuthenticationType.Integrated
            || Encrypt != EncryptOption.Optional
            || TrustServerCertificate is not null
            || ApplicationIntent is not null
            || ConnectTimeout is not null
            || MultiSubnetFailover is not null
            || PersistSecurityInfo is not null
            || Pooling is not null
            || MinPoolSize is not null
            || MaxPoolSize is not null
            || Options is not null;
    }

    internal string RedactSensitiveValues(string message)
    {
        var redacted = message;
        foreach (var secret in GetSensitiveLiteralValues())
            redacted = redacted.Replace(secret, "<redacted>", StringComparison.Ordinal);
        return redacted;
    }

    private IReadOnlyList<string> GetSensitiveLiteralValues()
    {
        var values = new List<string>();
        AddIfNonempty(values, PlainPassword);
        AddIfNonempty(values, EncryptedPassword);
        AddIfNonempty(values, PasswordValue?.LiteralValue);
        AddIfNonempty(values, ConnectionStringValue?.LiteralValue);

        if (Options is not null)
        {
            foreach (var (key, value) in Options)
            {
                if (IsSensitiveConnectionStringOption(key))
                    AddIfNonempty(values, value);
            }
        }

        return values;
    }

    private static bool IsConfigured(SqlServerConnectionValue value) =>
        value.IsReference || !string.IsNullOrWhiteSpace(value.LiteralValue);

    private static void AddIfNonempty(List<string> values, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            values.Add(value);
    }

    /// <summary>Determines whether a connection-string option value must be redacted.</summary>
    public static bool IsSensitiveConnectionStringOption(string key) =>
        key.Equals("Password", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Pwd", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Access Token", StringComparison.OrdinalIgnoreCase)
        || key.Equals("AccessToken", StringComparison.OrdinalIgnoreCase);

    private SqlConnectionStringBuilder BuildFieldConnectionStringBuilder(
        string server,
        string? database,
        string? username,
        string? password)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server
        };

        if (!string.IsNullOrWhiteSpace(database))
            builder.InitialCatalog = database;

        builder.Encrypt = Encrypt switch
        {
            EncryptOption.Optional => SqlConnectionEncryptOption.Optional,
            EncryptOption.Mandatory => SqlConnectionEncryptOption.Mandatory,
            EncryptOption.Strict => SqlConnectionEncryptOption.Strict,
            _ => builder.Encrypt
        };

        // TrustServerCertificate is meaningless under Strict TLS, so it is excluded there.
        if (Encrypt != EncryptOption.Strict && TrustServerCertificate is bool trust)
            builder.TrustServerCertificate = trust;

        if (Authentication == AuthenticationType.Integrated)
        {
            builder.IntegratedSecurity = true;
        }
        else if (Authentication == AuthenticationType.SqlPassword)
        {
            builder.UserID = username ?? string.Empty;
            builder.Password = password ?? string.Empty;
        }

        if (ApplicationIntent is { } intent)
        {
            builder.ApplicationIntent = intent == ApplicationIntentOption.ReadOnly
                ? Microsoft.Data.SqlClient.ApplicationIntent.ReadOnly
                : Microsoft.Data.SqlClient.ApplicationIntent.ReadWrite;
        }

        if (ConnectTimeout is { } connectTimeout)
            builder.ConnectTimeout = connectTimeout;

        if (MultiSubnetFailover is { } multiSubnetFailover)
            builder.MultiSubnetFailover = multiSubnetFailover;

        if (PersistSecurityInfo is { } persistSecurityInfo)
            builder.PersistSecurityInfo = persistSecurityInfo;

        if (Pooling is { } pooling)
            builder.Pooling = pooling;

        if (MinPoolSize is { } minPoolSize)
            builder.MinPoolSize = minPoolSize;

        if (MaxPoolSize is { } maxPoolSize)
            builder.MaxPoolSize = maxPoolSize;

        // Applied last so SqlConnectionStringBuilder validates keys/values and cleanly
        // overrides any first-class option the caller chose to restate via --opt.
        if (Options is not null)
        {
            foreach (var (key, value) in Options)
                ApplyOption(builder, key, value);
        }

        return builder;
    }

    // Lets the escape hatch accept the property-style key (e.g. "PacketSize") in addition
    // to SqlClient's canonical spaced keyword ("Packet Size"), whose synonym coverage is
    // inconsistent. This is a generic transform, not a per-option lookup table.
    private static void ApplyOption(SqlConnectionStringBuilder builder, string key, string value)
    {
        var effectiveKey = key;
        if (!builder.ContainsKey(key))
        {
            var spaced = System.Text.RegularExpressions.Regex.Replace(key, "(?<=[a-z0-9])(?=[A-Z])", " ");
            if (!string.Equals(spaced, key, StringComparison.Ordinal) && builder.ContainsKey(spaced))
                effectiveKey = spaced;
        }

        builder[effectiveKey] = value;
    }
}
