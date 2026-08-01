using System.Globalization;

namespace ItTiger.TigerQuery.Output;

/// <summary>
/// Resolves and compares output paths against one fixed base directory.
/// </summary>
internal static class OutputPathResolver
{
    /// <summary>
    /// Gets the comparer used to decide whether two resolved paths name the same
    /// physical destination.
    /// </summary>
    /// <remarks>
    /// Comparison follows the platform's usual file-system semantics after full-path
    /// resolution: case-insensitive on Windows and macOS, case-sensitive elsewhere.
    /// </remarks>
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>Resolves one script- or host-supplied path to a full path.</summary>
    /// <exception cref="OutputRoutingException">The path is empty or cannot be resolved.</exception>
    public static string Resolve(string baseDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new OutputRoutingException("An output path must not be empty.", path);
        }

        try
        {
            return Path.GetFullPath(path, baseDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or IOException
            or System.Security.SecurityException)
        {
            throw new OutputRoutingException(
                string.Format(CultureInfo.InvariantCulture, "The output path '{0}' could not be resolved.", path),
                path,
                ex);
        }
    }

    /// <summary>
    /// Returns the deterministic normal-message companion for a resolved result path.
    /// </summary>
    /// <remarks>
    /// The suffix is appended to the complete resolved path so the requested result
    /// name stays stable in both <see cref="Engine.ResultSetFileMode"/> values.
    /// </remarks>
    public static string GetMessageCompanionPath(string resolvedResultPath)
    {
        return resolvedResultPath + ".messages.log";
    }
}
