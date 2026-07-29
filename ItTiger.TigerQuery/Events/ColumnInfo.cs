using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Events;

/// <summary>Describes one column in a materialized SQL result set.</summary>
public sealed class ColumnInfo
{
    /// <summary>Gets the column name reported by SQL Server.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the CLR type used by <see cref="Microsoft.Data.SqlClient.SqlDataReader"/>.</summary>
    public Type ClrType { get; init; } = typeof(object);

    /// <summary>Gets the provider SQL type name, such as <c>nvarchar</c> or <c>int</c>.</summary>
    public string SqlTypeName { get; init; } = "";

    /// <summary>Gets numeric precision when reported by the provider.</summary>
    public int? Precision { get; init; }

    /// <summary>Gets numeric scale when reported by the provider.</summary>
    public int? Scale { get; init; }

    /// <summary>Gets the maximum column size when reported by the provider.</summary>
    public int? Length { get; init; }

    /// <summary>Gets whether the column permits database nulls when reported by the provider.</summary>
    public bool? IsNullable { get; init; }
}
