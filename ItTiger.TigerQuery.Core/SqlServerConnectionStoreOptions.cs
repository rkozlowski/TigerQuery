namespace ItTiger.TigerQuery.Core;

/// <summary>Specifies the JSON file used by a <see cref="SqlServerConnectionStore"/>.</summary>
/// <remarks>
/// There is deliberately no universal default. <see cref="Shared"/> suits a store
/// several applications from one vendor share, <see cref="AppSpecific"/> isolates a
/// single application, and an explicit <see cref="FilePath"/> lets a host or test run
/// select any JSON file. The host makes that choice once, constructs one
/// <see cref="SqlServerConnectionStore"/>, and injects it everywhere.
/// </remarks>
public sealed class SqlServerConnectionStoreOptions
{
    /// <summary>Gets the connection-profile JSON file path.</summary>
    /// <remarks>
    /// A relative path is resolved to an absolute one when the store is constructed
    /// and is then exposed as <see cref="SqlServerConnectionStore.FilePath"/>.
    /// </remarks>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets how long a mutating store operation waits for exclusive access to
    /// <see cref="FilePath"/> before failing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is fifteen seconds. Exceeding the budget throws
    /// <see cref="TimeoutException"/>; the store is never mutated in that case, and
    /// the operation is safe to retry.
    /// </para>
    /// <para>
    /// The wait covers both the in-process gate and the sibling
    /// <c>&lt;file&gt;.lock</c> used to keep a second process — another tool, a CLI
    /// session, or a test run — out of the same user store. The in-process guarantee
    /// is absolute; the cross-process one holds for processes that use this library
    /// and a file system with exclusive sharing semantics. A value of zero or less
    /// disables waiting and fails immediately on contention.
    /// </para>
    /// </remarks>
    public TimeSpan MutationTimeout { get; init; } = TimeSpan.FromSeconds(15);

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
