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
