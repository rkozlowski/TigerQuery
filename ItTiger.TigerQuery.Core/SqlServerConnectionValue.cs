using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItTiger.TigerQuery.Core;

/// <summary>
/// A persisted connection value represented either by a legacy-compatible literal JSON
/// string or by an explicit external-value reference object.
/// </summary>
[JsonConverter(typeof(SqlServerConnectionValueJsonConverter))]
public sealed class SqlServerConnectionValue
{
    private SqlServerConnectionValue(
        string? literalValue,
        SqlServerExternalValueReference? reference)
    {
        LiteralValue = literalValue;
        Reference = reference;
    }

    /// <summary>Gets the literal value, or null when this is a reference.</summary>
    public string? LiteralValue { get; }

    /// <summary>Gets the external reference, or null when this is a literal.</summary>
    public SqlServerExternalValueReference? Reference { get; }

    /// <summary>Gets whether this value is resolved externally.</summary>
    public bool IsReference => Reference is not null;

    /// <summary>Creates a literal value that persists as a plain JSON string.</summary>
    public static SqlServerConnectionValue Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new SqlServerConnectionValue(value, null);
    }

    /// <summary>Creates an external value that persists as an explicit reference object.</summary>
    public static SqlServerConnectionValue External(SqlServerExternalValueReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new SqlServerConnectionValue(null, reference);
    }

    /// <summary>Returns a safe display value without resolving external input.</summary>
    public string Describe(bool sensitive = false)
    {
        if (Reference is not null)
            return Reference.Describe();

        return sensitive ? "<redacted literal>" : LiteralValue ?? string.Empty;
    }

    internal static SqlServerConnectionValue FromJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return Literal(element.GetString()!);

        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("A connection value must be a string or an external reference object.");

        var sourceText = ReadOptionalString(element, "Source")
            ?? throw new JsonException("An external reference requires Source.");

        SqlServerExternalValueSource source = sourceText switch
        {
            "EnvironmentVariable" => SqlServerExternalValueSource.EnvironmentVariable,
            "File" => SqlServerExternalValueSource.File,
            _ => throw new JsonException("The external value Source is not supported.")
        };

        SqlServerExternalFileFormat? format = null;
        var formatText = ReadOptionalString(element, "Format");
        if (formatText is not null)
        {
            format = formatText switch
            {
                "Text" => SqlServerExternalFileFormat.Text,
                "Json" => SqlServerExternalFileFormat.Json,
                _ => throw new JsonException("The external file Format is not supported.")
            };
        }

        var reference = new SqlServerExternalValueReference
        {
            Source = source,
            Name = ReadOptionalString(element, "Name"),
            Path = ReadOptionalString(element, "Path"),
            Format = format,
            Key = ReadOptionalString(element, "Key")
        };

        var errors = reference.Validate();
        if (errors.Count > 0)
            throw new JsonException(string.Join(" ", errors));

        return External(reference);
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new JsonException($"External reference property {propertyName} must be a string.");
        return property.GetString();
    }
}

internal sealed class SqlServerConnectionValueJsonConverter
    : JsonConverter<SqlServerConnectionValue>
{
    public override SqlServerConnectionValue Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return SqlServerConnectionValue.FromJsonElement(document.RootElement);
    }

    public override void Write(
        Utf8JsonWriter writer,
        SqlServerConnectionValue value,
        JsonSerializerOptions options)
    {
        if (value.Reference is null)
        {
            writer.WriteStringValue(value.LiteralValue);
            return;
        }

        var reference = value.Reference;
        var errors = reference.Validate();
        if (errors.Count > 0)
            throw new JsonException("The external value reference is malformed.");

        writer.WriteStartObject();
        writer.WriteString("Source", reference.Source switch
        {
            SqlServerExternalValueSource.EnvironmentVariable => "EnvironmentVariable",
            SqlServerExternalValueSource.File => "File",
            _ => throw new JsonException("The external value Source is not supported.")
        });

        if (reference.Source == SqlServerExternalValueSource.EnvironmentVariable)
        {
            writer.WriteString("Name", reference.Name);
        }
        else
        {
            writer.WriteString("Path", reference.Path);
            writer.WriteString("Format", reference.Format switch
            {
                SqlServerExternalFileFormat.Text => "Text",
                SqlServerExternalFileFormat.Json => "Json",
                _ => throw new JsonException("The external file Format is not supported.")
            });
            if (reference.Format == SqlServerExternalFileFormat.Json)
                writer.WriteString("Key", reference.Key);
        }

        writer.WriteEndObject();
    }
}
