namespace ItTiger.TigerQuery.Core;

/// <summary>
/// The reserved connection-profile metadata TigerQuery uses to authorize end-to-end test
/// infrastructure, and the exact grammar its values must follow.
/// </summary>
/// <remarks>
/// <para>
/// The governing rule is that <b>reachability is not authorization</b>. A profile becomes
/// usable for E2E work only because someone deliberately wrote
/// <see cref="Enabled"/><c>=true</c> into it. A valid, connectable, perfectly ordinary
/// development profile is never eligible, whatever it is called and wherever it sits in
/// the store.
/// </para>
/// <para>
/// Authorization is also not identity. These keys say what a profile is <i>allowed</i> to
/// be used for; they never say which profile is <i>the</i> bootstrap connection. That
/// choice is made by name, by the caller or by host configuration — see
/// <see cref="SqlServerE2eConnectionResolver"/>.
/// </para>
/// <para>
/// Profile metadata is compared ordinally and case-sensitively, so the grammar is exact:
/// keys are matched as written here, lower-case, and Boolean values are the literal
/// <see cref="True"/> and <see cref="False"/>. <c>ITTIGER.E2E.ENABLED</c>, <c>True</c>,
/// <c>1</c>, <c>yes</c>, and <c>&#160;true&#160;</c> are not accepted spellings — nothing
/// is trimmed, lower-cased, or interpreted.
/// </para>
/// <para>
/// The <see cref="ReservedKeyPrefix"/> namespace belongs to TigerQuery. Applications must
/// keep their own metadata under their own prefix; a key TigerQuery does not define today
/// may gain a meaning in a later release. Keys under the prefix that this version does not
/// recognize are ignored during resolution, so a store written by a newer TigerQuery still
/// loads.
/// </para>
/// </remarks>
public static class SqlServerE2eMetadata
{
    /// <summary>The metadata namespace reserved for TigerQuery: <c>ittiger.</c>.</summary>
    /// <remarks>
    /// Reserved by documentation and by <see cref="IsReservedKey"/>. Generic profile,
    /// store-copy, and CLI metadata mutations reject writes to it; TigerQuery-owned
    /// operations such as <see cref="AuthorizeNewProfile"/> are its only writers.
    /// </remarks>
    public const string ReservedKeyPrefix = "ittiger.";

    /// <summary>
    /// <c>ittiger.e2e.enabled</c> — the profile may be used by TigerQuery E2E
    /// infrastructure. Required, and required to be exactly <see cref="True"/>.
    /// </summary>
    public const string Enabled = "ittiger.e2e.enabled";

    /// <summary>
    /// <c>ittiger.e2e.allow-database-create</c> — E2E workflows may create databases
    /// through this profile. Checked only when a caller asks for that permission, and
    /// never implied by <see cref="Enabled"/>.
    /// </summary>
    public const string AllowDatabaseCreation = "ittiger.e2e.allow-database-create";

    /// <summary>The only accepted affirmative value for a reserved Boolean key.</summary>
    public const string True = "true";

    /// <summary>The only accepted negative value for a reserved Boolean key.</summary>
    public const string False = "false";

    /// <summary>
    /// Adds the canonical TigerQuery E2E authorization metadata to a newly created
    /// connection profile.
    /// </summary>
    /// <param name="profile">The profile being created.</param>
    /// <param name="allowDatabaseCreation">
    /// Whether E2E infrastructure is also authorized to create databases through the
    /// profile.
    /// </param>
    /// <remarks>
    /// This is the TigerQuery-owned write path for the reserved keys. It records
    /// authorization only; it does not identify the profile as a bootstrap profile,
    /// persist it, or open a SQL connection.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    public static void AuthorizeNewProfile(
        SqlServerConnectionProfile profile,
        bool allowDatabaseCreation = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.SetReservedMetadata(Enabled, True);
        if (allowDatabaseCreation)
            profile.SetReservedMetadata(AllowDatabaseCreation, True);
    }

    /// <summary>
    /// Preserves TigerQuery-owned metadata while rebuilding an existing profile through
    /// an ordinary edit path.
    /// </summary>
    /// <param name="source">The existing profile.</param>
    /// <param name="destination">The replacement profile.</param>
    /// <remarks>
    /// Every current or future reserved key is copied verbatim. This supports
    /// forward-compatible edits without letting generic metadata mutation APIs set or
    /// remove the namespace.
    /// </remarks>
    public static void PreserveReservedMetadata(
        SqlServerConnectionProfile source,
        SqlServerConnectionProfile destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var (key, value) in source.Metadata)
        {
            if (IsReservedKey(key))
                destination.SetReservedMetadata(key, value);
        }
    }

    /// <summary>Determines whether a metadata key falls in TigerQuery's reserved namespace.</summary>
    /// <param name="key">The key to test.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="key"/> starts with
    /// <see cref="ReservedKeyPrefix"/>, compared ordinally.
    /// </returns>
    /// <remarks>
    /// The comparison is case-sensitive for the same reason the keys are: metadata lookup
    /// is ordinal, so <c>ITTIGER.</c> is a different namespace and TigerQuery neither
    /// claims nor reads it.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="key"/> is <see langword="null"/>.
    /// </exception>
    public static bool IsReservedKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.StartsWith(ReservedKeyPrefix, StringComparison.Ordinal);
    }

    /// <summary>Reads one reserved Boolean metadata entry under the strict grammar.</summary>
    /// <param name="profile">The profile whose metadata is inspected.</param>
    /// <param name="key">The reserved key to read.</param>
    /// <returns>
    /// <see cref="SqlServerE2eFlagState.Absent"/> when the key is not present,
    /// <see cref="SqlServerE2eFlagState.True"/> or <see cref="SqlServerE2eFlagState.False"/>
    /// for an exact match, and <see cref="SqlServerE2eFlagState.Malformed"/> for anything
    /// else — including an empty value and a value that differs only in case or
    /// surrounding whitespace.
    /// </returns>
    /// <remarks>
    /// Reading metadata touches no file and opens no connection.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is null or empty. The parameter name is <c>key</c>.
    /// </exception>
    public static SqlServerE2eFlagState ReadFlag(SqlServerConnectionProfile profile, string key)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (!profile.Metadata.TryGetValue(key, out var value))
            return SqlServerE2eFlagState.Absent;

        return value switch
        {
            True => SqlServerE2eFlagState.True,
            False => SqlServerE2eFlagState.False,
            _ => SqlServerE2eFlagState.Malformed
        };
    }
}
