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
    /// variables supplied programmatically seed the variable table and may be
    /// replaced by script assignments.
    /// </summary>
    SqlCmd = 1,

    /// <summary>
    /// Enables sqlcmd features for applications and automation while preventing
    /// <c>:setvar</c> from replacing variables supplied through
    /// <see cref="Engine.TigerQueryEngineOptions.Variables"/>.
    /// Script-local variables can still be created and updated when their names
    /// do not conflict with protected programmatic variables. Names are matched
    /// case-insensitively.
    /// </summary>
    SqlCmdEx = 2
}

