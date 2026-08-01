using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Phase 3 application-contract tests for tiger-sqlcmd's thin mapping onto the
/// TigerQuery-owned output-routing implementation.
/// </summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class TigerSqlCmdOutputRoutingTests : IDisposable
{
    private readonly TempConnectionStore _temp = new();

    public void Dispose() => _temp.Dispose();

    private Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args)
        => CliTestRunner.RunAsync(_temp.Store, args);

    [Fact]
    public void DefaultSettings_PreserveApplicationFallbackRoutes()
    {
        var routing = new TigerSqlCmdSettings().ToOutputRoutingOptions();

        Assert.Null(routing.InitialOutPath);
        Assert.Null(routing.InitialErrorPath);
        Assert.Null(routing.FileEncoding);
        Assert.Equal(ResultSetOutputFormat.Csv, routing.ResultSetFormat);
        Assert.Equal(ResultSetFileMode.SingleFile, routing.ResultSetFileMode);
        Assert.Equal(OutDirectiveBehavior.ResultSetsOnly, routing.OutBehavior);
        Assert.True(routing.AllowScriptOutputDirectives);
    }

    [Fact]
    public void Settings_MapDirectlyToTigerQueryOutputRouting()
    {
        var settings = new TigerSqlCmdSettings
        {
            OutputPath = "results.csv",
            ErrorOutputPath = "errors.log",
            ResultSetFormat = ResultSetOutputFormat.Csv,
            ResultSetFileMode = ResultSetFileMode.FilePerResultSet,
            OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages,
            OutputEncoding = "utf-8"
        };

        var routing = settings.ToOutputRoutingOptions();

        Assert.Equal("results.csv", routing.InitialOutPath);
        Assert.Equal("errors.log", routing.InitialErrorPath);
        Assert.Equal(ResultSetOutputFormat.Csv, routing.ResultSetFormat);
        Assert.Equal(ResultSetFileMode.FilePerResultSet, routing.ResultSetFileMode);
        Assert.Equal(OutDirectiveBehavior.ResultSetsAndNormalMessages, routing.OutBehavior);
        Assert.Equal("utf-8", routing.FileEncoding!.WebName);
        Assert.Equal([0xEF, 0xBB, 0xBF], routing.FileEncoding.GetPreamble());
    }

    [Theory]
    [InlineData("-o", "results.csv")]
    [InlineData("--output", "results.csv")]
    [InlineData("-e", "errors.log")]
    [InlineData("--error-output", "errors.log")]
    [InlineData("--format", "Csv")]
    [InlineData("--result-format", "Csv")]
    [InlineData("--output-mode", "FilePerResultSet")]
    [InlineData("--result-file-mode", "SingleFile")]
    [InlineData("--out-behavior", "ResultSetsAndNormalMessages")]
    [InlineData("--output-encoding", "utf-8")]
    [InlineData("--encoding", "utf-8")]
    public async Task OutputOptionAndAlias_IsAccepted(string option, string value)
    {
        var result = await RunAsync(
            "run", "--non-interactive", "-c", "missing", "-q", "SELECT 1",
            "--verbosity", "Silent", option, value);

        // Reaching connection resolution proves that binding and settings validation passed.
        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.DoesNotContain("Invalid value", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidOutputEncoding_FailsCliValidationBeforeConnectionResolution()
    {
        var result = await RunAsync(
            "run", "--non-interactive", "-c", "missing", "-q", "SELECT 1",
            "--output-encoding", "not-an-encoding");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains("not a supported .NET output encoding", result.StdErr);
        Assert.DoesNotContain("saved connection", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhitespaceInitialPath_FailsCliValidation()
    {
        var result = await RunAsync(
            "run", "--non-interactive", "-c", "missing", "-q", "SELECT 1",
            "--output", "   ");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, result.ExitCode);
        Assert.Contains("--output must not be empty or whitespace", result.StdErr);
    }

    [Fact]
    public async Task InvalidRoutingEnum_FailsAsInvalidArguments()
    {
        var result = await RunAsync(
            "run", "--non-interactive", "-c", "missing", "-q", "SELECT 1",
            "--output-mode", "one-file-per-table");

        Assert.Equal((int)TigerSqlCmdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("Invalid value for", result.StdErr);
        Assert.Contains("one-file-per-table", result.StdErr);
    }

    [Fact]
    public async Task PolishCulture_LocalizesOutputHelpAndValidation()
    {
        var help = await RunAsync("run", "--culture", "pl-PL", "--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, help.ExitCode);
        Assert.Contains("Wyniki: początkowa ścieżka", help.StdOut);
        Assert.Contains("Komunikaty/błędy: początkowa ścieżka", help.StdOut);
        Assert.Contains("domyślnie UTF-8 z BOM", help.StdOut);

        var invalid = await RunAsync(
            "run", "--culture", "pl-PL", "--non-interactive",
            "-c", "missing", "-q", "SELECT 1",
            "--output-encoding", "not-an-encoding");

        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionInvalidArguments, invalid.ExitCode);
        Assert.Contains("nie jest obsługiwaną nazwą kodowania", invalid.StdErr);
    }

    [Fact]
    public async Task RunHelp_GroupsAndDocumentsEveryOutputRoutingOption()
    {
        var result = await RunAsync("run", "--help");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("-o, --output", result.StdOut);
        Assert.Contains("-e, --error-output", result.StdOut);
        Assert.Contains("--format, --result-format", result.StdOut);
        Assert.Contains("--output-mode, --result-file-mode", result.StdOut);
        Assert.Contains("--out-behavior", result.StdOut);
        Assert.Contains("--output-encoding, --encoding", result.StdOut);
        Assert.Contains("Result output:", result.StdOut);
        Assert.Contains("Message/error routing:", result.StdOut);
        Assert.Contains("UTF-8 with BOM", result.StdOut);
    }

    [Fact]
    public async Task ExitCodeHelp_DocumentsDedicatedOutputFailure()
    {
        var result = await RunAsync("--help-errors");

        Assert.Equal((int)TigerSqlCmdExitCode.Ok, result.ExitCode);
        Assert.Contains("8", result.StdOut);
        Assert.Contains("Output failed", result.StdOut);
        Assert.Contains("could not be routed, serialized, or written", result.StdOut);
    }

    [Fact]
    public void OutputFailure_HasDedicatedExitCodeEight()
    {
        Assert.Equal(8, (int)TigerSqlCmdExitCode.OutputFailed);
        Assert.Equal(
            TigerSqlCmdExitCode.OutputFailed,
            ExecutionResultCode.OutputFailed.ToExitCode());
    }

    [Fact]
    public async Task EscapedOutputRoutingFailure_AlsoUsesDedicatedExitCodeEight()
    {
        var exitCode = await TigerSqlCmdEngineRunner.RunAsync(
            logger: null,
            _ => Task.FromException<ExecutionResult>(
                new OutputRoutingException("Output route failed.", "results.csv")));

        Assert.Equal(TigerSqlCmdExitCode.OutputFailed, exitCode);
    }

    [Fact]
    public async Task CliInitialRouteCollision_ExitsEightBeforeOpeningSqlConnection()
    {
        var add = await RunAsync(
            "--non-interactive", "connections", "add", "demo", "--server", "srv");
        Assert.Equal((int)TigerSqlCmdExitCode.Ok, add.ExitCode);

        var path = Path.Combine(
            Path.GetTempPath(), "TigerQueryCliTests", $"{Guid.NewGuid():N}.log");
        var result = await RunAsync(
            "run", "--non-interactive", "-c", "demo", "-q", "SELECT 1",
            "--verbosity", "Silent", "--output", path, "--error-output", path);

        Assert.Equal((int)TigerSqlCmdExitCode.OutputFailed, result.ExitCode);
        Assert.Contains("output path", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(path));
    }
}
