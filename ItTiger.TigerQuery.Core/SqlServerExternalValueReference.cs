namespace ItTiger.TigerQuery.Core;

/// <summary>Describes an external source without containing its resolved value.</summary>
public sealed class SqlServerExternalValueReference
{
    /// <summary>Gets or sets the source kind.</summary>
    public SqlServerExternalValueSource Source { get; set; }

    /// <summary>Gets or sets the environment-variable name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the file path.</summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets the file format.</summary>
    public SqlServerExternalFileFormat? Format { get; set; }

    /// <summary>Gets or sets the exact top-level JSON property name.</summary>
    public string? Key { get; set; }

    /// <summary>Validates the explicit source-specific shape without reading the source.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        switch (Source)
        {
            case SqlServerExternalValueSource.EnvironmentVariable:
                if (string.IsNullOrWhiteSpace(Name))
                    errors.Add("An environment-variable reference requires a nonblank Name.");
                if (Path is not null || Format is not null || Key is not null)
                    errors.Add("An environment-variable reference may contain only Source and Name.");
                break;

            case SqlServerExternalValueSource.File:
                if (string.IsNullOrWhiteSpace(Path))
                    errors.Add("A file reference requires a nonblank Path.");
                if (Name is not null)
                    errors.Add("A file reference must not contain Name.");
                if (Format is null)
                    errors.Add("A file reference requires an explicit Format.");
                else if (Format == SqlServerExternalFileFormat.Text && Key is not null)
                    errors.Add("A Text file reference must not contain Key.");
                else if (Format == SqlServerExternalFileFormat.Json && string.IsNullOrWhiteSpace(Key))
                    errors.Add("A Json file reference requires a nonblank Key.");
                break;

            default:
                errors.Add("The external value source is not supported.");
                break;
        }

        return errors;
    }

    /// <summary>Returns a safe description containing the source locator, never its value.</summary>
    public string Describe() => Source switch
    {
        SqlServerExternalValueSource.EnvironmentVariable =>
            $"environment variable '{Name ?? "<missing>"}'",
        SqlServerExternalValueSource.File when Format == SqlServerExternalFileFormat.Json =>
            $"JSON file '{Path ?? "<missing>"}', key '{Key ?? "<missing>"}'",
        SqlServerExternalValueSource.File => $"text file '{Path ?? "<missing>"}'",
        _ => "unsupported external source"
    };
}
