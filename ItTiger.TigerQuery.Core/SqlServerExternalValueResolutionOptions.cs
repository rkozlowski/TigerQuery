namespace ItTiger.TigerQuery.Core;

/// <summary>Injectable external-source readers used when an effective connection is built.</summary>
public sealed class SqlServerExternalValueResolutionOptions
{
    /// <summary>Gets the environment lookup, or null to use the process environment.</summary>
    public Func<string, string?>? EnvironmentReader { get; init; }

    /// <summary>Gets the UTF-8 text-file reader, or null to use <see cref="File.ReadAllText(string)"/>.</summary>
    public Func<string, string>? FileReader { get; init; }
}
