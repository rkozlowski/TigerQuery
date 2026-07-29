namespace ItTiger.TigerQuery.Core;

/// <summary>Specifies the JSON file used by a <see cref="SqlServerConnectionStore"/>.</summary>
public sealed class SqlServerConnectionStoreOptions
{
    /// <summary>Gets the connection-profile JSON file path.</summary>
    public required string FilePath { get; init; }

    /// <summary>Creates a per-user store location shared by a vendor's applications.</summary>
    /// <param name="vendorName">The nonblank vendor directory name.</param>
    /// <param name="fileName">The nonblank JSON file name.</param>
    /// <returns>
    /// Options beneath the roaming application-data directory on Windows or
    /// <c>~/.config</c> on other supported platforms.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="vendorName"/> or <paramref name="fileName"/> is null,
    /// empty, or whitespace. The parameter name identifies the invalid argument.
    /// </exception>
    public static SqlServerConnectionStoreOptions Shared(
        string vendorName,
        string fileName = "sqlserver-connections.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return new SqlServerConnectionStoreOptions
        {
            FilePath = Path.Combine(GetConfigRoot(), vendorName, fileName)
        };
    }

    /// <summary>Creates a per-user store location isolated to one application.</summary>
    /// <param name="vendorName">The nonblank vendor directory name.</param>
    /// <param name="appName">The nonblank application directory name.</param>
    /// <param name="fileName">The nonblank JSON file name.</param>
    /// <returns>
    /// Options beneath vendor and application directories in the platform's
    /// per-user configuration root.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="vendorName"/>, <paramref name="appName"/>, or
    /// <paramref name="fileName"/> is null, empty, or whitespace. The parameter
    /// name identifies the invalid argument.
    /// </exception>
    public static SqlServerConnectionStoreOptions AppSpecific(
        string vendorName,
        string appName,
        string fileName = "connections.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return new SqlServerConnectionStoreOptions
        {
            FilePath = Path.Combine(GetConfigRoot(), vendorName, appName, fileName)
        };
    }

    private static string GetConfigRoot()
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config");
    }
}
