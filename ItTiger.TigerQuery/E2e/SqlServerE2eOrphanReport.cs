namespace ItTiger.TigerQuery.E2e;

/// <summary>Reports prefix-matching databases that this lifecycle does not own.</summary>
/// <remarks>
/// The names are informational candidates only. This type deliberately exposes no delete
/// operation; removing an orphan requires a separate manual process and explicit human
/// approval.
/// </remarks>
public sealed class SqlServerE2eOrphanReport
{
    /// <summary>Gets the prefix used for detection.</summary>
    public required string DatabasePrefix { get; init; }

    /// <summary>Gets the exact candidate database names reported by SQL Server.</summary>
    public IReadOnlyList<string> DatabaseNames { get; init; } = [];
}
