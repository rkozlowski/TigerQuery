using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

internal static class VariableExpansionHelper
{
    public static string Expand(string input, Func<string, string?> getValue)
    {
        var sb = new StringBuilder();
        int i = 0;

        while (i < input.Length)
        {
            if (input[i] == '$' && i + 1 < input.Length && input[i + 1] == '(')
            {
                int start = i + 2;
                int end = input.IndexOf(')', start);
                if (end > start)
                {
                    var varName = input.Substring(start, end - start);
                    var value = getValue(varName) ?? $"$({varName})";
                    sb.Append(value);
                    i = end + 1;
                    continue;
                }
            }

            sb.Append(input[i]);
            i++;
        }

        return sb.ToString();
    }
}
