using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

/// <summary>
/// Represents one lexical element read from a SQL script.
/// </summary>
/// <remarks>
/// Line and column values are one-based source positions. <see cref="Text"/>
/// retains delimiters and escapes exactly as read.
/// </remarks>
public sealed class SqlElement
{
    /// <summary>Gets or sets the element's lexical kind.</summary>
    public SqlElementKind Kind { get; set; } = SqlElementKind.Text;

    /// <summary>Gets or sets the raw source text, including any delimiters.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the one-based source line at which the element begins.</summary>
    public int Line { get; set; } = -1;

    /// <summary>Gets or sets the one-based source column at which the element begins.</summary>
    public int Column { get; set; } = -1;

    /// <summary>Gets or sets the condition that terminated a text element.</summary>
    public SqlElementKind EndedBy { get; set; } = SqlElementKind.Unknown;

    /// <summary>
    /// Gets the content without outer delimiters and with doubled closing
    /// delimiters unescaped for quoted strings and bracketed identifiers.
    /// </summary>
    /// <value>
    /// The unescaped inner content for delimited elements; otherwise
    /// <see cref="Text"/>.
    /// </value>
    /// <exception cref="TigerQueryException">
    /// The element is delimited but its <see cref="Text"/> has fewer than two characters.
    /// </exception>
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

    /// <summary>Initializes a lexical SQL element.</summary>
    /// <param name="kind">The element's lexical kind.</param>
    /// <param name="text">The raw source text, including delimiters.</param>
    /// <param name="line">The one-based source line.</param>
    /// <param name="column">The one-based source column.</param>
    /// <param name="endedBy">The condition that terminated a text element.</param>
    public SqlElement(
        SqlElementKind kind,
        string text,
        int line,
        int column,
        SqlElementKind endedBy = SqlElementKind.Unknown)
    {
        Text = text;
        Kind = kind;
        Line = line;
        Column = column;
        EndedBy = endedBy;
    }

    
}
