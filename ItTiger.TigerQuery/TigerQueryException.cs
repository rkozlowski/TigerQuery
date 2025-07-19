using System;
using System.Runtime.Serialization;

namespace ItTiger.TigerQuery;

[Serializable]
public sealed class TigerQueryException : Exception
{
    public int? Line { get; }
    public int? Column { get; }

    public TigerQueryException()
    {
    }

    public TigerQueryException(string message)
        : base(message)
    {
    }

    public TigerQueryException(string message, Exception inner)
        : base(message, inner)
    {
    }

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
