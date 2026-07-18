using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Central parsing and validation for CLI metadata assignments, mutations, and filters.
/// Raw values are intentionally never trimmed or normalized.
/// </summary>
internal static class SqlServerConnectionMetadataOptions
{
    public const string MalformedAssignment =
        "Metadata entries must use key=value format.";
    public const string EmptyKey =
        "Metadata keys must not be empty.";
    public const string DuplicateAssignment =
        "A metadata key cannot be set more than once in the same command.";
    public const string ConflictingMutation =
        "The same metadata key cannot be both set and removed in one command.";

    public static string? ValidateMutations(
        IReadOnlyList<string> assignments,
        IReadOnlyList<string> removals)
    {
        var error = TryParseAssignments(assignments, rejectDuplicateKeys: true, out var parsed);
        if (error is not null)
            return error;

        if (removals.Any(string.IsNullOrEmpty))
            return EmptyKey;

        var removedKeys = removals.ToHashSet(StringComparer.Ordinal);
        return parsed.Any(entry => removedKeys.Contains(entry.Key))
            ? ConflictingMutation
            : null;
    }

    public static string? ValidateFilters(
        IReadOnlyList<string> equals,
        IReadOnlyList<string> isSet,
        IReadOnlyList<string> isNotSet)
    {
        var error = TryParseAssignments(equals, rejectDuplicateKeys: false, out _);
        if (error is not null)
            return error;

        return isSet.Any(string.IsNullOrEmpty) || isNotSet.Any(string.IsNullOrEmpty)
            ? EmptyKey
            : null;
    }

    public static void ApplyMutations(
        SqlServerConnectionProfile profile,
        IReadOnlyList<string> assignments,
        IReadOnlyList<string> removals)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var error = TryParseAssignments(assignments, rejectDuplicateKeys: true, out var parsed);
        if (error is not null)
            throw new ArgumentException(error, nameof(assignments));

        foreach (var (key, value) in parsed)
            profile.SetMetadata(key, value);

        foreach (var key in removals)
            profile.RemoveMetadata(key);
    }

    public static IReadOnlyList<SqlServerConnectionMetadataFilter> ToFilters(
        IReadOnlyList<string> equals,
        IReadOnlyList<string> isSet,
        IReadOnlyList<string> isNotSet)
    {
        var error = TryParseAssignments(equals, rejectDuplicateKeys: false, out var parsed);
        if (error is not null)
            throw new ArgumentException(error, nameof(equals));

        return
        [
            .. parsed.Select(entry => new SqlServerConnectionMetadataFilter
            {
                Key = entry.Key,
                Operator = SqlServerConnectionMetadataFilterOperator.Equals,
                Value = entry.Value
            }),
            .. isSet.Select(key => new SqlServerConnectionMetadataFilter
            {
                Key = key,
                Operator = SqlServerConnectionMetadataFilterOperator.IsSet
            }),
            .. isNotSet.Select(key => new SqlServerConnectionMetadataFilter
            {
                Key = key,
                Operator = SqlServerConnectionMetadataFilterOperator.IsNotSet
            })
        ];
    }

    private static string? TryParseAssignments(
        IReadOnlyList<string> values,
        bool rejectDuplicateKeys,
        out List<KeyValuePair<string, string>> parsed)
    {
        parsed = new List<KeyValuePair<string, string>>(values.Count);
        HashSet<string>? keys = rejectDuplicateKeys
            ? new HashSet<string>(StringComparer.Ordinal)
            : null;

        foreach (var raw in values)
        {
            var separator = raw.IndexOf('=');
            if (separator < 0)
                return MalformedAssignment;
            if (separator == 0)
                return EmptyKey;

            var key = raw[..separator];
            if (keys is not null && !keys.Add(key))
                return DuplicateAssignment;

            parsed.Add(new KeyValuePair<string, string>(key, raw[(separator + 1)..]));
        }

        return null;
    }
}
