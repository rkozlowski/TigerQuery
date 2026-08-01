namespace ItTiger.TigerQuery.Engine;

/// <summary>
/// One ordered unit of work produced by <see cref="SqlCmdParser"/> and consumed by
/// <see cref="TigerQueryEngine"/> in both streaming and prepared execution.
/// </summary>
/// <remarks>
/// The step stream is the engine's authoritative execution representation. It keeps
/// every output directive in source order instead of collapsing directives into
/// final parser state, which is what makes repeated routes to the same path
/// observable. The type is deliberately internal; no public script-step API exists.
/// </remarks>
internal abstract record ExecutionStep;

/// <summary>Executes one logical batch with the <c>:ON ERROR</c> policy captured for it.</summary>
internal sealed record ExecuteBatchStep(ExecutionBatch Execution) : ExecutionStep;

/// <summary>Applies an <c>:Out</c> directive at its source position.</summary>
internal sealed record SetOutRouteStep(OutputDirective Directive) : ExecutionStep;

/// <summary>Applies an <c>:Error</c> directive at its source position.</summary>
internal sealed record SetErrorRouteStep(OutputDirective Directive) : ExecutionStep;

/// <summary>
/// The parsed argument of one <c>:Out</c> or <c>:Error</c> directive.
/// </summary>
/// <param name="Path">
/// The filename exactly as written after quote unescaping and variable expansion.
/// It is not resolved, canonicalized, or probed by the parser.
/// </param>
/// <param name="Line">The one-based source line of the directive.</param>
/// <param name="Column">The one-based source column of the directive.</param>
internal sealed record OutputDirective(string Path, int Line, int Column);
