using System.Text.Json;

namespace ItTiger.TigerQuery.Core;

internal static class SqlServerExternalValueResolver
{
    public static string Resolve(
        SqlServerConnectionValue value,
        string fieldName,
        bool allowEmpty,
        SqlServerExternalValueResolutionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Reference is null)
            return RequireNonemptyWhenNeeded(value.LiteralValue ?? string.Empty, fieldName, allowEmpty, null);

        var reference = value.Reference;
        var definitionErrors = reference.Validate();
        if (definitionErrors.Count > 0)
        {
            throw new SqlServerExternalValueException(
                $"The external {fieldName} reference is malformed: {string.Join(" ", definitionErrors)}");
        }

        var resolved = reference.Source switch
        {
            SqlServerExternalValueSource.EnvironmentVariable =>
                ResolveEnvironment(reference, fieldName, options),
            SqlServerExternalValueSource.File => ResolveFile(reference, fieldName, options),
            _ => throw new SqlServerExternalValueException(
                $"The external {fieldName} reference uses an unsupported source.")
        };

        return RequireNonemptyWhenNeeded(resolved, fieldName, allowEmpty, reference);
    }

    private static string ResolveEnvironment(
        SqlServerExternalValueReference reference,
        string fieldName,
        SqlServerExternalValueResolutionOptions? options)
    {
        var reader = options?.EnvironmentReader ?? Environment.GetEnvironmentVariable;
        string? value;
        try
        {
            value = reader(reference.Name!);
        }
        catch (Exception)
        {
            throw new SqlServerExternalValueException(
                $"The external {fieldName} value from {reference.Describe()} could not be read.");
        }

        if (value is null)
        {
            throw new SqlServerExternalValueException(
                $"The external {fieldName} value from {reference.Describe()} is missing.");
        }

        return value;
    }

    private static string ResolveFile(
        SqlServerExternalValueReference reference,
        string fieldName,
        SqlServerExternalValueResolutionOptions? options)
    {
        string text;
        try
        {
            var reader = options?.FileReader ?? File.ReadAllText;
            text = reader(reference.Path!);
        }
        catch (Exception ex)
        {
            throw new SqlServerExternalValueException(
                $"The external {fieldName} value from {reference.Describe()} could not be read: "
                + DescribeReadFailure(ex));
        }

        if (reference.Format == SqlServerExternalFileFormat.Text)
            return text;

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new SqlServerExternalValueException(
                    $"The external {fieldName} value from {reference.Describe()} requires a top-level JSON object.");
            }

            if (!document.RootElement.TryGetProperty(reference.Key!, out var property))
            {
                throw new SqlServerExternalValueException(
                    $"The external {fieldName} value from {reference.Describe()} is missing.");
            }

            if (property.ValueKind != JsonValueKind.String)
            {
                throw new SqlServerExternalValueException(
                    $"The external {fieldName} value from {reference.Describe()} must be a JSON string.");
            }

            return property.GetString()!;
        }
        catch (JsonException ex)
        {
            // The position locates the defect without echoing the file, which may hold a
            // secret next to the broken token.
            var position = ex.LineNumber is long line
                ? $" (line {line + 1}, byte {(ex.BytePositionInLine ?? 0) + 1})"
                : string.Empty;
            throw new SqlServerExternalValueException(
                $"The external {fieldName} value from {reference.Describe()} contains malformed JSON{position}.");
        }
    }

    // The cause is reduced to a category on purpose. The default reader's exceptions name
    // only the path, which the reference already shows, but an injected reader may put
    // anything into its message, including the value it was reading.
    private static string DescribeReadFailure(Exception exception) => exception switch
    {
        FileNotFoundException => "the file does not exist.",
        DirectoryNotFoundException => "the directory does not exist.",
        UnauthorizedAccessException => "access to the file was denied.",
        PathTooLongException or NotSupportedException or ArgumentException => "the path is not valid.",
        IOException => "an I/O error occurred, for example because another process holds the file.",
        _ => "the file reader failed."
    };

    private static string RequireNonemptyWhenNeeded(
        string value,
        string fieldName,
        bool allowEmpty,
        SqlServerExternalValueReference? reference)
    {
        if (allowEmpty || !string.IsNullOrWhiteSpace(value))
            return value;

        var source = reference is null ? "the configured literal" : reference.Describe();
        throw new SqlServerExternalValueException(
            $"The external {fieldName} value from {source} is empty.");
    }
}
