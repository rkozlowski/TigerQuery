namespace ItTiger.TigerQuery.Core;

/// <summary>
/// The result of resolving a TigerQuery E2E bootstrap connection profile: a
/// <see cref="Status"/>, the <see cref="Profile"/> when — and only when — one was
/// authorized, and diagnostics for a developer fixing their setup.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="SqlServerE2eResolutionStatus.Resolved"/> carries a
/// <see cref="Profile"/>. Every other status carries none, so code that reaches for the
/// profile without checking the status gets a null reference rather than an unauthorized
/// connection.
/// </para>
/// <para>
/// <see cref="Errors"/> and <see cref="CandidateNames"/> exist to be printed. They contain
/// profile names, metadata keys, and validation messages, and never a password, a
/// connection string, or any other credential material.
/// </para>
/// </remarks>
public sealed class SqlServerE2eConnectionResolution
{
    /// <summary>Gets the resolution outcome. Required.</summary>
    public required SqlServerE2eResolutionStatus Status { get; init; }

    /// <summary>
    /// Gets the authorized bootstrap profile, or null for every status other than
    /// <see cref="SqlServerE2eResolutionStatus.Resolved"/>.
    /// </summary>
    /// <remarks>
    /// A detached copy loaded from the store, exactly as
    /// <see cref="SqlServerConnectionStore.Find"/> would return it. Nothing has been
    /// connected to; turning it into a connection is a separate, explicit step.
    /// </remarks>
    public SqlServerConnectionProfile? Profile { get; init; }

    /// <summary>
    /// Gets the names of the store's E2E-authorized profiles, in store order, as a hint
    /// about what could have been meant. Empty on
    /// <see cref="SqlServerE2eResolutionStatus.Resolved"/>.
    /// </summary>
    /// <remarks>
    /// A diagnostic, never a menu the resolver picks from: listing candidates is exactly
    /// what makes it visible that TigerQuery declined to choose among them.
    /// </remarks>
    public IReadOnlyList<string> CandidateNames { get; init; } = [];

    /// <summary>
    /// Gets the user-facing reasons the resolution did not produce a profile, in English.
    /// Empty on <see cref="SqlServerE2eResolutionStatus.Resolved"/>.
    /// </summary>
    /// <remarks>
    /// Core carries no resources. A localizing host composes its own text from
    /// <see cref="Status"/> and the names it already knows, and uses these as the neutral
    /// fallback.
    /// </remarks>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets the profile name that selection was attempted with, or null when neither the
    /// caller nor the host supplied one.
    /// </summary>
    /// <remarks>
    /// Reported so a diagnostic can say <i>which</i> name was looked up, which is the
    /// difference between "you have no bootstrap connection" and "your host default names
    /// a profile you never created".
    /// </remarks>
    public string? RequestedName { get; init; }
}
