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

    /// <summary>The filegroup suffix for a database provisioned for memory-optimized tables.</summary>
    public const string MemoryOptimizedFilegroupSuffix = "_MOD_FG";

    /// <summary>The logical-file suffix for the memory-optimized data container.</summary>
    public const string MemoryOptimizedContainerSuffix = "_MOD_FILE";

    /// <summary>The directory-name suffix for the memory-optimized data container.</summary>
    public const string MemoryOptimizedContainerDirectorySuffix = "_MOD_DIR";

    /// <summary>Names the memory-optimized filegroup belonging to one exact E2E database.</summary>
    /// <param name="databaseName">The exact owned database the filegroup is added to.</param>
    public static string MemoryOptimizedFilegroup(string databaseName) =>
        DeriveFromDatabase(databaseName, MemoryOptimizedFilegroupSuffix);

    /// <summary>Names the memory-optimized container's logical file for one exact E2E database.</summary>
    /// <param name="databaseName">The exact owned database the container belongs to.</param>
    public static string MemoryOptimizedContainer(string databaseName) =>
        DeriveFromDatabase(databaseName, MemoryOptimizedContainerSuffix);

    /// <summary>
    /// Names the memory-optimized container's directory for one exact E2E database. It is a
    /// single directory name, to be combined with a location SQL Server itself reports.
    /// </summary>
    /// <param name="databaseName">The exact owned database the container belongs to.</param>
    /// <remarks>
    /// The name is derived from an already-validated E2E database name, whose grammar admits
    /// only ASCII letters, digits, underscore, and hyphen. It can therefore contain no path
    /// separator, no drive or UNC prefix, no <c>..</c> segment, and no quoting-relevant
    /// character, so combining it with a server-reported directory cannot escape that
    /// directory.
    /// </remarks>
    public static string MemoryOptimizedContainerDirectory(string databaseName) =>
        DeriveFromDatabase(databaseName, MemoryOptimizedContainerDirectorySuffix);

    /// <summary>Validates a database as an exact protected E2E drop target.</summary>    /// <summary>Validates a database as an exact protected E2E drop target.</summary>
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

    /// <summary>
    /// Builds one owned-resource name from the exact database it belongs to.
    /// </summary>
    /// <remarks>
    /// The database name goes through <see cref="ValidateDroppableDatabase"/> first, so a
    /// derived name is only ever produced for a name that already satisfies the protected
    /// E2E prefix, length, and character guards. A generated database name is at most 105
    /// characters, which leaves every suffix here inside <c>sysname</c>; the check stays so
    /// that a caller-supplied name cannot silently produce a truncated or invalid one.
    /// </remarks>
    private static string DeriveFromDatabase(string databaseName, string suffix)
    {
        ValidateDroppableDatabase(databaseName);
        var name = databaseName + suffix;
        if (name.Length > 128)
        {
            throw new ArgumentException(
                $"The generated E2E name '{name}' exceeds 128 characters.",
                nameof(databaseName));
        }

        return name;
    }

    private static string ValidateSuffix(string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        if (suffix.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("The E2E unique suffix must be ASCII alphanumeric text.", nameof(suffix));
        return suffix;
    }
}
