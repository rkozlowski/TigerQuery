using System.Globalization;
using System.Text;

namespace ItTiger.TigerQuery.Output;

/// <summary>
/// Owns every output file opened during one run and enforces that a physical path
/// belongs to exactly one channel.
/// </summary>
/// <remarks>
/// Version one imposes no maximum on open destinations and performs no eviction, so
/// a script that routes to very many distinct paths holds correspondingly many file
/// handles until the run ends.
/// </remarks>
internal sealed class OutputDestinationRegistry : IDisposable
{
    private readonly Encoding _encoding;
    private readonly Dictionary<string, OutputChannel> _channels;
    private readonly Dictionary<string, OutputDestination> _destinations;
    private bool _closed;

    public OutputDestinationRegistry(Encoding encoding)
    {
        _encoding = encoding;
        _channels = new Dictionary<string, OutputChannel>(OutputPathResolver.PathComparer);
        _destinations = new Dictionary<string, OutputDestination>(OutputPathResolver.PathComparer);
    }

    /// <summary>
    /// Binds a resolved path to a channel without creating anything.
    /// </summary>
    /// <exception cref="OutputRoutingException">
    /// The path is already bound to a different channel. A known collision is a
    /// configuration or directive error, not permission to mix payload types.
    /// </exception>
    public void Reserve(string path, OutputChannel channel)
    {
        if (_channels.TryGetValue(path, out var existing))
        {
            if (existing == channel)
            {
                return;
            }

            throw new OutputRoutingException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The output path '{0}' is already used for {1} and cannot also receive {2}.",
                    path,
                    Describe(existing),
                    Describe(channel)),
                path);
        }

        _channels[path] = channel;
    }

    /// <summary>Returns the CSV destination for a resolved path, creating it lazily.</summary>
    public ResultSetOutputDestination GetResultSetDestination(string path)
    {
        Reserve(path, OutputChannel.ResultSets);

        if (_destinations.TryGetValue(path, out var existing))
        {
            return (ResultSetOutputDestination)existing;
        }

        var destination = new ResultSetOutputDestination(path, _encoding);
        _destinations[path] = destination;
        return destination;
    }

    /// <summary>Returns the text destination for a resolved path, creating it lazily.</summary>
    public TextOutputDestination GetTextDestination(string path, OutputChannel channel)
    {
        Reserve(path, channel);

        if (_destinations.TryGetValue(path, out var existing))
        {
            return (TextOutputDestination)existing;
        }

        var destination = new TextOutputDestination(path, channel, _encoding);
        _destinations[path] = destination;
        return destination;
    }

    /// <summary>Flushes every created file.</summary>
    /// <exception cref="OutputRoutingException">A flush failed.</exception>
    public void FlushAll()
    {
        foreach (var destination in _destinations.Values)
        {
            destination.Flush();
        }
    }

    /// <summary>
    /// Flushes and closes every created file and returns the first failure, if any.
    /// </summary>
    /// <remarks>
    /// Every destination is attempted even after a failure, so no handle is leaked
    /// by an earlier error. The first failure is returned rather than thrown so a
    /// caller can keep an existing primary exception.
    /// </remarks>
    public OutputRoutingException? Close()
    {
        if (_closed)
        {
            return null;
        }

        _closed = true;
        OutputRoutingException? first = null;

        foreach (var destination in _destinations.Values)
        {
            try
            {
                destination.Close();
            }
            catch (OutputRoutingException ex)
            {
                first ??= ex;
            }
        }

        return first;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Close();
    }

    private static string Describe(OutputChannel channel) => channel switch
    {
        OutputChannel.ResultSets => "result sets",
        OutputChannel.NormalMessages => "normal messages",
        OutputChannel.ErrorMessages => "error messages",
        _ => channel.ToString()
    };
}
