namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Prevents plain-text passwords from surviving save or load without providing
/// an alternative encrypted persistence mechanism.
/// </summary>
/// <remarks>
/// Both operations clear <see cref="SqlServerConnectionProfile.PlainPassword"/>.
/// This is the non-Windows default, so SQL-password profiles require the password
/// to be supplied again for each session.
/// </remarks>
public sealed class NonPersistingConnectionPasswordProtector : IConnectionPasswordProtector
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    public void ProtectForSave(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.PlainPassword = null;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    public void UnprotectAfterLoad(SqlServerConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.PlainPassword = null;
    }
}
