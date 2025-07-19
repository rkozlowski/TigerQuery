using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

public sealed class SqlCmdVariable
{
    public string Name { get; }
    public string Value { get; private set; }

    /// <summary>
    /// Indicates whether this variable can be modified by a `:setvar` command in the script.
    /// </summary>
    public bool CanBeOverridden { get; }

    public SqlCmdVariable(string name, string value, bool canBeOverridden = true)
    {
        Name = name;
        Value = value;
        CanBeOverridden = canBeOverridden;
    }

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
