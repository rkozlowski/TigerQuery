using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

/// <summary>
/// Represents one logical SQL batch produced by <see cref="SqlCmdParser"/>.
/// </summary>
/// <remarks>
/// A <c>GO n</c> separator is represented by one instance whose
/// <see cref="ExecCount"/> is <c>n</c>; the SQL text is not duplicated.
/// </remarks>
public sealed class SqlBatch
{
    /// <summary>Gets the variable-expanded SQL text sent to SQL Server.</summary>
    public string Text { get; init; } = default!;

    /// <summary>Gets the one-based source line at which batch text begins.</summary>
    public int StartLine { get; init; }

    /// <summary>Gets the one-based source column at which batch text begins.</summary>
    public int StartColumn { get; init; }
    // Maybe: public string? SourceFile;

    /// <summary>
    /// Gets the repeat count parsed from the terminating <c>GO</c> command.
    /// </summary>
    /// <remarks>
    /// The default is one. Zero and negative values produce no executions but
    /// still describe a logical batch.
    /// </remarks>
    public int ExecCount { get; init; } = 1;
}
