using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

public sealed class SqlBatch
{
    public string Text { get; init; } = default!;
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    // Maybe: public string? SourceFile;
    public int ExecCount { get; init; } = 1;
}
