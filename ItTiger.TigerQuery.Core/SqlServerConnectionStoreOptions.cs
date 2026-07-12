namespace ItTiger.TigerQuery.Core;

public sealed class SqlServerConnectionStoreOptions
{
    public required string FilePath { get; init; }

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
