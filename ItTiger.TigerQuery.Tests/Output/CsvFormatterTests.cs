using ItTiger.TigerQuery.Output;
using System.Globalization;
using System.Text;

namespace ItTiger.TigerQuery.Tests.Output;

/// <summary>
/// Covers the fixed version-one CSV value conversion and quoting rules.
/// </summary>
public sealed class CsvFormatterTests
{
    [Fact]
    public void NullAndDbNullAndEmptyStringAllProduceAnEmptyField()
    {
        Assert.Equal(string.Empty, CsvFormatter.FormatValue(null));
        Assert.Equal(string.Empty, CsvFormatter.FormatValue(DBNull.Value));
        Assert.Equal(string.Empty, CsvFormatter.FormatValue(string.Empty));
    }

    [Fact]
    public void SqlNullAndEmptyStringSerializeIdentically()
    {
        var fromNull = new StringBuilder();
        var fromEmpty = new StringBuilder();

        CsvFormatter.AppendField(fromNull, CsvFormatter.FormatValue(DBNull.Value));
        CsvFormatter.AppendField(fromEmpty, CsvFormatter.FormatValue(string.Empty));

        Assert.Equal(fromEmpty.ToString(), fromNull.ToString());
        Assert.Equal(string.Empty, fromNull.ToString());
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(" leading", " leading")]
    [InlineData("trailing ", "trailing ")]
    [InlineData("contains, comma", "\"contains, comma\"")]
    [InlineData("said \"hello\"", "\"said \"\"hello\"\"\"")]
    [InlineData("\"", "\"\"\"\"")]
    [InlineData("line\rreturn", "\"line\rreturn\"")]
    [InlineData("line\nfeed", "\"line\nfeed\"")]
    [InlineData("line\r\nboth", "\"line\r\nboth\"")]
    [InlineData("café über 日本", "café über 日本")]
    [InlineData("semi;colon", "semi;colon")]
    [InlineData("tab\there", "tab\there")]
    public void FieldsAreQuotedOnlyWhenNecessary(string field, string expected)
    {
        var builder = new StringBuilder();

        CsvFormatter.AppendField(builder, field);

        Assert.Equal(expected, builder.ToString());
    }

    [Fact]
    public void EmbeddedLineEndingsArePreservedInsideQuotes()
    {
        var builder = new StringBuilder();

        CsvFormatter.AppendField(builder, "first\r\nsecond");

        Assert.Equal("\"first\r\nsecond\"", builder.ToString());
        Assert.Equal("\r\n", CsvFormatter.RecordTerminator);
    }

    [Fact]
    public void ValuesUseRoundTripInvariantFormats()
    {
        Assert.Equal("text", CsvFormatter.FormatValue("text"));
        Assert.Equal("x", CsvFormatter.FormatValue('x'));
        Assert.Equal("True", CsvFormatter.FormatValue(true));
        Assert.Equal("0x00FF10", CsvFormatter.FormatValue(new byte[] { 0x00, 0xFF, 0x10 }));
        Assert.Equal("0x", CsvFormatter.FormatValue(Array.Empty<byte>()));
        Assert.Equal(
            "2024-03-04T05:06:07.0080000",
            CsvFormatter.FormatValue(new DateTime(2024, 3, 4, 5, 6, 7, 8, DateTimeKind.Unspecified)));
        Assert.Equal(
            "2024-03-04T05:06:07.0080000+02:00",
            CsvFormatter.FormatValue(new DateTimeOffset(2024, 3, 4, 5, 6, 7, 8, TimeSpan.FromHours(2))));
        Assert.Equal("1.02:03:04.0050000", CsvFormatter.FormatValue(new TimeSpan(1, 2, 3, 4, 5)));
        Assert.Equal(
            "0f8fad5b-d9cb-469f-a165-70867728950e",
            CsvFormatter.FormatValue(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void NumbersAndDatesIgnoreTheCurrentCulture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            Assert.Equal("1234.5", CsvFormatter.FormatValue(1234.5d));
            Assert.Equal("1234.5", CsvFormatter.FormatValue(1234.5f));
            Assert.Equal("1234.56789", CsvFormatter.FormatValue(1234.56789m));
            Assert.Equal("-42", CsvFormatter.FormatValue(-42));
            Assert.Equal("9223372036854775807", CsvFormatter.FormatValue(long.MaxValue));
            Assert.Equal(
                "2024-03-04T05:06:07.0000000",
                CsvFormatter.FormatValue(new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Unspecified)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(0.1d, "0.1")]
    [InlineData(1d / 3d, "0.3333333333333333")]
    [InlineData(double.MaxValue, "1.7976931348623157E+308")]
    public void DoublesRoundTrip(double value, string expected)
    {
        var text = CsvFormatter.FormatValue(value);

        Assert.Equal(expected, text);
        Assert.Equal(value, double.Parse(text, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DecimalsKeepTheirScale()
    {
        Assert.Equal("1.500", CsvFormatter.FormatValue(1.500m));
        Assert.Equal("0.0000000001", CsvFormatter.FormatValue(0.0000000001m));
    }
}
