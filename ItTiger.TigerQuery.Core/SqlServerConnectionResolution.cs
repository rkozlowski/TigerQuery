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

    public bool IsSuccess { get; }

    /// <summary>The resolved connection string. Non-null only when <see cref="IsSuccess"/> is true.</summary>
    public string? ConnectionString { get; }

    /// <summary>A clean, user-facing failure reason. Non-null only when <see cref="IsSuccess"/> is false.</summary>
    public string? ErrorMessage { get; }

    public static SqlServerConnectionResolution Success(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new SqlServerConnectionResolution(true, connectionString, null);
    }

    public static SqlServerConnectionResolution Failure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new SqlServerConnectionResolution(false, null, errorMessage);
    }
}
