namespace ItTiger.TigerQuery.Core;

/// <summary>Identifies where a connection-profile value is resolved from.</summary>
public enum SqlServerExternalValueSource
{
    /// <summary>Read the value from an environment variable.</summary>
    EnvironmentVariable = 0,

    /// <summary>Read the value from a file.</summary>
    File = 1
}
