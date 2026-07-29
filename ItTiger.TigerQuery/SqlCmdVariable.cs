using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

/// <summary>
/// Holds a case-insensitively named sqlcmd variable and its override policy.
/// </summary>
public sealed class SqlCmdVariable
{
    /// <summary>Gets the variable name without <c>$(</c> and <c>)</c> delimiters.</summary>
    public string Name { get; }

    /// <summary>Gets the current replacement value.</summary>
    public string Value { get; private set; }

    /// <summary>
    /// Gets whether a <c>:setvar</c> command may replace this variable's value.
    /// </summary>
    public bool CanBeOverridden { get; }

    /// <summary>Initializes a sqlcmd variable.</summary>
    /// <param name="name">The variable name without reference delimiters.</param>
    /// <param name="value">The initial replacement value.</param>
    /// <param name="canBeOverridden">
    /// Whether subsequent script assignments may replace <paramref name="value"/>.
    /// </param>
    public SqlCmdVariable(string name, string value, bool canBeOverridden = true)
    {
        Name = name;
        Value = value;
        CanBeOverridden = canBeOverridden;
    }

    /// <summary>Attempts to replace the variable's value.</summary>
    /// <param name="newValue">The proposed replacement value.</param>
    /// <returns>
    /// <see langword="true"/> when the value was replaced; otherwise
    /// <see langword="false"/> because <see cref="CanBeOverridden"/> is false.
    /// </returns>
    public bool TrySet(string newValue)
    {
        if (CanBeOverridden)
        {
            Value = newValue;
            return true;
        }

        return false;
    }
}
