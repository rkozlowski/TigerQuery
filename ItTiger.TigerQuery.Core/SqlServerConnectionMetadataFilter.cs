namespace ItTiger.TigerQuery.Core;

/// <summary>Comparison applied by a SQL Server connection metadata filter.</summary>
public enum SqlServerConnectionMetadataFilterOperator
{
    /// <summary>The key must exist and its value must match exactly.</summary>
    Equals,

    /// <summary>The key must exist, including when its value is empty.</summary>
    IsSet,

    /// <summary>The key must not exist.</summary>
    IsNotSet
}

/// <summary>
/// One ordinal, case-sensitive predicate over opaque connection-profile metadata.
/// </summary>
public sealed record SqlServerConnectionMetadataFilter
{
    /// <summary>The non-empty metadata key to test. It is not trimmed or normalized.</summary>
    public required string Key { get; init; }

    /// <summary>The comparison to apply. The default is <see cref="SqlServerConnectionMetadataFilterOperator.Equals"/>.</summary>
    public SqlServerConnectionMetadataFilterOperator Operator { get; init; }

    /// <summary>
    /// The exact value required by <see cref="SqlServerConnectionMetadataFilterOperator.Equals"/>.
    /// It must be null for the other operators.
    /// </summary>
    public string? Value { get; init; }
}
