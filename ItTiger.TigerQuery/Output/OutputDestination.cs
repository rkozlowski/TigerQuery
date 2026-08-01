using ItTiger.TigerQuery.Events;
using System.Globalization;
using System.Text;

namespace ItTiger.TigerQuery.Output;

/// <summary>
/// One physical output file owned by the run.
/// </summary>
/// <remarks>
/// The stream is created lazily, on the first payload written, with create/truncate
/// semantics. Merely parsing or applying a directive creates nothing. A destination
/// stays open until the run completes so its byte-order mark, header, and writer
/// state cannot be duplicated by leaving a path and returning to it.
/// </remarks>
internal abstract class OutputDestination : IDisposable
{
    /// <summary>The line terminator written by every destination, on every platform.</summary>
    protected const string LineTerminator = "\r\n";

    private readonly Encoding _encoding;
    private StreamWriter? _writer;
    private bool _disposed;

    protected OutputDestination(string path, OutputChannel channel, Encoding encoding)
    {
        Path = path;
        Channel = channel;
        _encoding = encoding;
    }

    /// <summary>Gets the resolved path of the file.</summary>
    public string Path { get; }

    /// <summary>Gets the channel this file belongs to for the whole run.</summary>
    public OutputChannel Channel { get; }

    /// <summary>Gets whether the file has actually been created.</summary>
    public bool IsOpen => _writer is not null;

    /// <exception cref="OutputRoutingException">The file could not be created.</exception>
    private StreamWriter Writer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_writer is not null)
            {
                return _writer;
            }

            try
            {
                var stream = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream, _encoding) { AutoFlush = false };
            }
            catch (Exception ex) when (IsOutputFailure(ex))
            {
                throw Failure("could not be created", ex);
            }

            return _writer;
        }
    }

    /// <exception cref="OutputRoutingException">The text could not be written.</exception>
    protected void WriteText(string text)
    {
        var writer = Writer;
        try
        {
            writer.Write(text);
        }
        catch (Exception ex) when (IsOutputFailure(ex))
        {
            throw Failure("could not be written", ex);
        }
    }

    /// <summary>Flushes buffered text to the file, if the file was created.</summary>
    /// <exception cref="OutputRoutingException">The flush failed.</exception>
    public void Flush()
    {
        if (_writer is null || _disposed)
        {
            return;
        }

        try
        {
            _writer.Flush();
        }
        catch (Exception ex) when (IsOutputFailure(ex))
        {
            throw Failure("could not be flushed", ex);
        }
    }

    /// <summary>Flushes and closes the file, if it was created.</summary>
    /// <exception cref="OutputRoutingException">The flush or close failed.</exception>
    public void Close()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var writer = _writer;
        _writer = null;
        if (writer is null)
        {
            return;
        }

        try
        {
            writer.Dispose();
        }
        catch (Exception ex) when (IsOutputFailure(ex))
        {
            throw Failure("could not be closed", ex);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Close();
        }
        catch (OutputRoutingException)
        {
            // The registry surfaces cleanup failures; the safety-net path must not throw.
        }
    }

    protected OutputRoutingException Failure(string what, Exception? inner = null)
    {
        var message = string.Format(
            CultureInfo.InvariantCulture,
            "The output file '{0}' {1}.",
            Path,
            what);

        return inner is null
            ? new OutputRoutingException(message, Path)
            : new OutputRoutingException(message, Path, inner);
    }

    /// <summary>
    /// Identifies the failures that become <see cref="OutputRoutingException"/>
    /// rather than escaping as infrastructure exceptions.
    /// </summary>
    /// <remarks>
    /// <see cref="ArgumentException"/> covers <see cref="EncoderFallbackException"/>,
    /// which is how the strict encoding reports an unencodable character instead of
    /// writing a replacement.
    /// </remarks>
    private static bool IsOutputFailure(Exception exception) => exception
        is IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException
        or NotSupportedException
        or ArgumentException
        or ObjectDisposedException;
}

/// <summary>A plain text file holding routed message text.</summary>
/// <remarks>
/// Message files are not CSV. Each message's text is written verbatim in event
/// order and terminated with CRLF; embedded line endings are preserved and no
/// markup, colors, timestamps, or prefixes are added.
/// </remarks>
internal sealed class TextOutputDestination : OutputDestination
{
    public TextOutputDestination(string path, OutputChannel channel, Encoding encoding)
        : base(path, channel, encoding)
    {
    }

    public void WriteMessage(string text)
    {
        WriteText(text);
        WriteText(LineTerminator);
    }
}

/// <summary>A CSV file holding routed result sets.</summary>
internal sealed class ResultSetOutputDestination : OutputDestination
{
    private readonly StringBuilder _builder = new();
    private string[]? _schema;

    public ResultSetOutputDestination(string path, Encoding encoding)
        : base(path, OutputChannel.ResultSets, encoding)
    {
    }

    /// <summary>
    /// Appends one result set, writing the header only for the first one.
    /// </summary>
    /// <remarks>
    /// A zero-column result is not a CSV result set: it writes nothing and does not
    /// create the file. A result set with columns and no rows writes its header.
    /// </remarks>
    /// <exception cref="OutputRoutingException">
    /// The schema is incompatible with the established one, or the file could not be
    /// created or written.
    /// </exception>
    public void Write(ResultSetInfo result)
    {
        if (result.Columns.Count == 0)
        {
            return;
        }

        var names = new string[result.Columns.Count];
        for (var index = 0; index < names.Length; index++)
        {
            names[index] = result.Columns[index].Name;
        }

        if (_schema is null)
        {
            WriteText(BuildRecord(names));
            _schema = names;
        }
        else
        {
            // Validated before any byte of this result set reaches the file.
            RequireCompatibleSchema(names);
        }

        foreach (var row in result.Rows)
        {
            WriteText(BuildRow(row, names.Length));
        }

        Flush();
    }

    private void RequireCompatibleSchema(string[] names)
    {
        var schema = _schema!;
        var compatible = schema.Length == names.Length;
        if (compatible)
        {
            for (var index = 0; index < schema.Length; index++)
            {
                if (!string.Equals(schema[index], names[index], StringComparison.Ordinal))
                {
                    compatible = false;
                    break;
                }
            }
        }

        if (compatible)
        {
            return;
        }

        throw new OutputRoutingException(
            string.Format(
                CultureInfo.InvariantCulture,
                "The result set has {0} column(s) that do not match the {1} column(s) already written to '{2}'. "
                + "A single CSV file requires the same column names in the same ordinal positions.",
                names.Length,
                schema.Length,
                Path),
            Path);
    }

    private string BuildRecord(string[] fields)
    {
        _builder.Clear();
        for (var index = 0; index < fields.Length; index++)
        {
            if (index > 0)
            {
                _builder.Append(CsvFormatter.Delimiter);
            }

            CsvFormatter.AppendField(_builder, fields[index]);
        }

        _builder.Append(CsvFormatter.RecordTerminator);
        return _builder.ToString();
    }

    private string BuildRow(object?[]? row, int columnCount)
    {
        _builder.Clear();
        for (var index = 0; index < columnCount; index++)
        {
            if (index > 0)
            {
                _builder.Append(CsvFormatter.Delimiter);
            }

            var value = row is not null && index < row.Length ? row[index] : null;
            CsvFormatter.AppendField(_builder, CsvFormatter.FormatValue(value));
        }

        _builder.Append(CsvFormatter.RecordTerminator);
        return _builder.ToString();
    }
}
