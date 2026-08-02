namespace ItTiger.TigerQuery.Core;

/// <summary>Why a supplied connection-store file path could not be used.</summary>
/// <remarks>
/// Every member describes a purely syntactic problem. Resolution never asks the file
/// system whether the path exists, so no member here means "missing file"; whether an
/// absent store is an error is a separate, caller-owned policy.
/// <para>
/// The code is paired with <see cref="SqlServerConnectionStorePathResolution.Source"/> so
/// a presentation layer can select its own localized message instead of using the
/// English <see cref="SqlServerConnectionStorePathResolution.ErrorMessage"/>.
/// </para>
/// </remarks>
public enum SqlServerConnectionStorePathError
{
    /// <summary>No error; the resolution succeeded.</summary>
    None = 0,

    /// <summary>The source supplied a value, but it was empty or whitespace.</summary>
    /// <remarks>
    /// A source that supplies nothing at all is skipped rather than failed. Only a
    /// present-but-blank value produces this code, so an environment variable set to an
    /// empty string is a configuration error and not the same as an unset variable.
    /// </remarks>
    Blank = 1,

    /// <summary>The value could not be normalized to an absolute path.</summary>
    /// <remarks>Illegal characters, a malformed root, or a path too long for the platform.</remarks>
    Malformed = 2,

    /// <summary>The value has no file-name component, so it names a directory.</summary>
    /// <remarks>
    /// Detected syntactically: the normalized path ends in a directory separator or is a
    /// bare root. A path that happens to name an existing directory without saying so —
    /// <c>.</c>, for example — is not detectable here and is not treated as an error.
    /// </remarks>
    MissingFileName = 3
}
