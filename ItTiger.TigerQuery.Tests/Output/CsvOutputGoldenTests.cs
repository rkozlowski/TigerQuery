using CsvHelper;
using CsvHelper.Configuration;
using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;
using System.Globalization;
using System.Text;

namespace ItTiger.TigerQuery.Tests.Output;

/// <summary>
/// Asserts the exact bytes TigerQuery writes for routed result sets.
/// </summary>
public sealed class CsvOutputGoldenTests
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public async Task FileStartsWithTheUtf8ByteOrderMarkAndUsesCommaAndCrLf()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            ["Id", "Name", "Comment"],
            [1, "Alice", "contains, comma"],
            [2, "Bob", "said \"hello\""],
            [3, DBNull.Value, string.Empty]);

        var result = await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        var bytes = host.ReadBytes("report.csv");
        Assert.Equal(Utf8Bom, bytes.Take(3));

        var expected = "Id,Name,Comment\r\n"
            + "1,Alice,\"contains, comma\"\r\n"
            + "2,Bob,\"said \"\"hello\"\"\"\r\n"
            + "3,,\r\n";
        Assert.Equal(expected, host.ReadText("report.csv"));
        Assert.Equal(Utf8Bom.Concat(Encoding.UTF8.GetBytes(expected)), bytes);
    }

    [Fact]
    public async Task ZeroRowResultWritesOnlyItsHeader()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id", "Name"]);

        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal("Id,Name\r\n", host.ReadText("report.csv"));
    }

    [Fact]
    public async Task ZeroColumnResultCreatesNoFile()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet([]);

        var result = await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Empty(host.ProducedFiles());
    }

    [Fact]
    public async Task RedirectedZeroColumnResultRaisesNoCallbackEither()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet([]);

        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Empty(host.ResultSetCallbacks);
    }

    [Fact]
    public async Task HeaderNamesUseTheSameEscapingAsDataFields()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            ["with, comma", "with \"quote\"", "", "with\r\nbreak"],
            ["a", "b", "c", "d"]);

        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(
            "\"with, comma\",\"with \"\"quote\"\"\",,\"with\r\nbreak\"\r\na,b,c,d\r\n",
            host.ReadText("report.csv"));
    }

    [Fact]
    public async Task NullAndEmptyStringAreIndistinguishableInTheFile()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            ["A", "B"],
            [DBNull.Value, string.Empty],
            [null, string.Empty]);

        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal("A,B\r\n,\r\n,\r\n", host.ReadText("report.csv"));
    }

    [Fact]
    public async Task UnicodeWhitespaceAndEmbeddedBreaksSurviveRoundTrip()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            ["Value"],
            ["café über 日本 🐯"],
            ["  padded  "],
            ["line\r\nbreak"],
            ["carriage\rreturn"],
            ["line\nfeed"]);

        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        var parsed = ParseWithCsvHelper(host.PathOf("report.csv"));

        Assert.Equal(["Value"], parsed[0]);
        Assert.Equal(["café über 日本 🐯"], parsed[1]);
        Assert.Equal(["  padded  "], parsed[2]);
        Assert.Equal(["line\r\nbreak"], parsed[3]);
        Assert.Equal(["carriage\rreturn"], parsed[4]);
        Assert.Equal(["line\nfeed"], parsed[5]);
    }

    [Fact]
    public async Task AWidelyUsedCsvLibraryReadsTheEmittedFile()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            ["Id", "Name", "Comment"],
            [1, "Alice", "contains, comma"],
            [2, "Bob", "said \"hello\""],
            [3, DBNull.Value, "multi\r\nline"]);

        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        var parsed = ParseWithCsvHelper(host.PathOf("report.csv"));

        Assert.Equal(4, parsed.Count);
        Assert.Equal(["Id", "Name", "Comment"], parsed[0]);
        Assert.Equal(["1", "Alice", "contains, comma"], parsed[1]);
        Assert.Equal(["2", "Bob", "said \"hello\""], parsed[2]);
        Assert.Equal(["3", "", "multi\r\nline"], parsed[3]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public async Task ValuesAreInvariantUnderANonEnglishCulture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            using var host = new OutputTestHost();
            host.Emit = emission => emission.ResultSet(
                ["Number", "Money", "Moment", "Span", "Id", "Blob", "Real"],
                [
                    1234.5d,
                    12345.67m,
                    new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Unspecified),
                    new TimeSpan(1, 2, 3, 4, 5),
                    Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                    new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                    1.5f
                ]);

            await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

            Assert.Equal(
                "Number,Money,Moment,Span,Id,Blob,Real\r\n"
                + "1234.5,12345.67,2024-03-04T05:06:07.0000000,1.02:03:04.0050000,"
                + "0f8fad5b-d9cb-469f-a165-70867728950e,0xDEADBEEF,1.5\r\n",
                host.ReadText("report.csv"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task TheRequestedPathIsUsedExactlyWithNoInferredExtension()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["A"], ["1"]);

        await host.RunAsync(":Out report\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(["report"], host.ProducedFiles());
        Assert.Equal("A\r\n1\r\n", host.ReadText("report"));
    }

    [Fact]
    public async Task DefaultEncodingFailsOnUnencodableInputInsteadOfReplacingIt()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            ["Value"],
            // A lone high surrogate cannot be encoded as UTF-8.
            [new string(['\uD800', 'x'])]);

        var result = await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        var failure = Assert.IsType<OutputRoutingException>(result.Exception);
        Assert.Equal(host.PathOf("report.csv"), failure.Path);
        Assert.DoesNotContain("�", host.ReadText("report.csv"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAlternateEncodingIsUsedWithItsOwnPreambleAndStaysStrict()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Value"], ["plain"]);

        var routing = new OutputRoutingOptions
        {
            FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };
        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n", routing: routing);

        var bytes = host.ReadBytes("report.csv");
        Assert.NotEqual(Utf8Bom, bytes.Take(3));
        Assert.Equal("Value\r\nplain\r\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task AnAlternateEncodingFailsOnValuesItCannotRepresent()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Value"], ["日本"]);

        var routing = new OutputRoutingOptions { FileEncoding = Encoding.ASCII };
        var result = await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n", routing: routing);

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        Assert.IsType<OutputRoutingException>(result.Exception);
    }

    private static List<string[]> ParseWithCsvHelper(string path)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            Delimiter = ",",
            NewLine = "\r\n"
        };

        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, configuration);

        var records = new List<string[]>();
        while (csv.Read())
        {
            var record = new string[csv.Parser.Count];
            for (var index = 0; index < record.Length; index++)
            {
                record[index] = csv.GetField(index) ?? string.Empty;
            }

            records.Add(record);
        }

        return records;
    }
}
