namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Outcome of resolving a saved connection profile (by name) to a usable SQL Server
/// connection string. Either carries a non-empty <see cref="ConnectionString"/> on
/// success, or an <see cref="ErrorMessage"/> describing why resolution failed.
/// </summary>
public sealed class SqlServerConnectionResolution
{
    private SqlServerConnectionResolution(bool isSuccess, string? connectionString, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ConnectionString = connectionString;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets whether resolution produced a usable nonblank connection string.</summary>
    public bool IsSuccess { get; }

    /// <summary>The resolved connection string. Non-null only when <see cref="IsSuccess"/> is true.</summary>
    public string? ConnectionString { get; }

    /// <summary>A clean, user-facing failure reason. Non-null only when <see cref="IsSuccess"/> is false.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Creates a successful resolution.</summary>
    /// <param name="connectionString">The nonblank resolved connection string.</param>
    /// <returns>A success carrying <paramref name="connectionString"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="connectionString"/> is null, empty, or whitespace. The
    /// parameter name is <c>connectionString</c>.
    /// </exception>
    public static SqlServerConnectionResolution Success(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new SqlServerConnectionResolution(true, connectionString, null);
    }

    /// <summary>Creates a failed resolution with a user-facing reason.</summary>
    /// <param name="errorMessage">The nonblank failure reason.</param>
    /// <returns>A failure carrying <paramref name="errorMessage"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="errorMessage"/> is null, empty, or whitespace. The parameter
    /// name is <c>errorMessage</c>.
    /// </exception>
    public static SqlServerConnectionResolution Failure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new SqlServerConnectionResolution(false, null, errorMessage);
    }
}
