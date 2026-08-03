using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Connection-value option surface shared by the <c>add</c>, <c>edit</c>, and
/// <c>add-e2e-bootstrap</c> connection commands.
/// Add treats every value as new input; edit seeds unsupplied values from the
/// existing profile (TigerCli <c>.AsEdit()</c> merge) so only changed options are
/// touched. The escape hatch <c>--opt key=value</c> and the non-promptable
/// first-class options map straight onto <see cref="Microsoft.Data.SqlClient.SqlConnectionStringBuilder"/>.
/// </summary>
internal abstract class SqlServerConnectionInputSettings : TigerCliSettings
{
    // ── Promptable common options ────────────────────────────────────

    // Presence is enforced by the command's domain validation rather than framework
    // requiredness: a shared add/edit settings class must let edit preserve a value it
    // does not restate on the command line (edit-seeded values are not counted as
    // "provided" by the required-option check).
    [TigerCliOption("--server",
        Description = "SQL Server host or instance.",
        DescriptionResourceKey = "Opt_Connection_Server_Description",
        Promptable = TigerCliPromptable.Normal,
        MinLength = 1,
        MaxLength = 1024)]
    public string Server { get; set; } = string.Empty;

    [TigerCliOption("--authentication",
        Description = "Authentication mode.",
        DescriptionResourceKey = "Opt_Connection_Authentication_Description",
        Promptable = TigerCliPromptable.First)]
    public AuthenticationType Authentication { get; set; } = AuthenticationType.Integrated;

    // Username and password hang off --authentication: DependsOnOption orders them after it
    // (so PromptWhen sees the effective auth, including a freshly chosen one) and PromptWhen
    // limits prompting to SQL auth. Presence for SQL auth is enforced by the command's domain
    // validation (see --server) rather than RequiredWhen, so edit can preserve a seeded value
    // it does not restate on the command line (e.g. a non-interactive edit of an existing SQL
    // connection that does not repeat --username).
    [TigerCliOption("--username",
        Description = "SQL login username.",
        DescriptionResourceKey = "Opt_Connection_Username_Description",
        Promptable = TigerCliPromptable.Normal,
        DependsOnOption = "--authentication",
        PromptWhenOption = "--authentication",
        PromptWhenValue = "SqlPassword",
        MinLength = 1,
        MaxLength = 128)]
    public string? Username { get; set; }

    // The password is editable so edit prompts for it whenever the effective authentication is
    // SqlPassword (PromptWhen + DependsOnOption on --authentication). Secret masks the entry and
    // AllowCommandLineValue = false keeps it off argv. Requiredness and MinLength are NOT declared:
    // an edit that keeps the existing password submits an empty entry (Enter), which the mapper
    // treats as "unchanged" and preserves the stored encrypted metadata. Presence for a genuinely
    // new SQL connection is enforced by the command's own validation (which also accepts a stored
    // password).
    [TigerCliOption("--password",
        Description = "SQL login password.",
        DescriptionResourceKey = "Opt_Connection_Password_Description",
        Promptable = TigerCliPromptable.Normal,
        Secret = true,
        AllowCommandLineValue = false,
        DependsOnOption = "--authentication",
        PromptWhenOption = "--authentication",
        PromptWhenValue = "SqlPassword")]
    public string? Password { get; set; }

    [TigerCliOption("--encrypt",
        Description = "Encryption mode.",
        DescriptionResourceKey = "Opt_Connection_Encrypt_Description",
        Promptable = TigerCliPromptable.Normal)]
    public EncryptOption Encrypt { get; set; } = EncryptOption.Mandatory;

    // Nullable so "unset" is distinct from false, and excluded under Encrypt=Strict.
    [TigerCliOption("--trust-server-certificate",
        Description = "Trust the server certificate.",
        DescriptionResourceKey = "Opt_Connection_TrustServerCertificate_Description",
        Promptable = TigerCliPromptable.Normal,
        DependsOnOption = "--encrypt",
        PromptWhenOption = "--encrypt",
        PromptWhenValueNotIn = new[] { "Strict" })]
    public bool? TrustServerCertificate { get; set; }

    [TigerCliOption("--application-intent",
        Description = "Application intent.",
        DescriptionResourceKey = "Opt_Connection_ApplicationIntent_Description",
        Promptable = TigerCliPromptable.Normal)]
    public ApplicationIntentOption? ApplicationIntent { get; set; }

    [TigerCliOption("--database",
        Description = "Initial database.",
        DescriptionResourceKey = "Opt_Connection_Database_Description",
        Provider = "databases",
        Promptable = TigerCliPromptable.Last,
        ValidateAgainstProvider = false)]
    public string? Database { get; set; }

    // External values use the same explicit JSON object shape as the store. These options
    // are non-promptable and reject JSON strings, so secrets cannot be smuggled onto argv
    // under a "reference" option.
    [TigerCliOption("--server-reference",
        Description = "External server reference as JSON.",
        DescriptionResourceKey = "Opt_Connection_ServerReference_Description",
        ValueName = "json",
        Promptable = TigerCliPromptable.No)]
    public string? ServerReference { get; set; }

    [TigerCliOption("--database-reference",
        Description = "External initial-database reference as JSON.",
        DescriptionResourceKey = "Opt_Connection_DatabaseReference_Description",
        ValueName = "json",
        Promptable = TigerCliPromptable.No)]
    public string? DatabaseReference { get; set; }

    [TigerCliOption("--username-reference",
        Description = "External SQL username reference as JSON.",
        DescriptionResourceKey = "Opt_Connection_UsernameReference_Description",
        ValueName = "json",
        Promptable = TigerCliPromptable.No)]
    public string? UsernameReference { get; set; }

    [TigerCliOption("--password-reference",
        Description = "External SQL password reference as JSON.",
        DescriptionResourceKey = "Opt_Connection_PasswordReference_Description",
        ValueName = "json",
        Promptable = TigerCliPromptable.No)]
    public string? PasswordReference { get; set; }

    [TigerCliOption("--connection-string-reference",
        Description = "External full connection-string reference as JSON.",
        DescriptionResourceKey = "Opt_Connection_ConnectionStringReference_Description",
        ValueName = "json",
        Promptable = TigerCliPromptable.No)]
    public string? ConnectionStringReference { get; set; }

    // ── Non-promptable first-class options ───────────────────────────

    [TigerCliOption("--connect-timeout", Description = "Connection timeout in seconds.",
        DescriptionResourceKey = "Opt_Connection_ConnectTimeout_Description")]
    public int? ConnectTimeout { get; set; }

    [TigerCliOption("--multi-subnet-failover", Description = "Enable multi-subnet failover.",
        DescriptionResourceKey = "Opt_Connection_MultiSubnetFailover_Description")]
    public bool? MultiSubnetFailover { get; set; }

    [TigerCliOption("--persist-security-info", Description = "Persist security info.",
        DescriptionResourceKey = "Opt_Connection_PersistSecurityInfo_Description")]
    public bool? PersistSecurityInfo { get; set; }

    [TigerCliOption("--pooling", Description = "Enable connection pooling.",
        DescriptionResourceKey = "Opt_Connection_Pooling_Description")]
    public bool? Pooling { get; set; }

    [TigerCliOption("--min-pool-size", Description = "Minimum pool size (requires pooling).",
        DescriptionResourceKey = "Opt_Connection_MinPoolSize_Description")]
    public int? MinPoolSize { get; set; }

    [TigerCliOption("--max-pool-size", Description = "Maximum pool size (requires pooling).",
        DescriptionResourceKey = "Opt_Connection_MaxPoolSize_Description")]
    public int? MaxPoolSize { get; set; }

    // ── Escape hatch ─────────────────────────────────────────────────

    [TigerCliOption("--opt",
        Description = "Additional connection-string option, e.g. --opt Pooling=true or --opt PacketSize 16000.",
        DescriptionResourceKey = "Opt_Connection_Opt_Description",
        ValueName = "key=value")]
    public List<KeyValuePair<string, string>> Opt { get; set; } = [];

    // ── Application metadata ─────────────────────────────────────────

    [TigerCliOption("--metadata",
        Description = "Set application metadata using key=value. Repeat for multiple entries.",
        DescriptionResourceKey = "Opt_Connection_Metadata_Description",
        ValueName = "key=value",
        Promptable = TigerCliPromptable.No)]
    public List<string> Metadata { get; set; } = [];

    [TigerCliOption("--remove-metadata",
        Description = "Remove an application metadata key. Repeat for multiple keys.",
        DescriptionResourceKey = "Opt_Connection_RemoveMetadata_Description",
        ValueName = "key",
        Promptable = TigerCliPromptable.No)]
    public List<string> RemoveMetadata { get; set; } = [];

    public override TigerCliValidationResult Validate()
    {
        var metadataError = SqlServerConnectionMetadataOptions.ValidateMutations(
            Metadata,
            RemoveMetadata);
        if (metadataError is not null)
            return TigerCliValidationResult.Error(T(metadataError));

        if (Pooling == false && (MinPoolSize.HasValue || MaxPoolSize.HasValue))
        {
            return TigerCliValidationResult.Error(T(
                "--min-pool-size and --max-pool-size cannot be used when pooling is disabled (--pooling false)."));
        }

        foreach (var reference in new[]
                 {
                     ServerReference,
                     DatabaseReference,
                     UsernameReference,
                     PasswordReference,
                     ConnectionStringReference
                 })
        {
            var referenceError = SqlServerExternalValueCliParser.Validate(reference);
            if (referenceError is not null)
                return TigerCliValidationResult.Error(T(referenceError));
        }

        if (ConnectionStringReference is not null)
        {
            if (HasIndividualConnectionInput())
            {
                return TigerCliValidationResult.Error(T(
                    "A full connection string cannot be combined with individual connection fields."));
            }
        }

        if ((PasswordReference is not null || UsernameReference is not null)
            && Authentication != AuthenticationType.SqlPassword)
        {
            return TigerCliValidationResult.Error(T(
                "External username and password references require SQL password authentication."));
        }

        if (Opt.Any(option => IsSensitiveOption(option.Key)))
        {
            return TigerCliValidationResult.Error(T(
                "Sensitive connection-string options cannot be supplied through --opt; use an external reference."));
        }

        return TigerCliValidationResult.Success();
    }

    private bool HasIndividualConnectionInput()
    {
        return !string.IsNullOrWhiteSpace(Server)
            || ServerReference is not null
            || Database is not null
            || DatabaseReference is not null
            || Username is not null
            || UsernameReference is not null
            || !string.IsNullOrEmpty(Password)
            || PasswordReference is not null
            || Authentication != AuthenticationType.Integrated
            || Encrypt != EncryptOption.Mandatory
            || TrustServerCertificate is not null
            || ApplicationIntent is not null
            || ConnectTimeout is not null
            || MultiSubnetFailover is not null
            || PersistSecurityInfo is not null
            || Pooling is not null
            || MinPoolSize is not null
            || MaxPoolSize is not null
            || Opt.Count > 0;
    }

    private static bool IsSensitiveOption(string key) =>
        key.Equals("Password", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Pwd", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Access Token", StringComparison.OrdinalIgnoreCase)
        || key.Equals("AccessToken", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Connection settings with the regular positional profile name.</summary>
internal class SqlServerConnectionSettings : SqlServerConnectionInputSettings
{
    [TigerCliArgument(0, Name = "name", Description = "Connection name.",
        DescriptionResourceKey = "Arg_Connection_Name_Description",
        MinLength = 1, MaxLength = 40,
        EditProvider = "connections")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Add-only settings for explicit E2E authorization.</summary>
internal sealed class AddSqlServerConnectionSettings : SqlServerConnectionSettings
{
    [TigerCliOption("--e2e",
        Description = "Authorize this profile for TigerQuery E2E use.",
        DescriptionResourceKey = "Opt_Connection_E2e_Description",
        Promptable = TigerCliPromptable.No)]
    public bool E2e { get; set; }

    [TigerCliOption("--allow-database-create",
        Description = "Authorize E2E database creation through this profile.",
        DescriptionResourceKey = "Opt_Connection_AllowDatabaseCreate_Description",
        Promptable = TigerCliPromptable.No)]
    public bool AllowDatabaseCreation { get; set; }

    public override TigerCliValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.IsValid)
            return result;

        return AllowDatabaseCreation && !E2e
            ? TigerCliValidationResult.Error(T(
                "--allow-database-create requires --e2e."))
            : TigerCliValidationResult.Success();
    }
}

/// <summary>Settings for the dedicated E2E bootstrap creation command.</summary>
internal sealed class AddE2eBootstrapSqlServerConnectionSettings
    : SqlServerConnectionInputSettings
{
    [TigerCliOption("--name",
        Description = "Bootstrap connection name; overrides the host default.",
        DescriptionResourceKey = "Opt_Connection_BootstrapName_Description",
        Promptable = TigerCliPromptable.No,
        MinLength = 1,
        MaxLength = 40)]
    public string? Name { get; set; }

    [TigerCliOption("--allow-database-create",
        Description = "Authorize E2E database creation through this profile.",
        DescriptionResourceKey = "Opt_Connection_AllowDatabaseCreate_Description",
        Promptable = TigerCliPromptable.No)]
    public bool AllowDatabaseCreation { get; set; }
}
