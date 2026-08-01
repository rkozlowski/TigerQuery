using ItTiger.TigerQuery.Engine;
using System.Text;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Covers the routing behavior that only a real provider can produce: the actual
/// error transport SQL Server chooses, multi-result batches, and true SQL data types
/// flowing into CSV.
/// </summary>
/// <remarks>
/// Path handling, CSV escaping, naming, and failure classification are proved by the
/// fast unit and probe tests; these stay narrow on purpose.
/// </remarks>
public sealed class OutputRoutingLiveTests : IDisposable
{
    private readonly string _directory;

    public OutputRoutingLiveTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "tigerquery-output-live",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ADiagnosticDeliveredByBothTransportsIsWrittenOnce(
        TigerQueryExecutionMode executionMode)
    {
        var result = await RunAsync(
            executionMode,
            """
            :Error errors.log
            RAISERROR('Deliberate severity 16.', 16, 7);
            GO
            THROW 51000, 'Deliberate throw.', 3;
            GO
            """,
            new OutputRoutingOptions { AllowScriptOutputDirectives = true },
            continueOnError: true);

        Assert.Equal(2, result.FailedBatches);

        var lines = ReadLines("errors.log");
        Assert.Equal(
            ["Deliberate severity 16.", "Deliberate throw."],
            lines);
        Assert.Equal(lines.Count, lines.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task MultipleResultSetsFromOneBatchKeepProviderOrderInOneFile()
    {
        var result = await RunAsync(
            TigerQueryExecutionMode.Streaming,
            """
            :Out report.csv
            SELECT 1 AS Id UNION ALL SELECT 2 ORDER BY Id;
            SELECT 3 AS Id;
            GO
            """,
            new OutputRoutingOptions());

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal("Id\r\n1\r\n2\r\n3\r\n", ReadText("report.csv"));
    }

    [Fact]
    public async Task FilePerResultSetUsesTheProviderResultCoordinates()
    {
        var result = await RunAsync(
            TigerQueryExecutionMode.Prepared,
            """
            :Out report.csv
            SELECT 1 AS First;
            SELECT 'two' AS Second;
            GO
            """,
            new OutputRoutingOptions { ResultSetFileMode = ResultSetFileMode.FilePerResultSet });

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);

        // Both selects belong to one execution, so they differ in the result coordinate.
        Assert.Equal("First\r\n1\r\n", ReadText("report_b0001_e0001_r0001.csv"));
        Assert.Equal("Second\r\ntwo\r\n", ReadText("report_b0001_e0001_r0002.csv"));
    }

    [Fact]
    public async Task RealSqlTypesUseInvariantRoundTripFormats()
    {
        var result = await RunAsync(
            TigerQueryExecutionMode.Streaming,
            """
            :Out types.csv
            SELECT
                CAST(1234.56 AS decimal(10,2)) AS Money,
                CAST('2024-03-04T05:06:07' AS datetime2(0)) AS Moment,
                CAST('0f8fad5b-d9cb-469f-a165-70867728950e' AS uniqueidentifier) AS Id,
                CAST(0xDEADBEEF AS varbinary(8)) AS Blob,
                CAST(NULL AS nvarchar(10)) AS Missing;
            GO
            """,
            new OutputRoutingOptions());

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal(
            "Money,Moment,Id,Blob,Missing\r\n"
            + "1234.56,2024-03-04T05:06:07.0000000,0f8fad5b-d9cb-469f-a165-70867728950e,0xDEADBEEF,\r\n",
            ReadText("types.csv"));
    }

    [Fact]
    public async Task ResultSetsAndNormalMessagesSplitAcrossTheCompanionFile()
    {
        var result = await RunAsync(
            TigerQueryExecutionMode.Streaming,
            """
            :Out report.csv
            PRINT 'first message';
            SELECT 1 AS Id;
            PRINT 'second message';
            GO
            """,
            new OutputRoutingOptions
            {
                OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages
            });

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Equal("Id\r\n1\r\n", ReadText("report.csv"));
        Assert.Equal(
            ["first message", "second message"],
            ReadLines("report.csv.messages.log"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private async Task<ExecutionResult> RunAsync(
        TigerQueryExecutionMode executionMode,
        string script,
        OutputRoutingOptions routing,
        bool continueOnError = false)
    {
        var connectionString = SqlServerTestEnvironment.RequireConnectionString();
        var options = new TigerQueryEngineOptions
        {
            ConnectionString = connectionString,
            ExecutionMode = executionMode,
            Mode = SqlCmdMode.SqlCmdEx,
            ContinueOnError = continueOnError,
            OutputRouting = new OutputRoutingOptions
            {
                BaseDirectory = _directory,
                OutBehavior = routing.OutBehavior,
                ResultSetFileMode = routing.ResultSetFileMode,
                ResultSetFormat = routing.ResultSetFormat,
                FileEncoding = routing.FileEncoding,
                InitialOutPath = routing.InitialOutPath,
                InitialErrorPath = routing.InitialErrorPath,
                AllowScriptOutputDirectives = routing.AllowScriptOutputDirectives
            }
        };

        var engine = new TigerQueryEngine(options);
        return await engine.RunFromStringAsync(script, TestContext.Current.CancellationToken);
    }

    private string ReadText(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(_directory, name));
        var preamble = Encoding.UTF8.GetPreamble();
        Assert.Equal(preamble, bytes.Take(preamble.Length));
        return Encoding.UTF8.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
    }

    private List<string> ReadLines(string name) =>
        [.. ReadText(name).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)];
}
