namespace ItTiger.TigerSqlCmd;

/// <summary>
/// Splits a raw process argument list at the <c>--</c> end-of-options separator that
/// introduces the <c>exec</c> child command line.
/// </summary>
/// <remarks>
/// TigerCli owns the grammar of everything before the separator, and it has no notion of a
/// passthrough tail, so the split happens once — before the app parses anything — and the
/// tail is handed to the <c>exec</c> command factory instead of to the parser. Only
/// <c>exec</c> takes part: for every other command path the argument list is returned
/// unchanged, so their parsing, errors, and help are exactly what they were before
/// <c>exec</c> existed.
/// </remarks>
internal static class TigerSqlCmdChildCommandLine
{
    /// <summary>The end-of-options separator that begins the child command line.</summary>
    public const string Separator = "--";

    /// <summary>
    /// Returns the arguments TigerCli should parse, plus the child command line when one was
    /// supplied.
    /// </summary>
    /// <returns>
    /// <c>ChildCommandLine</c> is null when no separator was present (the command line never
    /// asked for a child) and empty when the separator was present with nothing after it
    /// (the caller asked for a child but named none). <c>exec</c> reports those two states
    /// differently, so they must not be collapsed here.
    /// </returns>
    public static (string[] HostArguments, IReadOnlyList<string>? ChildCommandLine) Split(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // Command paths are matched case-insensitively by TigerCli, so the guard is too.
        if (arguments.Count == 0
            || !string.Equals(arguments[0], TigerSqlCmdApp.ExecCommandName, StringComparison.OrdinalIgnoreCase))
        {
            return ([.. arguments], null);
        }

        // First separator wins, as in every other tool that uses this convention: a later
        // "--" is an ordinary child argument, not a second split point.
        for (var index = 1; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], Separator, StringComparison.Ordinal))
                continue;

            return ([.. arguments.Take(index)], [.. arguments.Skip(index + 1)]);
        }

        return ([.. arguments], null);
    }
}
