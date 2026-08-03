namespace ItTiger.TigerQuery.Core;

/// <summary>A safe failure to resolve or interpret an external profile value.</summary>
public sealed class SqlServerExternalValueException : InvalidOperationException
{
    /// <summary>Initializes a failure whose message contains no resolved value.</summary>
    public SqlServerExternalValueException(string message)
        : base(message)
    {
    }
}
