using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;

namespace ItTiger.TigerQuery.Tests.Output;

/// <summary>
/// Covers single-file and file-per-result-set behavior, including schema validation,
/// generated names, and file lifecycle.
/// </summary>
public sealed class ResultSetFileModeTests
{
    [Fact]
    public async Task SingleFileWritesTheFirstHeaderOnceAndAppendsCompatibleRows()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            ["Id", "Name"],
            [emission.BatchNumber, $"row{emission.BatchNumber}"]);

        var result = await host.RunAsync(
            ":Out report.csv\r\n"
            + "SELECT 1;\r\nGO\r\n"
            + "SELECT 2;\r\nGO\r\n"
            + "SELECT 3;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(
            "Id,Name\r\n1,row1\r\n2,row2\r\n3,row3\r\n",
            host.ReadText("report.csv"));
    }

    [Fact]
    public async Task SingleFileComparesColumnNamesOrdinallyAndByPosition()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            emission.BatchNumber == 1 ? ["Id", "Name"] : ["id", "Name"],
            ["x", "y"]);

        var result = await host.RunAsync(
            ":Out report.csv\r\nSELECT 1;\r\nGO\r\nSELECT 2;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        Assert.IsType<OutputRoutingException>(result.Exception);
    }

    [Fact]
    public async Task SingleFileAcceptsDifferingTypesForTheSameColumnNames()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(
            ["Id"],
            [emission.BatchNumber == 1 ? 1 : "one"]);

        var result = await host.RunAsync(
            ":Out report.csv\r\nSELECT 1;\r\nGO\r\nSELECT 2;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal("Id\r\n1\r\none\r\n", host.ReadText("report.csv"));
    }

    [Fact]
    public async Task SingleFileAllowsEmptyAndDuplicateNamesWhenTheyMatchExactly()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["", "Dup", "Dup"], ["a", "b", "c"]);

        var result = await host.RunAsync(
            ":Out report.csv\r\nSELECT 1;\r\nGO\r\nSELECT 2;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(",Dup,Dup\r\na,b,c\r\na,b,c\r\n", host.ReadText("report.csv"));
    }

    [Fact]
    public async Task AnIncompatibleResultSetWritesNoneOfItsHeaderOrRows()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            if (emission.BatchNumber == 1)
            {
                emission.ResultSet(["Id", "Name"], [1, "one"]);
            }
            else
            {
                emission.ResultSet(["Different"], ["never written"]);
            }
        };

        var result = await host.RunAsync(
            ":Out report.csv\r\n"
            + "SELECT 1;\r\nGO\r\n"
            + "SELECT 2;\r\nGO\r\n"
            + "SELECT 3;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);

        // Earlier content stays as partial run output; nothing of the failing set lands.
        var text = host.ReadText("report.csv");
        Assert.Equal("Id,Name\r\n1,one\r\n", text);
        Assert.DoesNotContain("Different", text, StringComparison.Ordinal);
        Assert.DoesNotContain("never written", text, StringComparison.Ordinal);

        // The run stopped immediately: batch 3 never started.
        Assert.DoesNotContain("start:3:1", host.Events);
    }

    [Fact]
    public async Task FilePerResultSetWritesOneHeaderPerFileWithNoSchemaRestriction()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["Id"], [emission.BatchNumber]);
            emission.ResultSet(["Totally", "Different"], ["a", "b"]);
        };

        var routing = new OutputRoutingOptions
        {
            ResultSetFileMode = ResultSetFileMode.FilePerResultSet
        };
        var result = await host.RunAsync(
            ":Out report.csv\r\nSELECT 1;\r\nGO 2\r\n",
            routing: routing);

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(
            [
                "report_b0001_e0001_r0001.csv",
                "report_b0001_e0001_r0002.csv",
                "report_b0001_e0002_r0001.csv",
                "report_b0001_e0002_r0002.csv"
            ],
            host.ProducedFiles());
        Assert.Equal("Id\r\n1\r\n", host.ReadText("report_b0001_e0001_r0001.csv"));
        Assert.Equal("Totally,Different\r\na,b\r\n", host.ReadText("report_b0001_e0001_r0002.csv"));
    }

    [Fact]
    public async Task FilePerResultSetSkipsZeroColumnResultsWithoutRenumberingLaterFiles()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["First"], ["1"]);
            emission.ResultSet([]);
            emission.ResultSet(["Third"], ["3"]);
        };

        var routing = new OutputRoutingOptions
        {
            ResultSetFileMode = ResultSetFileMode.FilePerResultSet
        };
        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n", routing: routing);

        Assert.Equal(
            ["report_b0001_e0001_r0001.csv", "report_b0001_e0001_r0003.csv"],
            host.ProducedFiles());
        Assert.Equal("Third\r\n3\r\n", host.ReadText("report_b0001_e0001_r0003.csv"));
    }

    [Fact]
    public async Task FilePerResultSetUsesTheLogicalBatchNumberAcrossRouteChanges()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [emission.BatchNumber]);

        var routing = new OutputRoutingOptions
        {
            ResultSetFileMode = ResultSetFileMode.FilePerResultSet
        };
        await host.RunAsync(
            ":Out first.csv\r\nSELECT 1;\r\nGO\r\n"
            + ":Out second.csv\r\nSELECT 2;\r\nGO\r\n",
            routing: routing);

        Assert.Equal(
            ["first_b0001_e0001_r0001.csv", "second_b0002_e0001_r0001.csv"],
            host.ProducedFiles());
    }

    [Fact]
    public async Task ErrorFilesNeverGetAResultSetSuffix()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["Id"], [1]);
            emission.ServerMessage("failed", 16);
        };

        var routing = new OutputRoutingOptions
        {
            ResultSetFileMode = ResultSetFileMode.FilePerResultSet
        };
        await host.RunAsync(
            ":Out report.csv\r\n:Error errors.log\r\nSELECT 1;\r\nGO\r\n",
            routing: routing);

        Assert.Equal(
            ["errors.log", "report_b0001_e0001_r0001.csv"],
            host.ProducedFiles());
    }

    [Fact]
    public async Task AnExistingFileIsTruncatedOnFirstUseInTheRun()
    {
        using var host = new OutputTestHost();
        await File.WriteAllTextAsync(
            host.PathOf("report.csv"),
            "stale content that must not survive",
            TestContext.Current.CancellationToken);

        host.Emit = emission => emission.ResultSet(["Id"], [1]);

        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal("Id\r\n1\r\n", host.ReadText("report.csv"));
    }

    [Fact]
    public async Task ParsingADirectiveCreatesNothing()
    {
        using var host = new OutputTestHost();
        host.Emit = _ => { };

        var result = await host.RunAsync(
            ":Out report.csv\r\n:Error errors.log\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Empty(host.ProducedFiles());
    }

    [Fact]
    public async Task AMissingDirectoryIsAnOutputFailureAndIsNotCreated()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [1]);

        var result = await host.RunAsync(":Out missing/report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        var failure = Assert.IsType<OutputRoutingException>(result.Exception);
        Assert.Equal(host.PathOf(Path.Combine("missing", "report.csv")), failure.Path);
        Assert.False(Directory.Exists(host.PathOf("missing")));
    }

    [Fact]
    public async Task ASharingViolationIsAnOutputFailure()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [1]);

        using var exclusive = new FileStream(
            host.PathOf("locked.csv"),
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        var result = await host.RunAsync(":Out locked.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        var failure = Assert.IsType<OutputRoutingException>(result.Exception);
        Assert.Equal(host.PathOf("locked.csv"), failure.Path);
    }

    [Theory]
    [InlineData(":Out shared.log\r\n:Error shared.log\r\nSELECT 1;\r\nGO\r\n")]
    [InlineData(":Error shared.log\r\n:Out shared.log\r\nSELECT 1;\r\nGO\r\n")]
    public async Task AChannelCollisionIsRejected(string script)
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [1]);

        var result = await host.RunAsync(script);

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        var failure = Assert.IsType<OutputRoutingException>(result.Exception);
        Assert.Equal(host.PathOf("shared.log"), failure.Path);
        Assert.Empty(host.ProducedFiles());
    }

    [Fact]
    public async Task TheMessageCompanionCannotCollideWithTheErrorFile()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [1]);

        var routing = new OutputRoutingOptions
        {
            OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages
        };
        var result = await host.RunAsync(
            ":Error report.csv.messages.log\r\n:Out report.csv\r\nSELECT 1;\r\nGO\r\n",
            routing: routing);

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        Assert.IsType<OutputRoutingException>(result.Exception);
    }

    [Fact]
    public async Task ARejectedDirectiveLeavesTheEarlierRouteUntouched()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [emission.BatchNumber]);

        var result = await host.RunAsync(
            ":Out report.csv\r\nSELECT 1;\r\nGO\r\n"
            + ":Error report.csv\r\nSELECT 2;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);

        // Batch 1 was written before the failing directive; batch 2 never ran.
        Assert.Equal("Id\r\n1\r\n", host.ReadText("report.csv"));
        Assert.DoesNotContain("start:2:1", host.Events);
    }

    [Fact]
    public async Task DestinationsStayOpenUntilTheRunCompletesAndAreClosedAfterwards()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Id"], [emission.BatchNumber]);

        await host.RunAsync(
            ":Out first.csv\r\nSELECT 1;\r\nGO\r\n"
            + ":Out second.csv\r\nSELECT 2;\r\nGO\r\n");

        // Both handles are released, so the files can be reopened exclusively.
        foreach (var name in new[] { "first.csv", "second.csv" })
        {
            using var reopened = new FileStream(
                host.PathOf(name),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.True(reopened.Length > 0);
        }
    }
}
