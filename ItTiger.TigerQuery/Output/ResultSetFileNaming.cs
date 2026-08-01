using System.Globalization;

namespace ItTiger.TigerQuery.Output;

/// <summary>
/// Builds the deterministic file names used by
/// <see cref="Engine.ResultSetFileMode.FilePerResultSet"/>.
/// </summary>
/// <remarks>
/// Names come from the globally stable engine coordinates already present on each
/// result set, so they are identical in streaming and prepared execution and are
/// not renumbered by route changes or by a skipped zero-column result.
/// </remarks>
internal static class ResultSetFileNaming
{
    /// <summary>
    /// Returns <c>&lt;stem&gt;_b&lt;batch&gt;_e&lt;execution&gt;_r&lt;result&gt;&lt;extension&gt;</c>
    /// beside <paramref name="basePath"/>.
    /// </summary>
    /// <param name="basePath">The resolved path requested by the route.</param>
    /// <param name="defaultExtension">The format's extension, used when the base has none.</param>
    /// <param name="batchNumber">The one-based logical batch number.</param>
    /// <param name="executionIndex">The one-based repeat iteration.</param>
    /// <param name="resultSetIndex">The one-based result-set number within the execution.</param>
    public static string BuildPath(
        string basePath,
        string defaultExtension,
        int batchNumber,
        int executionIndex,
        int resultSetIndex)
    {
        var directory = Path.GetDirectoryName(basePath);
        var fileName = Path.GetFileName(basePath);
        var extension = Path.GetExtension(fileName);
        var stem = extension.Length > 0
            ? fileName[..^extension.Length]
            : fileName;

        if (extension.Length == 0)
        {
            extension = defaultExtension;
        }

        var generated = string.Concat(
            stem,
            "_b",
            Pad(batchNumber),
            "_e",
            Pad(executionIndex),
            "_r",
            Pad(resultSetIndex),
            extension);

        return string.IsNullOrEmpty(directory)
            ? generated
            : Path.Combine(directory, generated);
    }

    /// <summary>
    /// Formats one coordinate with at least four invariant digits, never truncating
    /// a longer value.
    /// </summary>
    private static string Pad(int value) => value.ToString("D4", CultureInfo.InvariantCulture);
}
