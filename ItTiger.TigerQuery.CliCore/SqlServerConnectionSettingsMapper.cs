using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Maps between the shared command settings and the persisted connection profile.
/// </summary>
internal static class SqlServerConnectionSettingsMapper
{
    /// <summary>
    /// Builds the profile to save. When <paramref name="existing"/> is provided (edit),
    /// the encrypted password and its metadata are preserved unless a new password was
    /// explicitly supplied.
    /// </summary>
    public static SqlServerConnectionProfile ToProfile(
        SqlServerConnectionSettings settings,
        SqlServerConnectionProfile? existing)
    {
        var isSqlPassword = settings.Authentication == AuthenticationType.SqlPassword;

        var profile = new SqlServerConnectionProfile
        {
            Name = settings.Name,
            Server = settings.Server,
            Database = settings.Database,
            Authentication = settings.Authentication,
            Username = isSqlPassword ? settings.Username : null,
            Encrypt = settings.Encrypt,
            TrustServerCertificate = settings.Encrypt == EncryptOption.Strict
                ? null
                : settings.TrustServerCertificate,
            ApplicationIntent = settings.ApplicationIntent,
            ConnectTimeout = settings.ConnectTimeout,
            MultiSubnetFailover = settings.MultiSubnetFailover,
            PersistSecurityInfo = settings.PersistSecurityInfo,
            Pooling = settings.Pooling,
            MinPoolSize = settings.MinPoolSize,
            MaxPoolSize = settings.MaxPoolSize,
            Options = ToOptions(settings.Opt)
        };

        if (isSqlPassword)
            ApplySqlPassword(profile, settings, existing);
        // When authentication is not SqlPassword (e.g. switched to Integrated), Username is already
        // null and the SQL credential metadata above is left at its defaults (PlainPassword null,
        // EncryptedPassword null, PasswordEncryption NotApplicable), clearing the stored credentials.

        return profile;
    }

    /// <summary>
    /// Resolves the password for a SQL-authentication profile. The password is prompt-only (never
    /// command-line) and the edit loader seeds <see cref="SqlServerConnectionSettings.Password"/>
    /// with the decrypted secret when it is available, so a non-empty value covers both a freshly
    /// typed password and an unchanged seeded one (Enter) — either way it is re-protected on save.
    /// An empty value can only occur when no decrypted password was available (Integrated, or a
    /// secret that could not be decrypted); it must never blank a stored password. Validation (not
    /// this mapper) rejects the remaining case where no password exists at all.
    /// </summary>
    private static void ApplySqlPassword(
        SqlServerConnectionProfile profile,
        SqlServerConnectionSettings settings,
        SqlServerConnectionProfile? existing)
    {
        if (!string.IsNullOrEmpty(settings.Password))
        {
            // A usable password is present (typed, or the seeded plaintext kept via Enter): use it.
            // Leaving EncryptedPassword unset lets the protector (re-)encrypt PlainPassword on save.
            profile.PlainPassword = settings.Password;
            return;
        }

        // Empty/null password in edit: keep the existing secret rather than blanking it.
        if (existing is null)
            return;

        if (!string.IsNullOrEmpty(existing.EncryptedPassword))
        {
            // The decrypted password was unavailable but stored metadata exists: preserve the blob
            // verbatim. PlainPassword is left null so the protector skips re-encryption and the
            // exact encrypted metadata passes through unchanged.
            profile.EncryptedPassword = existing.EncryptedPassword;
            profile.PasswordEncryption = existing.PasswordEncryption;
        }
        else if (!string.IsNullOrEmpty(existing.PlainPassword))
        {
            // Only a decrypted in-memory secret exists (no persisted blob): carry it forward so
            // the protector re-protects it on save.
            profile.PlainPassword = existing.PlainPassword;
        }
        // else: no existing password to preserve (e.g. Integrated -> SqlPassword). The profile is
        // left without a password and validation reports it as required.
    }

    /// <summary>
    /// Seeds an edit's settings from an existing profile. The decrypted password (when the store
    /// was able to load/decrypt it) is surfaced into <see cref="SqlServerConnectionSettings.Password"/>
    /// so the secret prompt shows the masked existing value and Enter keeps it, and so the
    /// database-selection provider can connect with the effective password before the user retypes.
    /// When the password could not be decrypted, <see cref="SqlServerConnectionProfile.PlainPassword"/>
    /// is null and <see cref="ToProfile"/> preserves the stored encrypted metadata instead.
    /// </summary>
    public static SqlServerConnectionSettings FromProfile(SqlServerConnectionProfile profile)
    {
        return new SqlServerConnectionSettings
        {
            Name = profile.Name,
            Server = profile.Server,
            Database = profile.Database,
            Authentication = profile.Authentication,
            Username = profile.Username,
            Password = profile.PlainPassword,
            Encrypt = profile.Encrypt,
            TrustServerCertificate = profile.TrustServerCertificate,
            ApplicationIntent = profile.ApplicationIntent,
            ConnectTimeout = profile.ConnectTimeout,
            MultiSubnetFailover = profile.MultiSubnetFailover,
            PersistSecurityInfo = profile.PersistSecurityInfo,
            Pooling = profile.Pooling,
            MinPoolSize = profile.MinPoolSize,
            MaxPoolSize = profile.MaxPoolSize,
            Opt = profile.Options is null
                ? []
                : profile.Options.Select(o => new KeyValuePair<string, string>(o.Key, o.Value)).ToList()
        };
    }

    /// <summary>
    /// Builds a probe profile (no target database) used to enumerate databases for the
    /// selection provider from the connection/security options gathered so far.
    /// </summary>
    public static SqlServerConnectionProfile ToProbeProfile(SqlServerConnectionSettings settings)
    {
        var profile = ToProfile(settings, existing: null);
        profile.Database = null;
        return profile;
    }

    private static Dictionary<string, string>? ToOptions(List<KeyValuePair<string, string>> opt)
    {
        if (opt is null || opt.Count == 0)
            return null;

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in opt)
        {
            if (!string.IsNullOrWhiteSpace(key))
                options[key.Trim()] = value;
        }

        return options.Count == 0 ? null : options;
    }
}
