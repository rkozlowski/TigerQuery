namespace ItTiger.TigerQuery.Core;

public static class ConnectionPasswordProtector
{
    public static IConnectionPasswordProtector CreateDefault()
    {
        if (OperatingSystem.IsWindows())
            return new DpapiConnectionPasswordProtector();

        return new NonPersistingConnectionPasswordProtector();
    }
}
