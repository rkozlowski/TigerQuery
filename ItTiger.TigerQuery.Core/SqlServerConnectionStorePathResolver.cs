namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Chooses the connection-store file path for a run: an explicit override, then the
/// TigerQuery environment variable, then the host application's default location.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the precedence is defined. Command-line tools, test libraries,
/// services, and containers all get the same answer, so a store selected by a tool and a
/// store selected by library code in the same workflow are the same file.
/// </para>
/// <para>
/// Resolution is inert. It reads the environment, normalizes a string, and returns — it
/// creates nothing, opens nothing, and asks the file system nothing, so calling it is
/// safe long before a caller knows whether a store will be used. Deciding what an absent
/// file means, and constructing the <see cref="SqlServerConnectionStore"/>, are separate
/// steps the caller owns.
/// </para>
/// </remarks>
public static class SqlServerConnectionStorePathResolver
{
    /// <summary>Resolves the store file path from the configured sources.</summary>
    /// <param name="options">The override, environment, and application-default inputs.</param>
    /// <returns>
    /// A success carrying the normalized absolute path and the source that selected it,
    /// or a failure naming the source whose value could not be used.
    /// </returns>
    /// <remarks>
    /// A source that supplies no value at all is skipped. A source that supplies a value
    /// decides the outcome: if that value is unusable the result is a failure, never a
    /// fallback to a lower-priority source. Only syntactic validation is applied — blank
    /// values, values that cannot be normalized to an absolute path, and values with no
    /// file-name component.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="SqlServerConnectionStorePathOptions.EnvironmentVariableName"/> is null,
    /// empty, or whitespace. That is a host configuration mistake rather than a user
    /// error, so it throws instead of producing a failed resolution.
    /// </exception>
    public static SqlServerConnectionStorePathResolution Resolve(
        SqlServerConnectionStorePathOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.EnvironmentVariableName,
            $"{nameof(options)}.{nameof(SqlServerConnectionStorePathOptions.EnvironmentVariableName)}");

        if (options.ExplicitFilePath is not null)
        {
            return Select(
                options.ExplicitFilePath, SqlServerConnectionStorePathSource.Explicit, options);
        }

        var readEnvironment = options.EnvironmentReader ?? Environment.GetEnvironmentVariable;
        var fromEnvironment = readEnvironment(options.EnvironmentVariableName);
        if (fromEnvironment is not null)
        {
            return Select(
                fromEnvironment, SqlServerConnectionStorePathSource.EnvironmentVariable, options);
        }

        // DefaultFilePath is required, but `null!` and "" still reach here; validating it
        // like any other source beats handing back a path no store could open.
        return Select(
            options.DefaultFilePath, SqlServerConnectionStorePathSource.ApplicationDefault, options);
    }

    private static SqlServerConnectionStorePathResolution Select(
        string? value,
        SqlServerConnectionStorePathSource source,
        SqlServerConnectionStorePathOptions options)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Failure(source, value, SqlServerConnectionStorePathError.Blank, options);

        if (!TryGetFullPath(value, out var normalized))
            return Failure(source, value, SqlServerConnectionStorePathError.Malformed, options);

        // A trailing separator and a bare root both leave no file name behind. Whether the
        // path names an existing directory is a question for the file system, and asking it
        // is exactly what resolution must not do.
        if (string.IsNullOrEmpty(Path.GetFileName(normalized)))
            return Failure(source, value, SqlServerConnectionStorePathError.MissingFileName, options);

        return SqlServerConnectionStorePathResolution.Success(normalized, source);
    }

    private static bool TryGetFullPath(string value, out string normalized)
    {
        try
        {
            normalized = Path.GetFullPath(value);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (PathTooLongException)
        {
        }
        catch (IOException)
        {
        }

        normalized = string.Empty;
        return false;
    }

    private static SqlServerConnectionStorePathResolution Failure(
        SqlServerConnectionStorePathSource source,
        string? value,
        SqlServerConnectionStorePathError error,
        SqlServerConnectionStorePathOptions options)
    {
        var subject = Describe(source, options);
        var message = error switch
        {
            SqlServerConnectionStorePathError.Blank =>
                $"{subject} is present but empty.",
            SqlServerConnectionStorePathError.Malformed =>
                $"{subject} is not a valid file path: '{value}'.",
            _ =>
                $"{subject} names a directory, not a connection-store file: '{value}'."
        };

        return SqlServerConnectionStorePathResolution.Failure(
            source,
            error,
            value,
            $"{message} TigerQuery does not fall back to another connection store.");
    }

    private static string Describe(
        SqlServerConnectionStorePathSource source,
        SqlServerConnectionStorePathOptions options) => source switch
        {
            SqlServerConnectionStorePathSource.Explicit =>
                "The explicitly supplied connection-store file path",
            SqlServerConnectionStorePathSource.EnvironmentVariable =>
                $"The {options.EnvironmentVariableName} environment variable",
            _ =>
                "The application default connection-store file path"
        };
}
