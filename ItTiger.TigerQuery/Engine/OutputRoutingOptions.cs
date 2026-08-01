using System.Text;

namespace ItTiger.TigerQuery.Engine;

/// <summary>
/// Configures TigerQuery's script-directed output routing, file output, and the
/// built-in structured result-set writer.
/// </summary>
/// <remarks>
/// <para>
/// All routing is opt-in. With no initial path and no <c>:Out</c> or <c>:Error</c>
/// directive, result sets and messages continue to reach
/// <see cref="TigerQueryEngineOptions.OnResultSet"/> and
/// <see cref="TigerQueryEngineOptions.OnMessage"/> and no file is created.
/// </para>
/// <para>
/// When a channel is routed to a file its presentation callback is not invoked.
/// This is redirection, not mirroring. Structured logging through
/// <see cref="TigerQueryEngineOptions.Logger"/> is independent of routing and
/// continues for every message.
/// </para>
/// <para>
/// Files are created lazily, on the first payload written to a physical
/// destination, using create/truncate semantics on first use in a run. The parent
/// directory must already exist; TigerQuery never creates directories. Output is
/// never appended across runs.
/// </para>
/// </remarks>
public sealed class OutputRoutingOptions
{
    /// <summary>
    /// Gets the initial result-set file, applied at run start before any script
    /// directive.
    /// </summary>
    /// <remarks>
    /// The value follows <see cref="OutBehavior"/> exactly as an <c>:Out</c>
    /// directive does, so it can also redirect normal messages to the companion
    /// file. A relative path is resolved against <see cref="BaseDirectory"/>.
    /// A script <c>:Out</c> directive replaces this route from its position onward.
    /// </remarks>
    public string? InitialOutPath { get; init; }

    /// <summary>
    /// Gets the initial error-message file, applied at run start before any script
    /// directive.
    /// </summary>
    /// <remarks>
    /// A relative path is resolved against <see cref="BaseDirectory"/>. A script
    /// <c>:Error</c> directive replaces this route from its position onward.
    /// </remarks>
    public string? InitialErrorPath { get; init; }

    /// <summary>
    /// Gets the directory that relative output paths are resolved against.
    /// </summary>
    /// <remarks>
    /// The value is captured once at run start. When it is <see langword="null"/>,
    /// <see cref="Environment.CurrentDirectory"/> is captured instead. The same rule
    /// applies to every entry point, including
    /// <see cref="TigerQueryEngine.RunFromFileAsync"/>; a host that wants paths
    /// relative to the script must pass the script directory explicitly.
    /// </remarks>
    public string? BaseDirectory { get; init; }

    /// <summary>Gets which channels an <c>:Out</c> directive redirects.</summary>
    public OutDirectiveBehavior OutBehavior { get; init; }
        = OutDirectiveBehavior.ResultSetsOnly;

    /// <summary>Gets whether routed result sets share one file or get one file each.</summary>
    public ResultSetFileMode ResultSetFileMode { get; init; }
        = Engine.ResultSetFileMode.SingleFile;

    /// <summary>Gets the encoding used for every output file.</summary>
    /// <remarks>
    /// <para>
    /// <see langword="null"/> selects TigerQuery's default of UTF-8 with a
    /// byte-order mark, which is the supported interoperability baseline.
    /// </para>
    /// <para>
    /// A supplied encoding is validated at run start and used with exception
    /// fallbacks, so an unencodable character fails the run instead of being
    /// silently replaced. Its byte-order-mark preference is preserved. CSV-library
    /// and spreadsheet interoperability then depends on that encoding and on
    /// consumer support for it.
    /// </para>
    /// </remarks>
    public Encoding? FileEncoding { get; init; }

    /// <summary>Gets the built-in format used for routed result sets.</summary>
    public ResultSetOutputFormat ResultSetFormat { get; init; }
        = ResultSetOutputFormat.Csv;

    /// <summary>
    /// Gets whether scripts may change output routes with <c>:Out</c> and
    /// <c>:Error</c>.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="true"/>. A service or restricted host can set
    /// it to <see langword="false"/>, which makes an encountered directive a parser
    /// error rather than a silently ignored command.
    /// </remarks>
    public bool AllowScriptOutputDirectives { get; init; } = true;
}

/// <summary>Selects which channels an <c>:Out</c> directive redirects.</summary>
public enum OutDirectiveBehavior
{
    /// <summary>Redirect result sets only; message routes are unchanged.</summary>
    ResultSetsOnly = 0,

    /// <summary>
    /// Redirect result sets and normal messages.
    /// </summary>
    /// <remarks>
    /// CSV cannot safely hold both rows and arbitrary prose, so result sets use the
    /// requested path while normal messages use a deterministic companion text file
    /// formed by appending <c>.messages.log</c> to the complete resolved result
    /// path. Error messages remain controlled only by <c>:Error</c>.
    /// </remarks>
    ResultSetsAndNormalMessages = 1
}

/// <summary>Selects how routed result sets map onto files.</summary>
public enum ResultSetFileMode
{
    /// <summary>
    /// Write every result set routed to a path into that one file.
    /// </summary>
    /// <remarks>
    /// The first result set establishes the header and the required schema. Later
    /// result sets must have the same column count and the same column names in the
    /// same ordinal positions, compared with
    /// <see cref="StringComparison.Ordinal"/>; they append rows only.
    /// </remarks>
    SingleFile = 0,

    /// <summary>
    /// Treat the requested path as a base name and write one file per result set.
    /// </summary>
    /// <remarks>
    /// Generated names use the stable engine coordinates
    /// <c>&lt;stem&gt;_b&lt;batch&gt;_e&lt;execution&gt;_r&lt;result&gt;&lt;extension&gt;</c>,
    /// each component one-based, invariant, and padded to at least four digits.
    /// Each file has its own header and no cross-file schema restriction applies.
    /// </remarks>
    FilePerResultSet = 1
}

/// <summary>Selects the built-in structured writer used for routed result sets.</summary>
public enum ResultSetOutputFormat
{
    /// <summary>
    /// RFC 4180-compatible CSV with a comma delimiter, CRLF records, a header, and
    /// invariant value formatting.
    /// </summary>
    Csv = 0
}
