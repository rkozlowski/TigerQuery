using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

/// <summary>
/// Identifies a lexical element returned by <see cref="SqlCmdParser.ReadElement"/>.
/// </summary>
public enum SqlElementKind
{
    /// <summary>Undelimited SQL text.</summary>
    Text,

    /// <summary>A <c>--</c> comment, including its line ending when present.</summary>
    SingleLineComment,

    /// <summary>A possibly nested <c>/* ... */</c> comment.</summary>
    MultiLineComment,

    /// <summary>A single-quoted string, including delimiters and escaped quotes.</summary>
    SingleQuotedString,

    /// <summary>A double-quoted string or identifier, including delimiters.</summary>
    DoubleQuotedString,

    /// <summary>A bracket-delimited identifier, including delimiters.</summary>
    BracketedIdentifier,

    /// <summary>A marker indicating that a text element ended at a line boundary.</summary>
    EndOfLine,

    /// <summary>A marker indicating that a text element ended at the end of input.</summary>
    EndOfStream,

    /// <summary>No specific element or terminating condition is known.</summary>
    Unknown
}
