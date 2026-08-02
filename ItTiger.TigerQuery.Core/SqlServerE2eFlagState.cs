namespace ItTiger.TigerQuery.Core;

/// <summary>
/// The state of one reserved TigerQuery E2E Boolean metadata entry on a connection profile.
/// </summary>
/// <remarks>
/// The grammar is deliberately narrow, so every reading of a reserved key has exactly one
/// of four answers and none of them is a guess. In particular <see cref="Malformed"/> is
/// not <see cref="False"/>: a profile written with <c>True</c>, <c>1</c>, or <c>yes</c>
/// fails loudly instead of quietly withdrawing an authorization the author believed they
/// had granted, which is the failure mode that makes a typo dangerous.
/// </remarks>
public enum SqlServerE2eFlagState
{
    /// <summary>The key is not present on the profile. It grants nothing.</summary>
    Absent = 0,

    /// <summary>The key is present with the exact value <c>true</c>.</summary>
    True = 1,

    /// <summary>The key is present with the exact value <c>false</c>.</summary>
    False = 2,

    /// <summary>
    /// The key is present with a value that is neither <c>true</c> nor <c>false</c>.
    /// </summary>
    /// <remarks>
    /// This is a configuration error, not a denial. Callers must report it rather than
    /// treat it as <see cref="False"/>.
    /// </remarks>
    Malformed = 3
}
