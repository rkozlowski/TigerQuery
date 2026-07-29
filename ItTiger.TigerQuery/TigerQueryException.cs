using System;
using System.Runtime.Serialization;

namespace ItTiger.TigerQuery;

/// <summary>
/// Represents a TigerQuery or sqlcmd structural parsing failure.
/// </summary>
/// <remarks>
/// This exception does not represent SQL Server T-SQL syntax, compilation,
/// permission, connection, or runtime errors. Source coordinates are nullable
/// because not every parser failure currently supplies them.
/// </remarks>
[Serializable]
public sealed class TigerQueryException : Exception
{
    /// <summary>Gets the one-based source line, or <see langword="null"/> when unavailable.</summary>
    public int? Line { get; }

    /// <summary>Gets the one-based source column, or <see langword="null"/> when unavailable.</summary>
    public int? Column { get; }

    /// <summary>Initializes an exception without a message or source position.</summary>
    public TigerQueryException()
    {
    }

    /// <summary>Initializes an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public TigerQueryException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes an exception with a message and underlying exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The exception that caused this failure.</param>
    public TigerQueryException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Initializes an exception with optional source coordinates.</summary>
    /// <param name="message">The error message without a location prefix.</param>
    /// <param name="line">The one-based source line, or <see langword="null"/>.</param>
    /// <param name="column">The one-based source column, or <see langword="null"/>.</param>
    /// <remarks>
    /// When either coordinate is present, the formatted <see cref="Exception.Message"/>
    /// is prefixed with both line and column; a missing coordinate is displayed as zero.
    /// </remarks>
    public TigerQueryException(string message, int? line, int? column)
        : base(FormatMessage(message, line, column))
    {
        Line = line;
        Column = column;
    }

    private static string FormatMessage(string message, int? line, int? column)
    {
        if (line is null && column is null)
            return message;

        return $"Line {line ?? 0}, Column {column ?? 0}: {message}";
    }
    
}
