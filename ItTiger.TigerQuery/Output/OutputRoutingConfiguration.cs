using ItTiger.TigerQuery.Engine;
using System.Text;

namespace ItTiger.TigerQuery.Output;

/// <summary>
/// The validated, run-scoped form of <see cref="OutputRoutingOptions"/>.
/// </summary>
/// <remarks>
/// Building this object is the run-start configuration check. It resolves the base
/// directory once, strengthens the encoding, and rejects undefined enum values
/// before parsing, connection opening, SQL execution, or output-file creation.
/// </remarks>
internal sealed class OutputRoutingConfiguration
{
    private OutputRoutingConfiguration(
        string baseDirectory,
        Encoding encoding,
        OutDirectiveBehavior outBehavior,
        ResultSetFileMode fileMode,
        ResultSetOutputFormat format,
        bool allowScriptOutputDirectives,
        string? initialOutPath,
        string? initialErrorPath)
    {
        BaseDirectory = baseDirectory;
        Encoding = encoding;
        OutBehavior = outBehavior;
        FileMode = fileMode;
        Format = format;
        AllowScriptOutputDirectives = allowScriptOutputDirectives;
        InitialOutPath = initialOutPath;
        InitialErrorPath = initialErrorPath;
    }

    public string BaseDirectory { get; }

    public Encoding Encoding { get; }

    public OutDirectiveBehavior OutBehavior { get; }

    public ResultSetFileMode FileMode { get; }

    public ResultSetOutputFormat Format { get; }

    public bool AllowScriptOutputDirectives { get; }

    public string? InitialOutPath { get; }

    public string? InitialErrorPath { get; }

    /// <summary>Gets the default extension of the selected built-in format.</summary>
    public string DefaultExtension => Format switch
    {
        ResultSetOutputFormat.Csv => ".csv",
        _ => ".csv"
    };

    /// <summary>
    /// Validates <paramref name="options"/> and captures the run's base directory
    /// and encoding.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// An enum value is undefined, the encoding cannot be used with exception
    /// fallbacks, or the base directory cannot be resolved.
    /// </exception>
    public static OutputRoutingConfiguration Create(OutputRoutingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RequireDefined(options.OutBehavior, nameof(OutputRoutingOptions.OutBehavior));
        RequireDefined(options.ResultSetFileMode, nameof(OutputRoutingOptions.ResultSetFileMode));
        RequireDefined(options.ResultSetFormat, nameof(OutputRoutingOptions.ResultSetFormat));

        return new OutputRoutingConfiguration(
            ResolveBaseDirectory(options.BaseDirectory),
            CreateStrictEncoding(options.FileEncoding),
            options.OutBehavior,
            options.ResultSetFileMode,
            options.ResultSetFormat,
            options.AllowScriptOutputDirectives,
            NormalizeInitialPath(options.InitialOutPath, nameof(OutputRoutingOptions.InitialOutPath)),
            NormalizeInitialPath(options.InitialErrorPath, nameof(OutputRoutingOptions.InitialErrorPath)));
    }

    private static void RequireDefined<TEnum>(TEnum value, string name)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                $"{nameof(TigerQueryEngineOptions.OutputRouting)}.{name}",
                value,
                $"'{value}' is not a defined {typeof(TEnum).Name} value.");
        }
    }

    private static string? NormalizeInitialPath(string? path, string name)
    {
        if (path is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "An initial output path must not be empty or whitespace.",
                $"{nameof(TigerQueryEngineOptions.OutputRouting)}.{name}");
        }

        return path;
    }

    private static string ResolveBaseDirectory(string? baseDirectory)
    {
        var candidate = baseDirectory;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = Environment.CurrentDirectory;
        }

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or IOException
            or System.Security.SecurityException)
        {
            throw new ArgumentException(
                "The output base directory could not be resolved.",
                $"{nameof(TigerQueryEngineOptions.OutputRouting)}.{nameof(OutputRoutingOptions.BaseDirectory)}",
                ex);
        }
    }

    /// <summary>
    /// Returns the encoding to write with, configured so an unencodable character
    /// fails the run instead of producing a replacement character.
    /// </summary>
    /// <remarks>
    /// A supplied encoding keeps its byte-order-mark preference; only its fallbacks
    /// are strengthened. An encoding that cannot be made strict is rejected.
    /// </remarks>
    private static Encoding CreateStrictEncoding(Encoding? encoding)
    {
        if (encoding is null)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        }

        Encoding strict;
        try
        {
            strict = (Encoding)encoding.Clone();
            strict.EncoderFallback = EncoderFallback.ExceptionFallback;
            strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new ArgumentException(
                "The supplied output encoding cannot be configured with exception fallbacks.",
                $"{nameof(TigerQueryEngineOptions.OutputRouting)}.{nameof(OutputRoutingOptions.FileEncoding)}",
                ex);
        }

        if (strict.EncoderFallback is not EncoderExceptionFallback)
        {
            throw new ArgumentException(
                "The supplied output encoding cannot be configured with exception fallbacks.",
                $"{nameof(TigerQueryEngineOptions.OutputRouting)}.{nameof(OutputRoutingOptions.FileEncoding)}");
        }

        return strict;
    }
}
