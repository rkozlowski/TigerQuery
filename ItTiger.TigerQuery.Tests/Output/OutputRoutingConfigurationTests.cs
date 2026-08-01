using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;
using System.Text;

namespace ItTiger.TigerQuery.Tests.Output;

/// <summary>
/// Covers run-start validation of routing configuration, which must happen before
/// parsing, connection opening, SQL execution, or output-file creation.
/// </summary>
public sealed class OutputRoutingConfigurationTests
{
    [Fact]
    public void DefaultsMatchTheDocumentedRecommendation()
    {
        var routing = new OutputRoutingOptions();

        Assert.Null(routing.InitialOutPath);
        Assert.Null(routing.InitialErrorPath);
        Assert.Null(routing.BaseDirectory);
        Assert.Null(routing.FileEncoding);
        Assert.Equal(OutDirectiveBehavior.ResultSetsOnly, routing.OutBehavior);
        Assert.Equal(ResultSetFileMode.SingleFile, routing.ResultSetFileMode);
        Assert.Equal(ResultSetOutputFormat.Csv, routing.ResultSetFormat);
        Assert.True(routing.AllowScriptOutputDirectives);
        Assert.NotNull(new TigerQueryEngineOptions().OutputRouting);
    }

    [Theory]
    [InlineData("OutBehavior")]
    [InlineData("ResultSetFileMode")]
    [InlineData("ResultSetFormat")]
    public async Task UndefinedEnumValuesFailAtRunStart(string member)
    {
        using var host = new OutputTestHost();
        var routing = member switch
        {
            "OutBehavior" => new OutputRoutingOptions { OutBehavior = (OutDirectiveBehavior)42 },
            "ResultSetFileMode" => new OutputRoutingOptions { ResultSetFileMode = (ResultSetFileMode)42 },
            _ => new OutputRoutingOptions { ResultSetFormat = (ResultSetOutputFormat)42 }
        };

        var engine = host.CreateEngine(host.BuildOptions(routing: routing));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => engine.RunFromStringAsync(
                "SELECT 1;\r\nGO\r\n",
                TestContext.Current.CancellationToken));

        Assert.Equal(0, host.OpenCount);
        Assert.Empty(host.ProducedFiles());
    }

    [Fact]
    public async Task ConfigurationIsValidatedBeforeParsing()
    {
        using var host = new OutputTestHost();
        var routing = new OutputRoutingOptions { ResultSetFileMode = (ResultSetFileMode)42 };
        var engine = host.CreateEngine(host.BuildOptions(routing: routing));

        // The script would also fail to parse; the configuration failure comes first.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => engine.RunFromStringAsync(":setvar\r\n", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEmptyInitialPathIsRejectedAtRunStart()
    {
        using var host = new OutputTestHost();
        var routing = new OutputRoutingOptions { InitialOutPath = "   " };
        var engine = host.CreateEngine(host.BuildOptions(routing: routing));

        await Assert.ThrowsAsync<ArgumentException>(
            () => engine.RunFromStringAsync(
                "SELECT 1;\r\nGO\r\n",
                TestContext.Current.CancellationToken));

        Assert.Equal(0, host.OpenCount);
    }

    [Fact]
    public async Task AnUnresolvableBaseDirectoryFailsAtRunStart()
    {
        using var host = new OutputTestHost();
        var routing = new OutputRoutingOptions { BaseDirectory = "\0invalid" };
        var options = host.BuildOptions(routing: OutputTestHost.CloneWithBaseDirectory(routing, "\0invalid"));
        var engine = host.CreateEngine(options);

        await Assert.ThrowsAsync<ArgumentException>(
            () => engine.RunFromStringAsync(
                "SELECT 1;\r\nGO\r\n",
                TestContext.Current.CancellationToken));

        Assert.Equal(0, host.OpenCount);
    }

    [Fact]
    public async Task AnUnusableEncodingFailsAtRunStart()
    {
        using var host = new OutputTestHost();
        var routing = new OutputRoutingOptions { FileEncoding = new StubbornEncoding() };
        var engine = host.CreateEngine(host.BuildOptions(routing: routing));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => engine.RunFromStringAsync(
                "SELECT 1;\r\nGO\r\n",
                TestContext.Current.CancellationToken));

        Assert.Contains("exception fallbacks", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, host.OpenCount);
    }

    [Fact]
    public async Task AReplacementConfiguredEncodingIsStrengthenedRatherThanUsedAsSupplied()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["Value"], ["日本"]);

        // Encoding.ASCII replaces unencodable characters with '?' by default.
        Assert.IsType<EncoderReplacementFallback>(Encoding.ASCII.EncoderFallback);

        var routing = new OutputRoutingOptions { FileEncoding = Encoding.ASCII };
        var result = await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n", routing: routing);

        Assert.Equal(ExecutionResultCode.OutputFailed, result.ResultCode);
        Assert.DoesNotContain("?", host.ReadText("report.csv"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RelativePathsResolveAgainstTheConfiguredBaseDirectory()
    {
        using var host = new OutputTestHost();
        var nested = Path.Combine(host.Directory, "nested");
        Directory.CreateDirectory(nested);
        host.Emit = emission => emission.ResultSet(["Id"], [1]);

        var routing = new OutputRoutingOptions { BaseDirectory = nested };
        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n", routing: routing);

        Assert.Equal(["nested/report.csv"], host.ProducedFiles());
    }

    /// <summary>An encoding that cannot be reconfigured with exception fallbacks.</summary>
    private sealed class StubbornEncoding : Encoding
    {
        public override int GetByteCount(char[] chars, int index, int count) => count;

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) => 0;

        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) => 0;

        public override int GetMaxByteCount(int charCount) => charCount;

        public override int GetMaxCharCount(int byteCount) => byteCount;

        public override object Clone() =>
            throw new NotSupportedException("This encoding cannot be copied.");
    }
}
