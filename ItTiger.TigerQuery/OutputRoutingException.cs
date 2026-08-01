using System;

namespace ItTiger.TigerQuery;

/// <summary>
/// Represents a failure to route, serialize, or write script output.
/// </summary>
/// <remarks>
/// <para>
/// This covers path resolution failures, channel collisions, directory-not-found,
/// access denied, sharing violations, unencodable characters, CSV schema
/// incompatibility, and flush or close failures. It never carries SQL Server
/// diagnostics, connection strings, or SQL values.
/// </para>
/// <para>
/// An output failure is fatal regardless of the effective <c>:ON ERROR</c> policy
/// and of
/// <see cref="Engine.TigerQueryEngineOptions.ContinueOnErrorForUnhandledExceptions"/>.
/// When it occurs during a batch it becomes the primary exception of that batch
/// and the run's <see cref="Engine.ExecutionResult.Exception"/> with
/// <see cref="Engine.ExecutionResultCode.OutputFailed"/>.
/// </para>
/// </remarks>
[Serializable]
public sealed class OutputRoutingException : TigerQueryException
{
    /// <summary>
    /// Names the <see cref="Exception.Data"/> entry that holds a SQL Server
    /// exception observed at the same time as the output failure.
    /// </summary>
    /// <remarks>
    /// The output failure always remains the primary exception; a contemporaneous
    /// <see cref="Microsoft.Data.SqlClient.SqlException"/> is attached under this
    /// key as secondary diagnostic context only.
    /// </remarks>
    public const string ContemporaneousExceptionDataKey = "TigerQuery.ContemporaneousException";

    /// <summary>Initializes an exception without a message or target path.</summary>
    public OutputRoutingException()
    {
    }

    /// <summary>Initializes an exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public OutputRoutingException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes an exception with a message and underlying exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The exception that caused this failure.</param>
    public OutputRoutingException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Initializes an exception with a message and the target path.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="path">The resolved output path involved in the failure.</param>
    public OutputRoutingException(string message, string? path)
        : base(message)
    {
        Path = path;
    }

    /// <summary>
    /// Initializes an exception with a message, the target path, and the cause.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="path">The resolved output path involved in the failure.</param>
    /// <param name="inner">The exception that caused this failure.</param>
    public OutputRoutingException(string message, string? path, Exception inner)
        : base(message, inner)
    {
        Path = path;
    }

    /// <summary>
    /// Gets the output path involved in the failure, or <see langword="null"/>
    /// when no single path applies.
    /// </summary>
    /// <remarks>
    /// The value is the fully resolved path when resolution succeeded; otherwise
    /// it is the path exactly as the script or host supplied it.
    /// </remarks>
    public string? Path { get; }
}
