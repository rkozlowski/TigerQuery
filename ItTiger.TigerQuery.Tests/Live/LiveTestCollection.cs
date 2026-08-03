namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Serializes SQL-backed tests that share the one environment-selected connection store.
/// Lifecycle tests persist and remove generated profiles in that store, so parallel reads
/// would turn expected file-lock contention into unrelated configuration failures.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LiveTestCollection
{
    public const string Name = "TigerQuery live SQL tests";
}
