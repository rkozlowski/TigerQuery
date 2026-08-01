using System.Globalization;
using System.Text;

namespace ItTiger.TigerQuery.Output;

/// <summary>
/// Converts result values to text and escapes fields for RFC 4180-compatible CSV.
/// </summary>
/// <remarks>
/// Version-one behavior is fixed: comma delimiter, CRLF records, header enabled,
/// minimal quoting, invariant culture, and SQL <c>NULL</c> written as an empty
/// field. The writer never adds banners, separator records, comments, or message
/// text to a CSV file.
/// </remarks>
internal static class CsvFormatter
{
    /// <summary>The record terminator, on every platform.</summary>
    public const string RecordTerminator = "\r\n";

    /// <summary>The field delimiter.</summary>
    public const char Delimiter = ',';

    private const char Quote = '"';

    /// <summary>
    /// Converts one value to its CSV text, before escaping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DBNull.Value"/> and a null reference are both SQL nulls and give
    /// an empty field, which an empty string also gives. SQL <c>NULL</c> and the
    /// empty string are therefore indistinguishable in version one.
    /// </para>
    /// <para>
    /// Conversions are culture-independent so output does not vary with the machine
    /// or thread culture.
    /// </para>
    /// </remarks>
    public static string FormatValue(object? value)
    {
        return value switch
        {
            null or DBNull => string.Empty,
            string text => text,
            char character => character.ToString(),
            byte[] bytes => "0x" + Convert.ToHexString(bytes),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
            float single => single.ToString("R", CultureInfo.InvariantCulture),
            double @double => @double.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Appends <paramref name="field"/> to <paramref name="builder"/>, quoting and
    /// escaping it only when necessary.
    /// </summary>
    /// <remarks>
    /// A field is quoted when it contains a comma, a double quote, CR, or LF. Every
    /// double quote inside a quoted field is doubled. Embedded CR and LF characters
    /// are preserved; they are data, not record terminators. Header names use the
    /// same rules as data fields.
    /// </remarks>
    public static void AppendField(StringBuilder builder, string field)
    {
        if (!RequiresQuoting(field))
        {
            builder.Append(field);
            return;
        }

        builder.Append(Quote);
        foreach (var character in field)
        {
            if (character == Quote)
            {
                builder.Append(Quote);
            }

            builder.Append(character);
        }

        builder.Append(Quote);
    }

    private static bool RequiresQuoting(string field)
    {
        foreach (var character in field)
        {
            if (character is Delimiter or Quote or '\r' or '\n')
            {
                return true;
            }
        }

        return false;
    }
}
