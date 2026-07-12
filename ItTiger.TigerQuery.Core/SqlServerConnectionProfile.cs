using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Core;

public sealed class SqlServerConnectionProfile
{
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string? Database { get; set; }

    public AuthenticationType Authentication { get; set; }
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public PasswordEncryptionType PasswordEncryption { get; set; } = PasswordEncryptionType.NotApplicable;

    [JsonIgnore]
    public string? PlainPassword { get; set; }

    public EncryptOption Encrypt { get; set; }

    /// <summary>
    /// Whether to trust the server certificate. Null leaves it unset; it is always
    /// excluded under <see cref="EncryptOption.Strict"/>.
    /// </summary>
    public bool? TrustServerCertificate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApplicationIntentOption? ApplicationIntent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ConnectTimeout { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MultiSubnetFailover { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PersistSecurityInfo { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Pooling { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinPoolSize { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxPoolSize { get; set; }

    /// <summary>
    /// Free-form connection-string options supplied through the <c>--opt key=value</c>
    /// escape hatch. Applied through <see cref="SqlConnectionStringBuilder"/> so its
    /// own validation and normalization handle unknown keys and conflicts.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Options { get; set; }

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
