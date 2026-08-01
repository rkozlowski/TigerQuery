using ItTiger.TigerQuery.Output;
using System.Globalization;

namespace ItTiger.TigerQuery.Tests.Output;

/// <summary>
/// Covers the deterministic file-per-result-set names.
/// </summary>
public sealed class ResultSetFileNamingTests
{
    [Theory]
    // The examples documented in the design.
    [InlineData("report.csv", 1, 1, 1, "report_b0001_e0001_r0001.csv")]
    [InlineData("report.csv", 3, 2, 1, "report_b0003_e0002_r0001.csv")]
    [InlineData("report.data", 10000, 1, 1, "report_b10000_e0001_r0001.data")]
    // No extension takes the format's default.
    [InlineData("report", 12, 1, 3, "report_b0012_e0001_r0003.csv")]
    // Values longer than four digits are never truncated.
    [InlineData("report.csv", 123456, 12345, 99999, "report_b123456_e12345_r99999.csv")]
    // Unicode stems and dotted stems are preserved.
    [InlineData("réçâp 日本.csv", 1, 1, 1, "réçâp 日本_b0001_e0001_r0001.csv")]
    [InlineData("report.2024.csv", 1, 1, 1, "report.2024_b0001_e0001_r0001.csv")]
    public void GeneratedNamesMatchTheDocumentedPattern(
        string baseName,
        int batch,
        int execution,
        int result,
        string expected)
    {
        var actual = ResultSetFileNaming.BuildPath(baseName, ".csv", batch, execution, result);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DirectoriesArePreserved()
    {
        var basePath = Path.Combine("exports", "nested", "report");

        var actual = ResultSetFileNaming.BuildPath(basePath, ".csv", 12, 1, 3);

        Assert.Equal(Path.Combine("exports", "nested", "report_b0012_e0001_r0003.csv"), actual);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    public void NamesAreCultureIndependent(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            Assert.Equal(
                "report_b0001_e0002_r0003.csv",
                ResultSetFileNaming.BuildPath("report.csv", ".csv", 1, 2, 3));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
