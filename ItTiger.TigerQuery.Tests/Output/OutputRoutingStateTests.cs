using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Events;
using ItTiger.TigerQuery.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace ItTiger.TigerQuery.Tests.Output;

/// <summary>
/// Covers the run-scoped routing state machine: which channel each directive moves,
/// when callbacks are used or suppressed, and what may never reach a file.
/// </summary>
public sealed class OutputRoutingStateTests
{
    [Fact]
    public async Task WithoutRoutesEverythingStillReachesTheCallbacks()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["A"], ["1"]);
            emission.ServerMessage("printed", 0);
        };

        var result = await host.RunAsync("SELECT 1;\r\nGO\r\n");

        Assert.Equal(ExecutionResultCode.Success, result.ResultCode);
        Assert.Single(host.ResultSetCallbacks);
        Assert.Single(host.MessageCallbacks);
        Assert.Empty(host.ProducedFiles());
    }

    [Fact]
    public async Task CallbacksAreUsedBeforeRedirectionAndSuppressedAfterIt()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["A"], [emission.BatchNumber]);

        await host.RunAsync(
            "SELECT 1;\r\nGO\r\n"
            + ":Out report.csv\r\n"
            + "SELECT 2;\r\nGO\r\n");

        var callback = Assert.Single(host.ResultSetCallbacks);
        Assert.Equal(1, callback.BatchNumber);
        Assert.Equal("A\r\n2\r\n", host.ReadText("report.csv"));
    }

    [Fact]
    public async Task OutOnlyMovesResultSetsUnderResultSetsOnly()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["A"], ["1"]);
            emission.ServerMessage("printed", 0);
            emission.ServerMessage("failed", 16);
        };

        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(["report.csv"], host.ProducedFiles());
        Assert.Equal(
            ["printed", "failed"],
            host.MessageCallbacks.Select(entry => entry.Message.Text));
    }

    [Fact]
    public async Task OutAlsoMovesNormalMessagesUnderResultSetsAndNormalMessages()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["A"], ["1"]);
            emission.ServerMessage("printed", 0);
            emission.ServerMessage("informational", 10);
            emission.ServerMessage("failed", 16);
        };

        var routing = new OutputRoutingOptions
        {
            OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages
        };
        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n", routing: routing);

        Assert.Equal(["report.csv", "report.csv.messages.log"], host.ProducedFiles());
        Assert.Equal("A\r\n1\r\n", host.ReadText("report.csv"));
        Assert.Equal("printed\r\ninformational\r\n", host.ReadText("report.csv.messages.log"));

        // Errors remain controlled independently by :Error.
        var callback = Assert.Single(host.MessageCallbacks);
        Assert.Equal("failed", callback.Message.Text);
    }

    [Fact]
    public async Task TheCompanionKeepsTheRequestedResultNameInPerResultSetMode()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["A"], ["1"]);
            emission.ServerMessage("printed", 0);
        };

        var routing = new OutputRoutingOptions
        {
            OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages,
            ResultSetFileMode = ResultSetFileMode.FilePerResultSet
        };
        await host.RunAsync(":Out report.csv\r\nSELECT 1;\r\nGO\r\n", routing: routing);

        Assert.Equal(
            ["report.csv.messages.log", "report_b0001_e0001_r0001.csv"],
            host.ProducedFiles());
    }

    [Fact]
    public async Task ErrorOnlyMovesTheErrorChannel()
    {
        using var host = new OutputTestHost();
        host.Emit = emission =>
        {
            emission.ResultSet(["A"], ["1"]);
            emission.ServerMessage("printed", 0);
            emission.ServerMessage("failed", 16);
        };

        var result = await host.RunAsync(":Error errors.log\r\nSELECT 1;\r\nGO\r\n");

        Assert.Equal(["errors.log"], host.ProducedFiles());
        Assert.Equal("failed\r\n", host.ReadText("errors.log"));
        Assert.Single(host.ResultSetCallbacks);
        var callback = Assert.Single(host.MessageCallbacks);
        Assert.Equal("printed", callback.Message.Text);

        // A severity 16 diagnostic still fails the batch under the default policy.
        Assert.Equal(1, result.FailedBatches);
    }

    [Theory]
    [InlineData((byte)0, false)]
    [InlineData((byte)10, false)]
    [InlineData((byte)11, true)]
    [InlineData((byte)16, true)]
    [InlineData((byte)20, true)]
    public async Task SeverityDecidesTheNormalOrErrorChannel(byte severity, bool isError)
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ServerMessage("diagnostic", severity);

        var routing = new OutputRoutingOptions
        {
            InitialOutPath = "report.csv",
            InitialErrorPath = "errors.log",
            OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages
        };
        await host.RunAsync("SELECT 1;\r\nGO\r\n", routing: routing);

        if (isError)
        {
            Assert.Equal("diagnostic\r\n", host.ReadText("errors.log"));
            Assert.False(host.Exists("report.csv.messages.log"));
        }
        else
        {
            Assert.Equal("diagnostic\r\n", host.ReadText("report.csv.messages.log"));
            Assert.False(host.Exists("errors.log"));
        }

        Assert.Empty(host.MessageCallbacks);
    }

    [Fact]
    public async Task EngineExceptionTextNeverEntersTheErrorFile()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => throw new InvalidOperationException("connection string secret=hunter2");

        var routing = new OutputRoutingOptions { InitialErrorPath = "errors.log" };
        var result = await host.RunAsync("SELECT 1;\r\nGO\r\n", routing: routing);

        Assert.Equal(ExecutionResultCode.UnhandledException, result.ResultCode);
        Assert.False(host.Exists("errors.log"));
        Assert.Empty(host.ProducedFiles());

        // It still reaches the application callback and the logger.
        var callback = Assert.Single(host.MessageCallbacks);
        Assert.Contains("hunter2", callback.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationTextNeverEntersTheErrorFile()
    {
        using var cancellation = new CancellationTokenSource();
        using var host = new OutputTestHost();
        host.Emit = _ =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        };

        var options = host.BuildOptions(routing: new OutputRoutingOptions { InitialErrorPath = "errors.log" });
        var engine = host.CreateEngine(options);

        var result = await engine.RunFromStringAsync("SELECT 1;\r\nGO\r\n", cancellation.Token);

        Assert.Equal(ExecutionResultCode.UserCancelled, result.ResultCode);
        Assert.Empty(host.ProducedFiles());
    }

    [Fact]
    public async Task TheLoggerStillSeesRedirectedMessages()
    {
        using var host = new OutputTestHost();
        var logger = new RecordingLogger();
        host.Emit = emission =>
        {
            emission.ServerMessage("printed", 0);
            emission.ServerMessage("failed", 16);
        };

        var routing = new OutputRoutingOptions
        {
            InitialOutPath = "report.csv",
            InitialErrorPath = "errors.log",
            OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages
        };
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd,
            Logger = logger,
            OutputRouting = OutputTestHost.CloneWithBaseDirectory(routing, host.Directory)
        };

        await host.RunAsync("SELECT 1;\r\nGO\r\n", options);

        Assert.Contains(logger.Entries, entry => entry.Contains("printed", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheLatestDirectiveForAChannelWins()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["A"], [emission.BatchNumber]);

        await host.RunAsync(
            ":Out first.csv\r\nSELECT 1;\r\nGO\r\n"
            + ":Out second.csv\r\nSELECT 2;\r\nGO\r\n");

        Assert.Equal("A\r\n1\r\n", host.ReadText("first.csv"));
        Assert.Equal("A\r\n2\r\n", host.ReadText("second.csv"));
    }

    [Fact]
    public async Task ScriptDirectivesOverrideTheInitialPaths()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["A"], [emission.BatchNumber]);

        var routing = new OutputRoutingOptions { InitialOutPath = "initial.csv" };
        await host.RunAsync(
            "SELECT 1;\r\nGO\r\n"
            + ":Out script.csv\r\nSELECT 2;\r\nGO\r\n",
            routing: routing);

        Assert.Equal("A\r\n1\r\n", host.ReadText("initial.csv"));
        Assert.Equal("A\r\n2\r\n", host.ReadText("script.csv"));
    }

    [Fact]
    public async Task ReturningToAnEarlierPathContinuesItWithoutASecondBomOrHeader()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["A"], [emission.BatchNumber]);

        await host.RunAsync(
            ":Out first.csv\r\nSELECT 1;\r\nGO\r\n"
            + ":Out second.csv\r\nSELECT 2;\r\nGO\r\n"
            + ":Out first.csv\r\nSELECT 3;\r\nGO\r\n");

        var bytes = host.ReadBytes("first.csv");
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));

        // Exactly one byte-order mark and exactly one header, with rows in run order.
        Assert.Equal(1, CountOccurrences(bytes, [0xEF, 0xBB, 0xBF]));
        Assert.Equal("A\r\n1\r\n3\r\n", host.ReadText("first.csv"));
    }

    private static int CountOccurrences(byte[] haystack, byte[] needle)
    {
        var count = 0;
        for (var index = 0; index + needle.Length <= haystack.Length; index++)
        {
            if (haystack.Skip(index).Take(needle.Length).SequenceEqual(needle))
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task ADirectiveAfterBufferedSqlRoutesThatBatch()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["A"], ["1"]);

        await host.RunAsync("SELECT 1;\r\n:Out selected.csv\r\nGO\r\n");

        Assert.Equal(["selected.csv"], host.ProducedFiles());
        Assert.Empty(host.ResultSetCallbacks);
    }

    [Fact]
    public async Task DisabledScriptDirectivesProduceAClearParserError()
    {
        using var host = new OutputTestHost();
        var routing = new OutputRoutingOptions { AllowScriptOutputDirectives = false };
        var options = host.BuildOptions(routing: routing);
        var engine = host.CreateEngine(options);

        var exception = await Assert.ThrowsAsync<TigerQueryException>(
            () => engine.RunFromStringAsync(
                ":Out report.csv\r\nSELECT 1;\r\nGO\r\n",
                TestContext.Current.CancellationToken));

        Assert.Equal("Script output directives are disabled; :Out is not permitted.", exception.Message);
        Assert.Empty(host.ProducedFiles());
    }

    [Fact]
    public async Task DisablingDirectivesDoesNotDisableInitialPaths()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["A"], ["1"]);

        var routing = new OutputRoutingOptions
        {
            AllowScriptOutputDirectives = false,
            InitialOutPath = "initial.csv"
        };
        await host.RunAsync("SELECT 1;\r\nGO\r\n", routing: routing);

        Assert.Equal("A\r\n1\r\n", host.ReadText("initial.csv"));
    }

    [Fact]
    public async Task ProgressAndLifecycleCallbacksAreNeverRedirected()
    {
        using var host = new OutputTestHost();
        host.Emit = emission => emission.ResultSet(["A"], ["1"]);

        var routing = new OutputRoutingOptions
        {
            InitialOutPath = "report.csv",
            InitialErrorPath = "errors.log",
            OutBehavior = OutDirectiveBehavior.ResultSetsAndNormalMessages
        };
        await host.RunAsync("SELECT 1;\r\nGO\r\n", routing: routing);

        Assert.Contains("start:1:1", host.Events);
        Assert.Contains("end:1:1:True", host.Events);
        Assert.Equal("A\r\n1\r\n", host.ReadText("report.csv"));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
        }
    }
}
