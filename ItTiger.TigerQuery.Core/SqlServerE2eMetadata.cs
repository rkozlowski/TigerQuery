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
/// General E2E authorization is not bootstrap authorization. A bootstrap profile must be
/// selected by the caller's or host's expected name and must also carry
/// <see cref="Bootstrap"/><c>=true</c>. Neither the name nor the metadata is sufficient on
/// its own — see <see cref="SqlServerE2eConnectionResolver"/>.
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
    /// <summary>The metadata namespace reserved for TigerQuery E2E lifecycle state.</summary>
    /// <remarks>
    /// Reserved by documentation and by <see cref="IsReservedKey"/>. Generic profile,
    /// store-copy, and CLI metadata mutations reject writes to it; TigerQuery-owned
    /// operations such as <see cref="AuthorizeNewProfile"/> are its only writers.
    /// </remarks>
    public const string ReservedKeyPrefix = "ittiger.e2e.";

    /// <summary>
    /// <c>ittiger.e2e.enabled</c> — the profile may be used by TigerQuery E2E
    /// infrastructure. Required, and required to be exactly <see cref="True"/>.
    /// </summary>
    public const string Enabled = "ittiger.e2e.enabled";

    /// <summary>
    /// <c>ittiger.e2e.bootstrap</c> — the profile is authorized to act as an E2E bootstrap
    /// connection. Required, and required to be exactly <see cref="True"/>, in addition to
    /// selecting the profile by its expected name and requiring <see cref="Enabled"/>.
    /// </summary>
    public const string Bootstrap = "ittiger.e2e.bootstrap";

    /// <summary>
    /// <c>ittiger.e2e.allow-database-create</c> — E2E workflows may create databases
    /// through this profile. Checked only when a caller asks for that permission, and
    /// never implied by <see cref="Enabled"/>.
    /// </summary>
    public const string AllowDatabaseCreation = "ittiger.e2e.allow-database-create";

    /// <summary>The session that owns or correlates a non-bootstrap E2E profile.</summary>
    public const string SessionId = "ittiger.e2e.session-id";

    /// <summary>The exact database targeted by a non-bootstrap E2E profile.</summary>
    public const string DatabaseName = "ittiger.e2e.database.name";

    /// <summary>Whether the recorded database is owned and may be dropped by E2E cleanup.</summary>
    public const string AllowDatabaseDrop = "ittiger.e2e.database.allow-drop";

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
    /// Adds the canonical TigerQuery E2E and bootstrap authorization metadata to a newly
    /// created connection profile.
    /// </summary>
    /// <param name="profile">The bootstrap profile being created.</param>
    /// <param name="allowDatabaseCreation">
    /// Whether E2E infrastructure is also authorized to create databases through the
    /// profile.
    /// </param>
    /// <remarks>
    /// This is the TigerQuery-owned bootstrap write path. The profile name identifies the
    /// expected profile during resolution; <see cref="Bootstrap"/><c>=true</c> authorizes
    /// the selected profile to act as the bootstrap. Both are required.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    public static void AuthorizeNewBootstrapProfile(
        SqlServerConnectionProfile profile,
        bool allowDatabaseCreation = false)
    {
        AuthorizeNewProfile(profile, allowDatabaseCreation);
        profile.SetReservedMetadata(Bootstrap, True);
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

    /// <summary>Writes the complete protected metadata schema for a session connection.</summary>
    /// <param name="profile">The new non-bootstrap E2E profile.</param>
    /// <param name="sessionId">The non-empty safety-correlation identifier.</param>
    /// <param name="databaseName">The exact database targeted by the profile.</param>
    /// <param name="allowDatabaseDrop">Whether dedicated E2E cleanup owns the database.</param>
    public static void ConfigureSessionProfile(
        SqlServerConnectionProfile profile,
        Guid sessionId,
        string databaseName,
        bool allowDatabaseDrop)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (sessionId == Guid.Empty)
            throw new ArgumentException("A non-empty E2E session ID is required.", nameof(sessionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        profile.ClearReservedMetadata();
        profile.SetReservedMetadata(Enabled, True);
        profile.SetReservedMetadata(Bootstrap, False);
        profile.SetReservedMetadata(AllowDatabaseCreation, False);
        profile.SetReservedMetadata(SessionId, sessionId.ToString("D"));
        profile.SetReservedMetadata(DatabaseName, databaseName);
        profile.SetReservedMetadata(AllowDatabaseDrop, allowDatabaseDrop ? True : False);
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
