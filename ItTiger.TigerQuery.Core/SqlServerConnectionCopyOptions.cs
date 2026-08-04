namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Describes the controlled overrides applied by
/// <see cref="SqlServerConnectionStore.Copy"/> to a copied connection profile.
/// </summary>
/// <remarks>
/// <para>
/// Everything the source profile persists is preserved unless it is named here.
/// That includes the authentication mode, the protected password representation,
/// free-form connection-string options, and every metadata entry.
/// </para>
/// <para>
/// Metadata keys are compared ordinally and case-sensitively and are never trimmed
/// or normalized, exactly as elsewhere in the connection model.
/// </para>
/// </remarks>
public sealed class SqlServerConnectionCopyOptions
{
    /// <summary>Gets the nonblank name given to the copied profile.</summary>
    /// <remarks>
    /// The name must not already exist in the store; copy is never an upsert.
    /// </remarks>
    public required string TargetName { get; init; }

    /// <summary>Gets the replacement initial catalog for the copy.</summary>
    /// <remarks>
    /// <see langword="null"/> preserves the source database, an empty string clears
    /// it to produce a server-level profile, and any other value replaces it.
    /// </remarks>
    public string? InitialCatalogOverride { get; init; }

    /// <summary>Gets metadata entries added to, or replaced on, the copy.</summary>
    /// <remarks>Keys must be non-empty and values must not be null.</remarks>
    public IReadOnlyDictionary<string, string> MetadataToSet { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets metadata keys removed from the copy.</summary>
    /// <remarks>
    /// Removals are applied before <see cref="MetadataToSet"/>. A key present in
    /// both collections is rejected rather than silently resolved.
    /// </remarks>
    public IReadOnlyCollection<string> MetadataToRemove { get; init; } = [];

    /// <summary>Validates the option surface independently of any store content.</summary>
    /// <param name="parameterName">The caller's parameter name used in thrown exceptions.</param>
    /// <exception cref="ArgumentException">An option value violates the documented rules.</exception>
    internal void Validate(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(TargetName))
            throw new ArgumentException("A copy target name is required.", parameterName);

        if (MetadataToSet is null)
            throw new ArgumentException("Metadata assignments must not be null.", parameterName);

        if (MetadataToRemove is null)
            throw new ArgumentException("Metadata removals must not be null.", parameterName);

        foreach (var (key, value) in MetadataToSet)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Metadata keys must not be empty.", parameterName);

            if (SqlServerE2eMetadata.IsReservedKey(key))
                throw new ArgumentException(ReservedMetadataMessage, parameterName);

            if (value is null)
                throw new ArgumentException(
                    $"The metadata value for key '{key}' must not be null.",
                    parameterName);
        }

        var removals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in MetadataToRemove)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Metadata keys must not be empty.", parameterName);

            if (SqlServerE2eMetadata.IsReservedKey(key))
                throw new ArgumentException(ReservedMetadataMessage, parameterName);

            removals.Add(key);
        }

        foreach (var key in MetadataToSet.Keys)
        {
            if (removals.Contains(key))
                throw new ArgumentException(
                    "The same metadata key cannot be both set and removed in one copy.",
                    parameterName);
        }
    }

    private const string ReservedMetadataMessage =
        "Metadata keys beginning with 'ittiger.e2e.' are reserved for TigerQuery.";
}
