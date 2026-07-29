namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Leaves every password field unchanged.
/// </summary>
/// <remarks>
/// This strategy provides no password protection. It is intended for tests or
/// stores whose security is managed entirely outside this library.
/// </remarks>
public sealed class NoOpConnectionPasswordProtector : IConnectionPasswordProtector
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    public void ProtectForSave(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    public void UnprotectAfterLoad(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
    }
}
