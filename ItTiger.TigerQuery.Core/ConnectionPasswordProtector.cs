namespace ItTiger.TigerQuery.Core;

/// <summary>Selects the platform-default connection password strategy.</summary>
public static class ConnectionPasswordProtector
{
    /// <summary>Creates the default password protector for the current platform.</summary>
    /// <returns>
    /// <see cref="DpapiConnectionPasswordProtector"/> on Windows; otherwise
    /// <see cref="NonPersistingConnectionPasswordProtector"/>.
    /// </returns>
    public static IConnectionPasswordProtector CreateDefault()
    {
        if (OperatingSystem.IsWindows())
            return new DpapiConnectionPasswordProtector();

        return new NonPersistingConnectionPasswordProtector();
    }
}
