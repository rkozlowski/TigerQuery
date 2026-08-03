using System.Text.Json;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

internal static class SqlServerExternalValueCliParser
{
    public const string ReferenceMustBeObject =
        "External-value options require a reference JSON object, not a literal value.";
    public const string MalformedReference =
        "An external-value option contains a malformed reference JSON object.";

    public static string? Validate(string? json)
    {
        if (json is null)
            return null;

        try
        {
            var value = JsonSerializer.Deserialize<SqlServerConnectionValue>(json);
            return value?.IsReference == true ? null : ReferenceMustBeObject;
        }
        catch (JsonException)
        {
            return MalformedReference;
        }
    }

    public static SqlServerConnectionValue? Parse(string? json)
    {
        if (json is null)
            return null;

        try
        {
            var value = JsonSerializer.Deserialize<SqlServerConnectionValue>(json);
            if (value?.IsReference != true)
                throw new ArgumentException(ReferenceMustBeObject, nameof(json));
            return value;
        }
        catch (JsonException)
        {
            throw new ArgumentException(MalformedReference, nameof(json));
        }
    }
}
