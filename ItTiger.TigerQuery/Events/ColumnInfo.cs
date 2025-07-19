using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

public sealed class ColumnInfo
{
    public string Name { get; init; } = "";
    public Type ClrType { get; init; } = typeof(object);
    public string SqlTypeName { get; init; } = ""; // e.g. nvarchar, int
    public int? Precision { get; init; }

    public int? Scale { get; init; }

    public int? Length { get; init; }

    public bool? IsNullable { get; init; }
}
