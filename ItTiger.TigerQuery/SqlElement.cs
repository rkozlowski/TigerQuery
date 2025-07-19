using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

public sealed class SqlElement
{
    public SqlElementKind Kind { get; set; } = SqlElementKind.Text;
    public string Text { get; set; } = string.Empty; // raw text including delimiters
    public int Line { get; set; } = -1;
    public int Column { get; set; } = -1;

    public SqlElementKind EndedBy { get; set; } = SqlElementKind.Unknown;

    public string InnerText
    {
        get 
        { 
            if (Kind is SqlElementKind.BracketedIdentifier or SqlElementKind.SingleQuotedString or SqlElementKind.DoubleQuotedString)
            {
                var len = Text.Length;
                if (len < 2)
                    throw new TigerQueryException("Text is too short");
                var endChar = Text[len - 1];
                var text = Text.Substring(1, len - 2).Replace($"{endChar}{endChar}", $"{endChar}");
                return text;
            }
            return Text;
        }
    }

    public SqlElement(SqlElementKind kind, string text, int line, int column, SqlElementKind endedBy = SqlElementKind.Unknown)
    {
        Text = text;
        Kind = kind;
        Line = line;
        Column = column;
        EndedBy = endedBy;
    }

    
}
