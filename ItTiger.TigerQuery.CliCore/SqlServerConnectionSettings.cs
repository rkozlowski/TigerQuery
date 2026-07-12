using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Option surface shared by the <c>add</c> and <c>edit</c> connection commands.
/// Add treats every value as new input; edit seeds unsupplied values from the
/// existing profile (TigerCli <c>.AsEdit()</c> merge) so only changed options are
/// touched. The escape hatch <c>--opt key=value</c> and the non-promptable
/// first-class options map straight onto <see cref="Microsoft.Data.SqlClient.SqlConnectionStringBuilder"/>.
/// </summary>
internal sealed class SqlServerConnectionSettings : TigerCliSettings
{
    [TigerCliArgument(0, Name = "name", Description = "Connection name.",
        DescriptionResourceKey = "Arg_Connection_Name_Description",
        MinLength = 1, MaxLength = 40,
        EditProvider = "connections")]
    public string Name { get; set; } = string.Empty;

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

    public override TigerCliValidationResult Validate()
    {
        if (Pooling == false && (MinPoolSize.HasValue || MaxPoolSize.HasValue))
        {
            return TigerCliValidationResult.Error(T(
                "--min-pool-size and --max-pool-size cannot be used when pooling is disabled (--pooling false)."));
        }

        return TigerCliValidationResult.Success();
    }
}
