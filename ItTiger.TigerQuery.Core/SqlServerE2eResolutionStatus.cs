namespace ItTiger.TigerQuery.Core;

/// <summary>The outcome of resolving a TigerQuery E2E bootstrap connection profile.</summary>
/// <remarks>
/// Exactly one member means "go ahead". Everything else is a stop, and the difference
/// between the stops is what a developer needs in order to fix their setup — not
/// something the resolver may paper over.
/// </remarks>
public enum SqlServerE2eResolutionStatus
{
    /// <summary>
    /// No E2E bootstrap connection is configured. The correct response is to skip E2E work
    /// without opening a connection.
    /// </summary>
    /// <remarks>
    /// This is the answer for a clean machine: an empty or absent store, no bootstrap name
    /// supplied by either the caller or the host, or a host convention name that the
    /// developer simply has not created yet. It is a normal state, not a fault.
    /// </remarks>
    NotConfigured = 0,

    /// <summary>
    /// One profile was named, found, explicitly authorized as a bootstrap, and fully
    /// qualified for the requested operation. It is the only status that carries a profile.
    /// </summary>
    Resolved = 1,

    /// <summary>
    /// More than one profile could have been meant, so none was chosen. TigerQuery never
    /// resolves ambiguity by taking the first candidate.
    /// </summary>
    Ambiguous = 2,

    /// <summary>
    /// A specific profile was identified but cannot be used: it does not exist, it is not
    /// E2E-authorized or bootstrap-authorized, its reserved metadata is malformed, it lacks
    /// a requested permission, or it fails structural validation.
    /// </summary>
    Invalid = 3
}
