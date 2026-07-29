namespace ItTiger.TigerQuery;
/// <summary>
/// Selects which sqlcmd language features the parser recognizes.
/// </summary>
/// <remarks>
/// All modes recognize <c>GO</c> batch separators and literal repeat counts.
/// Sqlcmd directives and variable expansion are enabled only in
/// <see cref="SqlCmd"/> and <see cref="SqlCmdEx"/>.
/// </remarks>
public enum SqlCmdMode
{
    /// <summary>
    /// Parses SQL batches without interpreting sqlcmd directives or expanding variables.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Enables <c>:setvar</c>, <c>:ON ERROR</c>, and <c>$(name)</c> expansion;
    /// script assignments may replace variables supplied programmatically.
    /// </summary>
    SqlCmd = 1,

    /// <summary>
    /// Enables sqlcmd features while preventing <c>:setvar</c> from replacing
    /// variables supplied through <see cref="Engine.TigerQueryEngineOptions.Variables"/>.
    /// </summary>
    SqlCmdEx = 2
}

