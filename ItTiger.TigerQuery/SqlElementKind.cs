using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

public enum SqlElementKind
{
    Text,                // Raw SQL text (until next special element)
    SingleLineComment,   // -- comment \n
    MultiLineComment,    // /* ... */ (with nested support)
    SingleQuotedString,  // '...' or multiline with escaped ''
    DoubleQuotedString,  // "..." used in sqlcmd or identifiers
    BracketedIdentifier, // [identifier or special token]

    EndOfLine,           // pseudo element - for indicating how Text element has ended
    EndOfStream,         // pseudo element - for indicating how Text element has ended
    Unknown              // pseudo element - for indicating how Text element has ended
}
