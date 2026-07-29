namespace ItTiger.TigerQuery.Core;

/// <summary>Specifies a comparison over one metadata key.</summary>
public enum SqlServerConnectionMetadataFilterOperator
{
    /// <summary>The key must exist and its value must match exactly using ordinal comparison.</summary>
    Equals,

    /// <summary>The key must exist, including when its value is empty.</summary>
    IsSet,

    /// <summary>The key must not exist.</summary>
    IsNotSet
}

/// <summary>
/// One ordinal, case-sensitive predicate over opaque connection-profile metadata.
/// </summary>
/// <remarks>
/// Filters are immutable value records after initialization. A collection passed
/// to <see cref="SqlServerConnectionStore.QueryByMetadata"/> is combined using AND semantics.
/// </remarks>
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
