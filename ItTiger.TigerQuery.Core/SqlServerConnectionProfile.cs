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

    /// <summary>Initializes an empty mutable connection profile.</summary>
    public SqlServerConnectionProfile()
    {
        readOnlyMetadata = new ReadOnlyDictionary<string, string>(metadata);
    }

    /// <summary>Gets or sets the store-unique profile name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the SQL Server host, instance, or endpoint.</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional initial database.
    /// </summary>
    /// <remarks>Null, empty, or whitespace values produce a server-level connection.</remarks>
    public string? Database { get; set; }

    /// <summary>Gets or sets the authentication mechanism.</summary>
    public AuthenticationType Authentication { get; set; }

    /// <summary>Gets or sets the SQL login name used by SQL-password authentication.</summary>
    public string? Username { get; set; }

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
    /// Keys and values are not trimmed or normalized. Call
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

        metadata[key] = value;
    }

    /// <summary>Removes one application-owned metadata value, if present.</summary>
    /// <param name="key">The non-empty, case-sensitive key.</param>
    /// <returns><see langword="true"/> when a value was removed.</returns>
    /// <remarks>Call <see cref="SqlServerConnectionStore.AddOrUpdate"/> to persist the change.</remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is null or empty. The parameter name is <c>key</c>.
    /// </exception>
    public bool RemoveMetadata(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return metadata.Remove(key);
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
    /// <exception cref="ArgumentException">
    /// A free-form option name or value is rejected by
    /// <see cref="SqlConnectionStringBuilder"/>.
    /// </exception>
    public SqlConnectionStringBuilder BuildConnectionStringBuilder()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server
        };

        if (!string.IsNullOrWhiteSpace(Database))
            builder.InitialCatalog = Database;

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
            builder.UserID = Username ?? string.Empty;
            builder.Password = PlainPassword ?? string.Empty;
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

    /// <summary>Builds the SqlClient connection string represented by this profile.</summary>
    /// <returns>The normalized connection string.</returns>
    /// <exception cref="ArgumentException">
    /// A free-form option name or value is rejected by
    /// <see cref="SqlConnectionStringBuilder"/>.
    /// </exception>
    public string BuildConnectionString() => BuildConnectionStringBuilder().ConnectionString;

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
