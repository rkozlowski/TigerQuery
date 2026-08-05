using System.Text;

namespace ItTiger.TigerSqlCmd;

/// <summary>
/// One <c>exec</c> invocation with the resolved connection string already substituted:
/// what <see cref="TigerSqlCmdChildProcess"/> starts.
/// </summary>
/// <param name="Executable">The child executable, exactly as the caller wrote it.</param>
/// <param name="Arguments">
/// The child arguments after placeholder substitution, passed to the child as a list so no
/// shell re-parses them.
/// </param>
/// <param name="EnvironmentVariableName">
/// The child-only environment variable to set to the connection string, or null when the
/// caller selected argument substitution alone.
/// </param>
internal sealed record TigerSqlCmdExecInvocation(
    string Executable,
    IReadOnlyList<string> Arguments,
    string? EnvironmentVariableName);

/// <summary>
/// A validated <c>exec</c> handoff configuration: the child executable, its argument
/// templates, and the handoff methods the caller selected.
/// </summary>
/// <remarks>
/// The plan is built from the command line alone and never holds a connection string, so
/// every handoff failure is reported before the saved connection is resolved and nothing
/// the plan can print can leak a secret. <see cref="Materialize"/> is the only place the
/// resolved value enters, and its result goes straight to the child process.
/// </remarks>
internal sealed class TigerSqlCmdExecPlan
{
    /// <summary>The exact token replaced by the resolved connection string.</summary>
    public const string ConnectionStringPlaceholder = "{connection-string}";

    private TigerSqlCmdExecPlan(
        string executable,
        IReadOnlyList<string> argumentTemplates,
        bool substitutesArguments,
        string? environmentVariableName)
    {
        Executable = executable;
        ArgumentTemplates = argumentTemplates;
        SubstitutesArguments = substitutesArguments;
        EnvironmentVariableName = environmentVariableName;
    }

    /// <summary>The child executable, exactly as the caller wrote it.</summary>
    public string Executable { get; }

    /// <summary>The child arguments before substitution; still carrying any placeholder.</summary>
    public IReadOnlyList<string> ArgumentTemplates { get; }

    /// <summary>True when at least one argument carries the placeholder.</summary>
    public bool SubstitutesArguments { get; }

    /// <summary>The validated child-only environment variable name, or null.</summary>
    public string? EnvironmentVariableName { get; }

    /// <summary>
    /// Validates a child command line and the requested environment-variable handoff.
    /// </summary>
    /// <param name="childCommandLine">
    /// The tokens after <c>--</c>: null when the separator was absent, empty when it was
    /// present with nothing after it.
    /// </param>
    /// <param name="environmentVariableName">
    /// The <c>--connection-string-env</c> value, or null when the option was omitted.
    /// </param>
    /// <returns>
    /// True with <paramref name="plan"/> set; otherwise false with a caller-facing
    /// <paramref name="error"/> that contains no resolved value.
    /// </returns>
    public static bool TryCreate(
        IReadOnlyList<string>? childCommandLine,
        string? environmentVariableName,
        out TigerSqlCmdExecPlan? plan,
        out string? error)
    {
        plan = null;

        if (childCommandLine is null)
        {
            error = "No child command was supplied. Write '--' after the exec options, "
                + "followed by the executable and its arguments.";
            return false;
        }

        if (childCommandLine.Count == 0)
        {
            error = "No child executable was supplied after '--'.";
            return false;
        }

        var executable = childCommandLine[0];
        if (string.IsNullOrWhiteSpace(executable))
        {
            error = "The child executable must not be empty or whitespace.";
            return false;
        }

        if (executable.Contains(ConnectionStringPlaceholder, StringComparison.Ordinal))
        {
            error = $"The {ConnectionStringPlaceholder} placeholder is substituted only into "
                + "child arguments, not into the child executable.";
            return false;
        }

        // Null means "omitted". An option that was supplied must name a usable variable,
        // including the empty-string case, which is a mistake rather than an omission.
        if (environmentVariableName is not null && !IsValidEnvironmentVariableName(environmentVariableName))
        {
            error = $"'{environmentVariableName}' is not a valid environment-variable name. Use a "
                + "letter or underscore followed by letters, digits, or underscores.";
            return false;
        }

        var argumentTemplates = childCommandLine.Skip(1).ToArray();
        var substitutesArguments = argumentTemplates.Any(
            argument => argument.Contains(ConnectionStringPlaceholder, StringComparison.Ordinal));

        if (!substitutesArguments && environmentVariableName is null)
        {
            error = "No connection-string handoff was requested. Put "
                + $"{ConnectionStringPlaceholder} in at least one child argument, supply "
                + "--connection-string-env <variable-name>, or use both.";
            return false;
        }

        plan = new TigerSqlCmdExecPlan(
            executable, argumentTemplates, substitutesArguments, environmentVariableName);
        error = null;
        return true;
    }

    /// <summary>
    /// Produces the invocation for a resolved connection string: every exact placeholder
    /// occurrence is replaced, in every argument, and all other argument text is preserved
    /// byte for byte. No shell expansion, quoting interpretation, environment expansion, or
    /// other templating happens here.
    /// </summary>
    public TigerSqlCmdExecInvocation Materialize(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        var arguments = new string[ArgumentTemplates.Count];
        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index] = ArgumentTemplates[index]
                .Replace(ConnectionStringPlaceholder, connectionString, StringComparison.Ordinal);
        }

        return new TigerSqlCmdExecInvocation(Executable, arguments, EnvironmentVariableName);
    }

    /// <summary>
    /// Renders the child command line for diagnostics, using the unsubstituted argument
    /// templates so the placeholder — never the resolved value — is what appears.
    /// </summary>
    /// <remarks>
    /// Only values TigerSqlCmd resolved are guaranteed absent: text the caller typed into an
    /// argument is echoed as typed, exactly as it already appears in their shell history and
    /// in the child's own command line.
    /// </remarks>
    public string DescribeRedacted()
    {
        var builder = new StringBuilder();
        AppendToken(builder, Executable);
        foreach (var template in ArgumentTemplates)
        {
            builder.Append(' ');
            AppendToken(builder, template);
        }

        return builder.ToString();
    }

    private static void AppendToken(StringBuilder builder, string token)
    {
        // Display-only quoting: the child never receives this string.
        var needsQuotes = token.Length == 0 || token.Any(char.IsWhiteSpace);
        if (needsQuotes)
            builder.Append('"').Append(token).Append('"');
        else
            builder.Append(token);
    }

    private static bool IsValidEnvironmentVariableName(string name)
    {
        // The portable identifier shape. Windows tolerates more, but a name that is not
        // portable is far more often a mistake than an intent, and the child has to be able
        // to read it back by the same name on every supported platform.
        if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            return false;

        return name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}
