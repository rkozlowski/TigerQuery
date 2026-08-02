namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Outcome of choosing a connection-store file path. Either carries a normalized absolute
/// <see cref="FilePath"/> and the <see cref="Source"/> that selected it, or an
/// <see cref="Error"/> and <see cref="ErrorMessage"/> describing why the highest-priority
/// source that supplied a value could not be used.
/// </summary>
/// <remarks>
/// A failure never carries a path. The resolver does not fall back to a lower-priority
/// source once a higher-priority one has spoken, so a misconfigured build agent fails
/// visibly instead of quietly using a developer's personal store.
/// </remarks>
public sealed class SqlServerConnectionStorePathResolution
{
    private SqlServerConnectionStorePathResolution(
        bool isSuccess,
        string? filePath,
        SqlServerConnectionStorePathSource source,
        SqlServerConnectionStorePathError error,
        string? attemptedValue,
        string? errorMessage)
    {
        IsSuccess = isSuccess;
        FilePath = filePath;
        Source = source;
        Error = error;
        AttemptedValue = attemptedValue;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets whether a usable store file path was selected.</summary>
    public bool IsSuccess { get; }

    /// <summary>The normalized absolute store path. Non-null only when <see cref="IsSuccess"/> is true.</summary>
    /// <remarks>
    /// Suitable for <see cref="SqlServerConnectionStoreOptions.FilePath"/>. Nothing has
    /// been read, created, or checked at this location; that happens when a store is
    /// constructed and used.
    /// </remarks>
    public string? FilePath { get; }

    /// <summary>The source that selected the path, or that supplied the unusable value.</summary>
    public SqlServerConnectionStorePathSource Source { get; }

    /// <summary>Why resolution failed. <see cref="SqlServerConnectionStorePathError.None"/> on success.</summary>
    public SqlServerConnectionStorePathError Error { get; }

    /// <summary>The rejected value, as supplied. Non-null only on failure, and null when the value was null.</summary>
    /// <remarks>
    /// A store path is not a secret, so it is safe to echo back to the user; showing it
    /// is usually the fastest way to reveal a mistyped option or a badly quoted
    /// container variable.
    /// </remarks>
    public string? AttemptedValue { get; }

    /// <summary>A clean, user-facing failure reason in English. Non-null only when <see cref="IsSuccess"/> is false.</summary>
    /// <remarks>
    /// Core carries no resources. A localizing host should compose its own message from
    /// <see cref="Error"/>, <see cref="Source"/>, and <see cref="AttemptedValue"/> and use
    /// this only as the neutral fallback.
    /// </remarks>
    public string? ErrorMessage { get; }

    /// <summary>Creates a successful resolution.</summary>
    /// <param name="filePath">The nonblank normalized absolute path.</param>
    /// <param name="source">The source that selected <paramref name="filePath"/>.</param>
    /// <returns>A success carrying <paramref name="filePath"/> and <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="filePath"/> is null, empty, or whitespace. The parameter name is
    /// <c>filePath</c>.
    /// </exception>
    public static SqlServerConnectionStorePathResolution Success(
        string filePath,
        SqlServerConnectionStorePathSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return new SqlServerConnectionStorePathResolution(
            true, filePath, source, SqlServerConnectionStorePathError.None, null, null);
    }

    /// <summary>Creates a failed resolution.</summary>
    /// <param name="source">The source that supplied the unusable value.</param>
    /// <param name="error">The reason the value was rejected.</param>
    /// <param name="attemptedValue">The rejected value, or null when the value was null.</param>
    /// <param name="errorMessage">The nonblank English failure reason.</param>
    /// <returns>A failure carrying no path.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="error"/> is <see cref="SqlServerConnectionStorePathError.None"/>,
    /// or <paramref name="errorMessage"/> is null, empty, or whitespace.
    /// </exception>
    public static SqlServerConnectionStorePathResolution Failure(
        SqlServerConnectionStorePathSource source,
        SqlServerConnectionStorePathError error,
        string? attemptedValue,
        string errorMessage)
    {
        if (error == SqlServerConnectionStorePathError.None)
            throw new ArgumentException("A failed resolution requires a reason.", nameof(error));
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new SqlServerConnectionStorePathResolution(
            false, null, source, error, attemptedValue, errorMessage);
    }
}
