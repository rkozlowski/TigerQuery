using ItTiger.TigerQuery.Engine;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery;

public sealed class SqlCmdParser
{
    private readonly TextReader _reader;
    private int _line = 1;
    private int _column = 1;
    private readonly TigerQueryEngineOptions _options;
    private readonly QueryExecutionContext _context;




    public SqlCmdParser(TextReader reader, TigerQueryEngineOptions options, QueryExecutionContext context)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }


    int _unreadChar = -1;

    private int ReadChar()
    {
        int ch;
        if (_unreadChar != -1)
        {
            ch = _unreadChar;
            _unreadChar = -1;
            return ch;
        }
        ch = _reader.Read();
        if (ch != -1)
        {
            UpdateLineAndColumn((char)ch);
        }
        return ch;
    }

    private void UnreadChar(char ch)
    {
        if (_unreadChar != -1)
        {
            throw new TigerQueryException("UnreadChar called too many times!");
        }
        _unreadChar = ch;
    }

    private int PeekChar()
    {
        if (_unreadChar != -1)
        {
            return _unreadChar;
        }
        return _reader.Peek();
    }

    public SqlElement? ReadElement()
    {
        var sb = new StringBuilder();
        int startLine = _line;
        int startCol = _column;

        while (true)
        {            
            int current = ReadChar();
            if (current == -1)
            {
                if (sb.Length > 0)
                {
                    return new SqlElement(SqlElementKind.Text, sb.ToString(), startLine, startCol, SqlElementKind.EndOfStream);
                }
                return null; // End of input
            }
            char ch = (char)current;
            int peek = PeekChar();
            char next = peek != -1 ? (char)peek : '\0';
            if (IsStartOfSpecialElement(ch, next, out SqlElementKind nextElemKind))
            {
                if (sb.Length > 0)
                {
                    UnreadChar(ch);
                    return new SqlElement(SqlElementKind.Text, sb.ToString(), startLine, startCol, nextElemKind);
                }

                //startLine = _line;
                //startCol = _column;
                if (nextElemKind == SqlElementKind.SingleLineComment)
                {
                    // Single-line comment
                    sb.Append(ch);
                    sb.Append((char)ReadChar());

                    while (true)
                    {
                        int c = ReadChar();
                        if (c == -1)
                        {
                            break;
                        }

                        char cur = (char)c;

                        if (cur == '\r')
                        {
                            sb.Append(cur);
                            // Look ahead for \r\n
                            if (PeekChar() == '\n')
                            {
                                sb.Append((char)ReadChar());
                            }
                            break;
                        }
                        else if (cur == '\n')
                        {
                            sb.Append(cur);
                            break;
                        }
                        else
                        {
                            sb.Append(cur);
                        }
                    }

                    return new SqlElement(nextElemKind, sb.ToString(), startLine, startCol);
                }

                if (nextElemKind == SqlElementKind.MultiLineComment)
                {
                    // Multi-line comment (supports nesting)
                    sb.Append(ch);
                    sb.Append((char)ReadChar());

                    int depth = 1;
                    while (depth > 0)
                    {
                        int c = ReadChar();
                        if (c == -1)
                            throw new TigerQueryException("Unexpected end of input inside multiline comment");

                        char cur = (char)c;
                        sb.Append(cur);

                        int pc = PeekChar();
                        char nch = pc == -1 ? '\0' : (char)pc;

                        if (cur == '*' && nch == '/')
                        {
                            sb.Append((char)ReadChar());
                            depth--;
                        }
                        else if (cur == '/' && nch == '*')
                        {
                            sb.Append((char)ReadChar());
                            depth++;
                        }
                    }

                    return new SqlElement(nextElemKind, sb.ToString(), startLine, startCol);
                }

                if (IsDelimitedStartChar(ch))
                {
                    // '...' | "..." | [...]
                    string text = ReadDelimitedInclusive(ch, DelimitedEndChar(ch), allowEscape: true);
                    return new SqlElement(nextElemKind, text, startLine, startCol);
                }
            }
            // not a special sequence...

            // check for '\n', '\r'...
            if (ch == '\n' || ch == '\r')
            {
                sb.Append(ch);
                if (ch == '\r' && next == '\n')
                {
                    sb.Append((char)ReadChar());
                }
                return new SqlElement(SqlElementKind.Text, sb.ToString(), startLine, startCol, SqlElementKind.EndOfLine);
            }
            sb.Append(ch);
        }
    }

    private static bool IsStartOfSpecialElement(char ch, char next, out SqlElementKind kind)
    {
        bool isStartOfSpecialElement = false;
        kind = SqlElementKind.Unknown;
        switch (ch)
        {
            case '\'':
                kind = SqlElementKind.SingleQuotedString;
                isStartOfSpecialElement = true;
                break;
            case '"':
                kind = SqlElementKind.DoubleQuotedString;
                isStartOfSpecialElement = true;
                break;
            case '[':
                kind = SqlElementKind.BracketedIdentifier;
                isStartOfSpecialElement = true;
                break;
            case '-':
                if (next == '-')
                {
                    kind = SqlElementKind.SingleLineComment;
                    isStartOfSpecialElement = true;
                }
                break;
            case '/':
                if (next == '*')
                {
                    kind = SqlElementKind.MultiLineComment;
                    isStartOfSpecialElement = true;
                }                
                break;            
        }
        return isStartOfSpecialElement;
    }


    private static bool IsDelimitedStartChar(char ch) => ch switch
    {
        '\'' or '"' or '[' => true,
        _ => false
    };

    private static char DelimitedEndChar(char ch) => ch switch
    {
        '[' => ']',
        _ => ch
    };
    
    private string ReadDelimitedInclusive(char startChar, char endChar, bool allowEscape)
    {
        var sb = new StringBuilder();
        sb.Append(startChar);

        while (true)
        {
            int peek = PeekChar();
            if (peek == -1)
                throw new TigerQueryException("Unexpected end of input in quoted section.");

            
            char ch = (char)ReadChar();
            sb.Append(ch);

            if (ch == endChar)
            {
                if (allowEscape && (char)PeekChar() == endChar)
                {
                    sb.Append((char)ReadChar());
                    continue;
                }
                break;
            }
        }

        return sb.ToString();
    }

    private void UpdateLineAndColumn(char ch)
    {
        if (ch == '\n')
        {
            _line++;
            _column = 1;
        }
        else if (ch != '\r')
        {
            _column++;
        }
    }



    public async IAsyncEnumerable<SqlBatch> ReadBatchesAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<string> currentLines = [];
        int? batchStartLine = null;
        int? batchStartColumn = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SqlElement? element = await Task.Run(ReadElement, cancellationToken);
            if (element is null)
                break;

            if (element.Kind == SqlElementKind.Text)
            {                
                string trimmed = element.Text.TrimStart();
                var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    if (parts[0].Equals("GO", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!(element.EndedBy is SqlElementKind.EndOfLine or SqlElementKind.EndOfStream or SqlElementKind.SingleLineComment))
                        {
                            throw new TigerQueryException("Incorrect syntax was encountered while parsing GO.");
                        }
                        int execCount = 1;
                        if (parts.Length > 1)
                        {
                            var countStr = parts[1];
                            if (_options.Mode != SqlCmdMode.Normal)
                            {
                                countStr = _context.ExpandVariables(countStr);
                            }
                            if (!int.TryParse(countStr, out execCount))
                            {
                                throw new TigerQueryException("Incorrect syntax was encountered while parsing GO.");
                            }
                        }

                        if (currentLines.Count > 0)
                        {
                            yield return new SqlBatch
                            {
                                Text = string.Concat(currentLines),
                                StartLine = batchStartLine ?? element.Line,
                                StartColumn = batchStartColumn ?? element.Column,
                                ExecCount = execCount
                            };
                            currentLines.Clear();
                            batchStartLine = null;
                            batchStartColumn = null;
                        }

                        continue;
                    }
                    if (parts[0].StartsWith(':') && _options.Mode != SqlCmdMode.Normal)
                    {
                        var command = parts[0];
                        if (command.Equals(":SETVAR", StringComparison.OrdinalIgnoreCase))
                        {                            
                            if (parts.Length < 2)
                            {
                                _options.Logger?.Log(LogLevel.Debug, ":SETVAR parsing failed. Parts.Length: {len}", parts.Length);
                                throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
                            }
                            var varName = parts[1];
                            string? value = null;
                            var endedBy = element.EndedBy;
                            if (parts.Length == 2 && endedBy == SqlElementKind.DoubleQuotedString)
                            {
                                var nextElement = await Task.Run(ReadElement, cancellationToken);
                                if (nextElement == null || nextElement.Kind != SqlElementKind.DoubleQuotedString) 
                                {
                                    _options.Logger?.Log(LogLevel.Debug, ":SETVAR parsing failed. Next element kind: {kind}, ended by: {endedBy}.", 
                                        nextElement?.Kind ?? SqlElementKind.Unknown, nextElement?.EndedBy ?? SqlElementKind.Unknown);
                                    throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
                                }
                                value = nextElement.InnerText;
                            }
                            else if (parts.Length == 3 
                                && (endedBy is SqlElementKind.EndOfLine or SqlElementKind.EndOfStream or SqlElementKind.SingleLineComment))
                            {
                                value = parts[2];
                            }
                            else
                            {
                                _options.Logger?.Log(LogLevel.Debug, ":SETVAR parsing failed. Parts.Length: {len}. Ended by: {endedBy}", 
                                    parts.Length, endedBy);
                                throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
                            }
                            _context.SetVariableFromScript(varName, value);
                        }
                        else
                        {
                            if (!(element.EndedBy is SqlElementKind.EndOfLine or SqlElementKind.EndOfStream or SqlElementKind.SingleLineComment))
                            {
                                throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
                            }
                            if (command.Equals(":ON", StringComparison.OrdinalIgnoreCase))
                            {
                                if (parts.Length == 3 && parts[1].Equals("ERROR", StringComparison.OrdinalIgnoreCase))
                                {
                                    var onError = parts[2];
                                    if (onError.Equals("IGNORE", StringComparison.OrdinalIgnoreCase))
                                    {
                                        _context.ContinueOnError = true;                                        
                                    }
                                    else if (onError.Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                                    {
                                        _context.ContinueOnError = false;
                                    }
                                    else
                                    {
                                        throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
                                    }
                                    _options.Logger?.Log(LogLevel.Debug, "ContinueOnError set to {continue}", _context.ContinueOnError);
                                }
                            }
                            else
                            {
                                throw new TigerQueryException($"Incorrect syntax near ':'.");
                            }
                        }
                        continue;
                    }
                }                
            }

            if (_options.Mode != SqlCmdMode.Normal && !(element.Kind is SqlElementKind.SingleLineComment or SqlElementKind.MultiLineComment))
            {
                element.Text = _context.ExpandVariables(element.Text);
            }

            // Add element to current batch
            if (batchStartLine is null)
            {
                batchStartLine = element.Line;
                batchStartColumn = element.Column;
            }

            currentLines.Add(element.Text);
        }

        // Yield final batch if anything left
        if (currentLines.Count > 0)
        {
            yield return new SqlBatch
            {
                Text = string.Concat(currentLines),
                StartLine = batchStartLine ?? 1,
                StartColumn = batchStartColumn ?? 1,
                ExecCount = 1
            };
        }
    }

}

