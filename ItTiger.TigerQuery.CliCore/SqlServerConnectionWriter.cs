using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Shared validation used by the add and edit commands. The rules themselves live in
/// <see cref="SqlServerConnectionValidator.ValidateComplete"/> so that stored,
/// edited, and copied profiles are held to one standard; connection-string concerns
/// (key/value validity, pool sizes, etc.) are delegated to
/// <see cref="Microsoft.Data.SqlClient.SqlConnectionStringBuilder"/> rather than
/// reimplemented there.
/// </summary>
internal static class SqlServerConnectionWriter
{
    public const string PasswordNotPersisted =
        "The connection was not saved: this connection store cannot keep a password on this "
        + "system. Saved passwords are protected with Windows DPAPI, which other platforms do "
        + "not provide. Use --password-reference to read the password from an environment "
        + "variable or a file.";

    /// <summary>
    /// Refuses a typed password that the run's store would discard on save, so a connection
    /// that cannot log in is never reported as saved.
    /// </summary>
    /// <returns><see langword="true"/> when the error was reported and nothing may be saved.</returns>
    public static bool TryReportUnpersistedPassword(
        TigerCliSettings settings,
        SqlServerConnectionProfile profile,
        SqlServerConnectionStore store)
    {
        if (string.IsNullOrEmpty(profile.PlainPassword) || store.PersistsPasswords())
            return false;

        return TryReportErrors(settings, [PasswordNotPersisted]);
    }

    public static IReadOnlyList<string> Validate(
        SqlServerConnectionProfile profile,
        SqlServerConnectionValidationPolicy policy) =>
        SqlServerConnectionValidator.ValidateComplete(profile, policy);

    public static bool TryReportErrors(
        TigerCliSettings settings,
        IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
            return false;

        // CliCore-owned messages resolve to the active culture via the source-text lookup;
        // pass-through messages (validator, SqlConnectionStringBuilder) miss the lookup and
        // fall back to themselves. Escaped: error text is data, not TigerCli markup.
        foreach (var error in errors)
            TigerConsole.MarkupErrorLine(CliMarkupParser.Escape(settings.T(error)));

        return true;
    }
}
