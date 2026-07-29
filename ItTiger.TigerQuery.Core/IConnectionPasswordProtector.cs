namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Defines how profile passwords are transformed immediately before persistence
/// and immediately after loading.
/// </summary>
/// <remarks>
/// Implementations may mutate password-related properties on the supplied profile.
/// The store invokes these methods synchronously once per profile and does not
/// provide concurrent calls for a single store operation.
/// </remarks>
public interface IConnectionPasswordProtector
{
    /// <summary>Prepares a profile's password fields for JSON persistence.</summary>
    /// <param name="profile">The mutable profile about to be serialized.</param>
    void ProtectForSave(SqlServerConnectionProfile profile);

    /// <summary>Restores usable in-memory password fields after JSON deserialization.</summary>
    /// <param name="profile">The mutable profile that was loaded.</param>
    void UnprotectAfterLoad(SqlServerConnectionProfile profile);
}
