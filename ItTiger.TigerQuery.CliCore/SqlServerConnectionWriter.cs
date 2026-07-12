using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Markup;
using ItTiger.TigerCli.Terminal;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Shared validation used by the add and edit commands. Connection-string concerns
/// (key/value validity, pool sizes, etc.) are delegated to
/// <see cref="Microsoft.Data.SqlClient.SqlConnectionStringBuilder"/> rather than
/// reimplemented here.
/// </summary>
internal static class SqlServerConnectionWriter
{
    public static IReadOnlyList<string> Validate(
        SqlServerConnectionProfile profile,
        SqlServerConnectionValidationPolicy policy)
    {
        var errors = SqlServerConnectionValidator.Validate(profile, policy).ToList();

        if (profile.Authentication == AuthenticationType.SqlPassword)
        {
            if (string.IsNullOrWhiteSpace(profile.Username))
                errors.Add("Username is required for SQL password authentication.");

            if (string.IsNullOrEmpty(profile.PlainPassword) &&
                string.IsNullOrEmpty(profile.EncryptedPassword))
            {
                errors.Add("Password is required for SQL password authentication.");
            }
        }

        // Let SqlConnectionStringBuilder validate the option surface (unknown --opt keys,
        // out-of-range pool sizes, malformed values, ...).
        try
        {
            _ = profile.BuildConnectionString();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            errors.Add(ex.Message);
        }

        return errors;
    }

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
