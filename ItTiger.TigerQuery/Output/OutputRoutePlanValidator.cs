using ItTiger.TigerQuery.Engine;

namespace ItTiger.TigerQuery.Output;

/// <summary>
/// Replays a prepared plan's route steps against a throwaway router so statically
/// known routing failures are found before the SQL connection is opened.
/// </summary>
/// <remarks>
/// <para>
/// The probe resolves paths and reserves channels but writes nothing, so a prepared
/// script that cannot route creates no output files. Using the real router type
/// keeps the static check and the executed run on exactly one set of rules.
/// </para>
/// <para>
/// Only statically known destinations are covered. In
/// <see cref="ResultSetFileMode.FilePerResultSet"/> the generated per-result names
/// depend on runtime coordinates and are validated when they are first written.
/// </para>
/// </remarks>
internal static class OutputRoutePlanValidator
{
    /// <exception cref="OutputRoutingException">
    /// A directive path cannot be resolved or collides with another channel.
    /// </exception>
    public static void Validate(
        IReadOnlyList<ExecutionStep> steps,
        TigerQueryEngineOptions options,
        OutputRoutingConfiguration configuration)
    {
        using var probe = new OutputRouter(options, configuration);

        foreach (var step in steps)
        {
            switch (step)
            {
                case SetOutRouteStep outStep:
                    probe.ApplyOutDirective(outStep.Directive.Path);
                    break;
                case SetErrorRouteStep errorStep:
                    probe.ApplyErrorDirective(errorStep.Directive.Path);
                    break;
            }
        }
    }
}
