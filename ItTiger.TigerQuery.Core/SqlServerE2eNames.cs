using System.Text;

namespace ItTiger.TigerQuery.Core;

/// <summary>Creates fixed-prefix, sanitized names for session-scoped E2E resources.</summary>
public static class SqlServerE2eNames
{
    /// <summary>The non-overridable database prefix.</summary>
    public const string DatabasePrefix = "_TQ_E2E_";

    /// <summary>The non-overridable saved-connection prefix.</summary>
    public const string ConnectionPrefix = "E2E-";

    /// <summary>The default part used when no name-part option is supplied.</summary>
    public const string DefaultNamePart = "session";

    /// <summary>Sanitizes a user name part using the common database/connection grammar.</summary>
    /// <param name="value">The optional source text.</param>
    /// <returns>Letters, digits, underscore, and hyphen, with other runs replaced by one hyphen.</returns>
    public static string SanitizePart(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? DefaultNamePart : value.Trim();
        var result = new StringBuilder(source.Length);
        var pendingSeparator = false;

        foreach (var character in source)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            {
                if (pendingSeparator && result.Length > 0 && result[^1] is not '-' and not '_')
                    result.Append('-');
                result.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        var sanitized = result.ToString().Trim('-', '_');
        if (sanitized.Length == 0)
            throw new ArgumentException("The name part must contain at least one letter or digit.", nameof(value));
        if (sanitized.Length > 64)
            throw new ArgumentException("The sanitized name part cannot exceed 64 characters.", nameof(value));
        return sanitized;
    }

    /// <summary>Creates a database name with the protected prefix.</summary>
    public static string Database(string? part, string suffix)
    {
        var name = $"{DatabasePrefix}{SanitizePart(part)}_{ValidateSuffix(suffix)}";
        if (name.Length > 128)
            throw new ArgumentException("The generated E2E database name exceeds 128 characters.", nameof(part));
        return name;
    }

    /// <summary>Creates a saved-connection name with the protected prefix.</summary>
    public static string Connection(string? part, string suffix) =>
        $"{ConnectionPrefix}{SanitizePart(part)}-{ValidateSuffix(suffix)}";

    /// <summary>Validates a database as an exact protected E2E drop target.</summary>
    public static void ValidateDroppableDatabase(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        if (databaseName.Length > 128
            || !databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal)
            || databaseName.Length == DatabasePrefix.Length
            || databaseName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new InvalidOperationException(
                $"Database '{databaseName}' does not satisfy the protected E2E database prefix and length guards.");
        }
    }

    private static string ValidateSuffix(string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        if (suffix.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("The E2E unique suffix must be ASCII alphanumeric text.", nameof(suffix));
        return suffix;
    }
}
