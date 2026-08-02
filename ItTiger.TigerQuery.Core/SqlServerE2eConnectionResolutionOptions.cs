namespace ItTiger.TigerQuery.Core;

/// <summary>
/// The inputs that select and qualify a TigerQuery E2E bootstrap connection profile.
/// </summary>
/// <remarks>
/// <para>
/// A bootstrap profile is always chosen <b>by name</b>: <see cref="ConnectionName"/> from
/// the caller, otherwise <see cref="DefaultConnectionName"/> from host configuration. When
/// neither is supplied nothing is selected, however many authorized profiles the store
/// holds. Authorization metadata, profile naming conventions, and store order never
/// nominate a profile on their own.
/// </para>
/// <para>
/// The two names are separate rather than one pre-merged value because they fail
/// differently. A caller who names a profile that does not exist made a mistake and gets
/// <see cref="SqlServerE2eResolutionStatus.Invalid"/>; a host convention name that does
/// not exist yet is just an unconfigured machine and gets
/// <see cref="SqlServerE2eResolutionStatus.NotConfigured"/>.
/// </para>
/// </remarks>
public sealed class SqlServerE2eConnectionResolutionOptions
{
    /// <summary>
    /// Gets the profile name the caller asked for — a command-line option, a test-fixture
    /// setting, an API argument. Null when the caller named nothing.
    /// </summary>
    /// <remarks>
    /// Matched ordinally and case-sensitively, like every other profile-name lookup. Wins
    /// over <see cref="DefaultConnectionName"/>.
    /// </remarks>
    public string? ConnectionName { get; init; }

    /// <summary>
    /// Gets the host application's configured bootstrap-connection convention, used when
    /// <see cref="ConnectionName"/> is null. Null means the host configured none.
    /// </summary>
    /// <remarks>
    /// This is host configuration such as <c>tiger-sqlcmd-e2e</c>, not a value TigerQuery
    /// invents. Core defines no default name, because a default name TigerQuery chose
    /// would make an unrelated profile eligible on somebody else's machine.
    /// </remarks>
    public string? DefaultConnectionName { get; init; }

    /// <summary>
    /// Gets whether the resolved profile must also carry
    /// <see cref="SqlServerE2eMetadata.AllowDatabaseCreation"/><c>=true</c>.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/>, so a caller only performing reads never
    /// demands a permission it does not need. Database-creating workflows set it, and the
    /// permission is never implied by
    /// <see cref="SqlServerE2eMetadata.Enabled"/>.
    /// </remarks>
    public bool RequireDatabaseCreationPermission { get; init; }

    /// <summary>
    /// Gets the policy the candidate profile is structurally validated against;
    /// <see cref="SqlServerConnectionValidationPolicy.DatabaseOptional"/> when null.
    /// </summary>
    /// <remarks>
    /// The default is deliberately the permissive one: a bootstrap profile normally names
    /// a server rather than a database, because the databases it works with do not exist
    /// yet. A caller that needs an initial catalog asks for
    /// <see cref="SqlServerConnectionValidationPolicy.DatabaseRequired"/>.
    /// </remarks>
    public SqlServerConnectionValidationPolicy? ValidationPolicy { get; init; }
}
