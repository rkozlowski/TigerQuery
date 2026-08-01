using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Events;

namespace ItTiger.TigerQuery.Output;

/// <summary>
/// The run-scoped routing state machine. It decides, for every payload, whether the
/// application callback or a file destination receives it.
/// </summary>
/// <remarks>
/// <para>
/// Precedence is: application callbacks by default; application-supplied initial
/// paths at run start; each script directive from its effective position onward; and
/// the latest directive for a channel wins. <c>:Out</c> never changes the error
/// route and <c>:Error</c> never changes the result-set or normal-message routes.
/// </para>
/// <para>
/// The router is not thread-safe and belongs to exactly one run.
/// </para>
/// </remarks>
internal sealed class OutputRouter : IDisposable
{
    private readonly TigerQueryEngineOptions _options;
    private readonly OutputRoutingConfiguration _configuration;
    private readonly OutputDestinationRegistry _registry;

    private string? _resultSetPath;
    private string? _normalMessagePath;
    private string? _errorMessagePath;
    private OutputRoutingException? _pendingFailure;

    /// <exception cref="OutputRoutingException">An initial path is invalid or collides.</exception>
    public OutputRouter(TigerQueryEngineOptions options, OutputRoutingConfiguration configuration)
    {
        _options = options;
        _configuration = configuration;
        _registry = new OutputDestinationRegistry(configuration.Encoding);

        if (configuration.InitialOutPath is not null)
        {
            ApplyOutDirective(configuration.InitialOutPath);
        }

        if (configuration.InitialErrorPath is not null)
        {
            ApplyErrorDirective(configuration.InitialErrorPath);
        }
    }

    /// <summary>Gets whether any channel is currently routed to a file.</summary>
    public bool HasFileRoute =>
        _resultSetPath is not null || _normalMessagePath is not null || _errorMessagePath is not null;

    /// <summary>
    /// Points the result-set channel — and, under
    /// <see cref="OutDirectiveBehavior.ResultSetsAndNormalMessages"/>, the
    /// normal-message channel — at <paramref name="path"/>.
    /// </summary>
    /// <exception cref="OutputRoutingException">
    /// The path cannot be resolved or collides with another channel's destination.
    /// </exception>
    public void ApplyOutDirective(string path)
    {
        var resolved = OutputPathResolver.Resolve(_configuration.BaseDirectory, path);
        _registry.Reserve(resolved, OutputChannel.ResultSets);

        string? companion = null;
        if (_configuration.OutBehavior == OutDirectiveBehavior.ResultSetsAndNormalMessages)
        {
            companion = OutputPathResolver.GetMessageCompanionPath(resolved);
            _registry.Reserve(companion, OutputChannel.NormalMessages);
        }

        // Applied only after every reservation succeeded, so a rejected directive
        // leaves the previous routes intact.
        _resultSetPath = resolved;
        if (companion is not null)
        {
            _normalMessagePath = companion;
        }
    }

    /// <summary>Points the error-message channel at <paramref name="path"/>.</summary>
    /// <exception cref="OutputRoutingException">
    /// The path cannot be resolved or collides with another channel's destination.
    /// </exception>
    public void ApplyErrorDirective(string path)
    {
        var resolved = OutputPathResolver.Resolve(_configuration.BaseDirectory, path);
        _registry.Reserve(resolved, OutputChannel.ErrorMessages);
        _errorMessagePath = resolved;
    }

    /// <summary>Delivers one materialized result set to its current destination.</summary>
    /// <remarks>
    /// The result-set callback is invoked only while the channel is routed to the
    /// application. Once redirected, a zero-column result produces neither a file nor
    /// a callback, but its coordinates remain consumed.
    /// </remarks>
    /// <exception cref="OutputRoutingException">Serialization or writing failed.</exception>
    public void RouteResultSet(ResultSetInfo result)
    {
        if (_resultSetPath is null)
        {
            _options.OnResultSet?.Invoke(result);
            return;
        }

        if (result.Columns.Count == 0)
        {
            return;
        }

        var path = _configuration.FileMode == ResultSetFileMode.SingleFile
            ? _resultSetPath
            : ResultSetFileNaming.BuildPath(
                _resultSetPath,
                _configuration.DefaultExtension,
                result.BatchNumber,
                result.ExecutionIndex,
                result.ResultSetIndex);

        _registry.GetResultSetDestination(path).Write(result);
    }

    /// <summary>Delivers one message to its current destination.</summary>
    /// <remarks>
    /// Provider message events are synchronous, so a write failure here is captured
    /// rather than thrown; the coordinator rethrows it at the next safe boundary.
    /// Only <see cref="MessageOrigin.ServerDiagnostic"/> messages can reach a file.
    /// </remarks>
    public void RouteMessage(SqlCmdMessage message, bool isException, MessageOrigin origin)
    {
        var path = origin == MessageOrigin.ServerDiagnostic
            ? (message.IsError ? _errorMessagePath : _normalMessagePath)
            : null;

        if (path is null)
        {
            _options.OnMessage?.Invoke(message, isException);
            return;
        }

        var channel = message.IsError ? OutputChannel.ErrorMessages : OutputChannel.NormalMessages;
        try
        {
            _registry.GetTextDestination(path, channel).WriteMessage(message.Text);
        }
        catch (OutputRoutingException ex)
        {
            Capture(ex);
        }
    }

    /// <summary>Flushes message text and any other buffered output at a batch boundary.</summary>
    public void FlushAtBatchBoundary()
    {
        try
        {
            _registry.FlushAll();
        }
        catch (OutputRoutingException ex)
        {
            Capture(ex);
        }
    }

    /// <summary>Rethrows a captured failure at a safe coordinator boundary.</summary>
    /// <exception cref="OutputRoutingException">A failure was captured earlier.</exception>
    public void ThrowIfFailed()
    {
        var failure = TakePendingFailure();
        if (failure is not null)
        {
            throw failure;
        }
    }

    /// <summary>Returns and clears the first captured failure, if any.</summary>
    public OutputRoutingException? TakePendingFailure()
    {
        var failure = _pendingFailure;
        _pendingFailure = null;
        return failure;
    }

    /// <summary>
    /// Flushes and closes every destination and returns the first cleanup failure.
    /// </summary>
    /// <remarks>
    /// Called on success, failure, and cancellation alike. A failure captured earlier
    /// takes precedence over a cleanup failure.
    /// </remarks>
    public OutputRoutingException? Complete()
    {
        var pending = TakePendingFailure();
        var cleanup = _registry.Close();
        return pending ?? cleanup;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _registry.Dispose();
    }

    private void Capture(OutputRoutingException failure)
    {
        // The first failure is the primary cause; later ones are consequences of it.
        _pendingFailure ??= failure;
    }
}
