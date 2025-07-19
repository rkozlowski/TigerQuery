using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerSqlCmd;

public static class SqlCmdVariableParser
{
    public static IDictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? pendingKey = null;

        foreach (var arg in args)
        {
            if (pendingKey != null)
            {
                result[pendingKey] = arg;
                pendingKey = null;
                continue;
            }

            if (arg.Contains('='))
            {
                var parts = arg.Split('=', 2);
                result[parts[0]] = parts[1];
            }
            else
            {
                pendingKey = arg;
            }
        }

        if (pendingKey != null)
            throw new InvalidOperationException($"Missing value for SQLCMD variable '{pendingKey}'");

        return result;
    }
}
