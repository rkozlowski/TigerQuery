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

/// <summary>
/// Incrementally tokenizes and parses SQL scripts with optional sqlcmd directives.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReadBatchesAsync"/> recognizes <c>GO</c> separators in every
/// <see cref="SqlCmdMode"/>. In sqlcmd modes it also expands <c>$(name)</c>,
/// processes <c>:setvar</c>, and applies <c>:ON ERROR EXIT</c> or
/// <c>:ON ERROR IGNORE</c> to the shared execution context.
/// </para>
/// <para>
/// In sqlcmd modes the parser also recognizes and validates the <c>:Out</c> and
/// <c>:Error</c> output directives, which previously failed as unknown commands.
/// They are represented internally as ordered execution steps that only
/// <see cref="Engine.TigerQueryEngine"/> consumes; <see cref="ReadBatchesAsync"/>
/// projects them away and still returns batches only.
/// </para>
/// <para>
/// Parsing validates TigerQuery/sqlcmd structure only. It does not validate T-SQL.
/// The parser consumes its <see cref="TextReader"/> and is not thread-safe.
/// </para>
/// </remarks>
public sealed class SqlCmdParser
{
    private readonly TextReader _reader;
    private int _line = 1;
    private int _column = 1;
    private readonly TigerQueryEngineOptions _options;
    private readonly QueryExecutionContext _context;




    /// <summary>Initializes a parser over a text reader and execution context.</summary>
    /// <param name="reader">The script source. The parser does not dispose it.</param>
    /// <param name="options">The parsing and variable options.</param>
    /// <param name="context">
    /// The mutable context that receives variable and <c>:ON ERROR</c> state.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="reader"/>, <paramref name="options"/>, or
    /// <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
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

    /// <summary>Reads the next lexical element from the script.</summary>
    /// <returns>The next element, or <see langword="null"/> at end of input.</returns>
    /// <exception cref="TigerQueryException">
    /// A quoted section or multiline comment is unterminated, or parser state is invalid.
    /// </exception>
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

    private static bool IsSingleVariableReference(string value)
    {
        return value.Length > 3
            && value.StartsWith("$(", StringComparison.Ordinal)
            && value.EndsWith(')')
            && value.IndexOf(')', 2) == value.Length - 1;
    }

    private static bool IsValidQuotedArgumentTerminator(SqlElement element)
    {
        if (element.Kind == SqlElementKind.SingleLineComment)
        {
            return true;
        }

        return element.Kind == SqlElementKind.Text
            && string.IsNullOrWhiteSpace(element.Text)
            && element.EndedBy is SqlElementKind.EndOfLine
                or SqlElementKind.EndOfStream
                or SqlElementKind.SingleLineComment;
    }
    
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

    private static bool IsOutputDirective(string command)
    {
        return command.Equals(":OUT", StringComparison.OrdinalIgnoreCase)
            || command.Equals(":ERROR", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the single filename argument of an <c>:Out</c> or <c>:Error</c> directive.
    /// </summary>
    /// <returns>
    /// The expanded filename and, when the quoted form consumed one, the element that
    /// terminated it and must be handed back to the caller's element loop.
    /// </returns>
    private async Task<(string Path, SqlElement? PendingElement)> ReadOutputDirectivePathAsync(
        string command,
        SqlElement element,
        string[] parts,
        CancellationToken cancellationToken)
    {
        var endedBy = element.EndedBy;
        var isTerminated = endedBy is SqlElementKind.EndOfLine
            or SqlElementKind.EndOfStream
            or SqlElementKind.SingleLineComment;

        // :Out results.csv
        if (parts.Length == 2 && isTerminated)
        {
            return (_context.ExpandVariables(parts[1]), null);
        }

        // :Out "directory with spaces/results.csv"
        if (parts.Length == 1 && endedBy == SqlElementKind.DoubleQuotedString)
        {
            var value = await Task.Run(ReadElement, cancellationToken);
            if (value is null || value.Kind != SqlElementKind.DoubleQuotedString)
            {
                _options.Logger?.Log(
                    LogLevel.Debug,
                    "{Command} parsing failed. Argument element kind: {kind}.",
                    command,
                    value?.Kind ?? SqlElementKind.Unknown);
                throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
            }

            var quotedPath = value.InnerText;
            if (string.IsNullOrWhiteSpace(quotedPath))
            {
                _options.Logger?.Log(LogLevel.Debug, "{Command} parsing failed. Empty quoted path.", command);
                throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
            }

            SqlElement? pendingElement = null;
            var terminator = await Task.Run(ReadElement, cancellationToken);
            if (terminator is not null)
            {
                if (!IsValidQuotedArgumentTerminator(terminator))
                {
                    _options.Logger?.Log(
                        LogLevel.Debug,
                        "{Command} parsing failed. Quoted path terminator kind: {kind}, ended by: {endedBy}.",
                        command,
                        terminator.Kind,
                        terminator.EndedBy);
                    throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
                }

                pendingElement = terminator;
            }

            return (_context.ExpandVariables(quotedPath), pendingElement);
        }

        _options.Logger?.Log(
            LogLevel.Debug,
            "{Command} parsing failed. Parts.Length: {len}. Ended by: {endedBy}",
            command,
            parts.Length,
            endedBy);
        throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
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



    /// <summary>Asynchronously yields logical SQL batches in source order.</summary>
    /// <param name="cancellationToken">A token observed throughout parser enumeration.</param>
    /// <returns>An asynchronous stream of parser-produced batches.</returns>
    /// <remarks>
    /// <para>
    /// Variables are expanded as text is parsed. Undefined ordinary variables
    /// remain as their literal <c>$(name)</c> references. A variable used as a
    /// <c>GO</c> count must expand to one valid <see cref="int"/> value.
    /// </para>
    /// <para>
    /// A directive after buffered SQL and before its terminating <c>GO</c> updates
    /// the context before that batch is yielded. Consumers that defer execution
    /// must therefore snapshot relevant context state for each yielded batch.
    /// </para>
    /// <para>
    /// Recognized <c>:Out</c> and <c>:Error</c> directives are validated but not
    /// returned by this method. Consumers that need output routing must execute
    /// through <see cref="Engine.TigerQueryEngine"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> is cancelled during enumeration.
    /// </exception>
    /// <exception cref="TigerQueryException">
    /// A sqlcmd directive, <c>GO</c> count, quoted section, or comment is malformed.
    /// </exception>
    public async IAsyncEnumerable<SqlBatch> ReadBatchesAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var step in ReadExecutionStepsAsync(cancellationToken))
        {
            if (step is ExecuteBatchStep batchStep)
            {
                yield return batchStep.Execution.Batch;
            }
        }
    }

    /// <summary>
    /// Asynchronously yields the ordered execution steps of the script.
    /// </summary>
    /// <param name="cancellationToken">A token observed throughout parser enumeration.</param>
    /// <returns>An asynchronous stream of batch and output-route steps in source order.</returns>
    /// <remarks>
    /// <para>
    /// This is the engine's authoritative execution representation.
    /// <see cref="ReadBatchesAsync"/> is a projection of this stream that keeps the
    /// same syntax validation while dropping route steps.
    /// </para>
    /// <para>
    /// A route step is emitted where its directive appears. Because a batch step is
    /// emitted only at its terminating <c>GO</c> or at end of input, a directive
    /// written after buffered SQL text is necessarily ordered before that batch, and
    /// consecutive directives keep their source order.
    /// </para>
    /// </remarks>
    internal async IAsyncEnumerable<ExecutionStep> ReadExecutionStepsAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<string> currentLines = [];
        int? batchStartLine = null;
        int? batchStartColumn = null;
        SqlElement? pendingElement = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SqlElement? element;
            if (pendingElement is not null)
            {
                element = pendingElement;
                pendingElement = null;
            }
            else
            {
                element = await Task.Run(ReadElement, cancellationToken);
            }
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
                            if (parts.Length != 2)
                            {
                                throw new TigerQueryException("Incorrect syntax was encountered while parsing GO.");
                            }

                            var countStr = parts[1];
                            if (_options.Mode != SqlCmdMode.Normal)
                            {
                                if (countStr.Contains("$(", StringComparison.Ordinal)
                                    && !IsSingleVariableReference(countStr))
                                {
                                    throw new TigerQueryException("Incorrect syntax was encountered while parsing GO.");
                                }
                                countStr = _context.ExpandVariables(countStr);
                            }
                            if (!int.TryParse(countStr, out execCount))
                            {
                                throw new TigerQueryException("Incorrect syntax was encountered while parsing GO.");
                            }
                        }

                        if (currentLines.Count > 0)
                        {
                            yield return new ExecuteBatchStep(new ExecutionBatch(
                                new SqlBatch
                                {
                                    Text = string.Concat(currentLines),
                                    StartLine = batchStartLine ?? element.Line,
                                    StartColumn = batchStartColumn ?? element.Column,
                                    ExecCount = execCount
                                },
                                _context.ContinueOnError));
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

                                var terminator = await Task.Run(ReadElement, cancellationToken);
                                if (terminator is not null)
                                {
                                    if (!IsValidQuotedArgumentTerminator(terminator))
                                    {
                                        _options.Logger?.Log(
                                            LogLevel.Debug,
                                            ":SETVAR parsing failed. Quoted value terminator kind: {kind}, ended by: {endedBy}.",
                                            terminator.Kind,
                                            terminator.EndedBy);
                                        throw new TigerQueryException($"Incorrect syntax was encountered while parsing {command}.");
                                    }
                                    pendingElement = terminator;
                                }
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
                        else if (IsOutputDirective(command))
                        {
                            if (_options.OutputRouting?.AllowScriptOutputDirectives == false)
                            {
                                _options.Logger?.Log(
                                    LogLevel.Debug,
                                    "{Command} rejected because script output directives are disabled.",
                                    command);
                                throw new TigerQueryException(
                                    $"Script output directives are disabled; {command} is not permitted.");
                            }

                            var (path, terminator) = await ReadOutputDirectivePathAsync(
                                command,
                                element,
                                parts,
                                cancellationToken);
                            if (terminator is not null)
                            {
                                pendingElement = terminator;
                            }

                            var directive = new OutputDirective(path, element.Line, element.Column);
                            yield return command.Equals(":OUT", StringComparison.OrdinalIgnoreCase)
                                ? new SetOutRouteStep(directive)
                                : new SetErrorRouteStep(directive);
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
            yield return new ExecuteBatchStep(new ExecutionBatch(
                new SqlBatch
                {
                    Text = string.Concat(currentLines),
                    StartLine = batchStartLine ?? 1,
                    StartColumn = batchStartColumn ?? 1,
                    ExecCount = 1
                },
                _context.ContinueOnError));
        }
    }

}

