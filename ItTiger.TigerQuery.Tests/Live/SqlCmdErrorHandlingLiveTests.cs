using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Events;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Proves the <c>:ON ERROR</c> contract against a real SQL Server, which is the only
/// place the provider's actual error transport can be exercised. Each test records the
/// SQL side effects its script produced so that "later batches did not run" is asserted
/// rather than assumed.
/// </summary>
[Collection(LiveTestCollection.Name)]
public sealed class SqlCmdErrorHandlingLiveTests
{
    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ExitOnError_RaiserrorSeverity16_StopsAndFailsTheRun(
        TigerQueryExecutionMode executionMode)
    {
        await using var scratch = await ScratchTable.CreateAsync();
        var run = await scratch.RunAsync(executionMode, $"""
            :ON ERROR EXIT
            INSERT INTO {scratch.TableName} (Marker) VALUES ('first');
            GO
            RAISERROR('Deliberate severity 16.', 16, 7);
            GO
            INSERT INTO {scratch.TableName} (Marker) VALUES ('later');
            GO
            """);

        Assert.Equal(ExecutionResultCode.BatchFailed, run.Result.ResultCode);
        Assert.Equal(1, run.Result.FailedBatches);
        Assert.Equal(1, run.Result.ExecutedBatches);
        Assert.Equal(["first"], await scratch.ReadMarkersAsync());

        Assert.Equal(
            [
                "start:1:1",
                "end:1:1:True",
                "start:2:1",
                "error:Deliberate severity 16.",
                "end:2:1:False"
            ],
            run.Events);

        var diagnostic = Assert.Single(run.Errors);
        Assert.Equal(50000, diagnostic.Number);
        Assert.Equal(16, diagnostic.Severity);
        Assert.Equal(7, diagnostic.State);
        Assert.Equal(1, diagnostic.LineNumber);

        var batchError = Assert.IsType<SqlBatchErrorException>(run.Result.Exception);
        Assert.Equal("Deliberate severity 16.", Assert.Single(batchError.Errors).Text);
        Assert.Equal(executionMode == TigerQueryExecutionMode.Prepared ? 1 : 0, run.PlanReadyCount);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ExitOnError_Throw_StopsAndFailsTheRun(TigerQueryExecutionMode executionMode)
    {
        await using var scratch = await ScratchTable.CreateAsync();
        var run = await scratch.RunAsync(executionMode, $"""
            :ON ERROR EXIT
            INSERT INTO {scratch.TableName} (Marker) VALUES ('first');
            GO
            THROW 51000, 'Deliberate throw.', 3;
            GO
            INSERT INTO {scratch.TableName} (Marker) VALUES ('later');
            GO
            """);

        Assert.Equal(ExecutionResultCode.BatchFailed, run.Result.ResultCode);
        Assert.Equal(1, run.Result.FailedBatches);
        Assert.Equal(1, run.Result.ExecutedBatches);
        Assert.Equal(["first"], await scratch.ReadMarkersAsync());
        Assert.DoesNotContain("start:3:1", run.Events);

        var diagnostic = Assert.Single(run.Errors);
        Assert.Equal(51000, diagnostic.Number);
        Assert.Equal(16, diagnostic.Severity);
        Assert.Equal(3, diagnostic.State);
        Assert.Equal("Deliberate throw.", diagnostic.Text);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ExitOnError_CompilationError_StopsAndFailsTheRun(
        TigerQueryExecutionMode executionMode)
    {
        await using var scratch = await ScratchTable.CreateAsync();
        var run = await scratch.RunAsync(executionMode, $"""
            :ON ERROR EXIT
            INSERT INTO {scratch.TableName} (Marker) VALUES ('first');
            GO
            SELECT * FROM dbo.NoSuchTableAnywhere;
            GO
            INSERT INTO {scratch.TableName} (Marker) VALUES ('later');
            GO
            """);

        Assert.Equal(ExecutionResultCode.BatchFailed, run.Result.ResultCode);
        Assert.Equal(1, run.Result.FailedBatches);
        Assert.Equal(["first"], await scratch.ReadMarkersAsync());
        Assert.DoesNotContain("start:3:1", run.Events);
        Assert.Contains(run.Errors, error => error.Number == 208);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task IgnoreOnError_RaiserrorSeverity16_CountsTheFailureAndContinues(
        TigerQueryExecutionMode executionMode)
    {
        await using var scratch = await ScratchTable.CreateAsync();
        var run = await scratch.RunAsync(executionMode, $"""
            :ON ERROR IGNORE
            INSERT INTO {scratch.TableName} (Marker) VALUES ('first');
            GO
            RAISERROR('Deliberate severity 16.', 16, 7);
            GO
            INSERT INTO {scratch.TableName} (Marker) VALUES ('later');
            GO
            """);

        // Documented compatibility: an ignored failed batch keeps the run successful.
        Assert.Equal(ExecutionResultCode.Success, run.Result.ResultCode);
        Assert.Equal(1, run.Result.FailedBatches);
        Assert.Equal(2, run.Result.ExecutedBatches);
        Assert.Equal(["first", "later"], await scratch.ReadMarkersAsync());

        Assert.Equal(
            [
                "start:1:1",
                "end:1:1:True",
                "start:2:1",
                "error:Deliberate severity 16.",
                "end:2:1:False",
                "start:3:1",
                "end:3:1:True"
            ],
            run.Events);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task IgnoreOnError_Throw_CountsTheFailureAndContinues(
        TigerQueryExecutionMode executionMode)
    {
        await using var scratch = await ScratchTable.CreateAsync();
        var run = await scratch.RunAsync(executionMode, $"""
            :ON ERROR IGNORE
            INSERT INTO {scratch.TableName} (Marker) VALUES ('first');
            GO
            THROW 51000, 'Deliberate throw.', 3;
            GO
            INSERT INTO {scratch.TableName} (Marker) VALUES ('later');
            GO
            """);

        Assert.Equal(ExecutionResultCode.Success, run.Result.ResultCode);
        Assert.Equal(1, run.Result.FailedBatches);
        Assert.Equal(2, run.Result.ExecutedBatches);
        Assert.Equal(["first", "later"], await scratch.ReadMarkersAsync());
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ExitOnError_FailingRepeatIteration_StopsTheRemainingIterations(
        TigerQueryExecutionMode executionMode)
    {
        await using var scratch = await ScratchTable.CreateAsync();
        var run = await scratch.RunAsync(executionMode, $"""
            :ON ERROR EXIT
            INSERT INTO {scratch.TableName} (Marker) VALUES ('iteration');
            IF (SELECT COUNT(*) FROM {scratch.TableName} WHERE Marker = 'iteration') = 2
                RAISERROR('Second iteration failed.', 16, 1);
            GO 4
            INSERT INTO {scratch.TableName} (Marker) VALUES ('later');
            GO
            """);

        Assert.Equal(ExecutionResultCode.BatchFailed, run.Result.ResultCode);
        Assert.Equal(1, run.Result.FailedBatches);
        Assert.Equal(1, run.Result.ExecutedBatches);
        Assert.Equal(["iteration", "iteration"], await scratch.ReadMarkersAsync());

        Assert.Equal(
            [
                "start:1:1",
                "end:1:1:True",
                "start:1:2",
                "error:Second iteration failed.",
                "end:1:2:False"
            ],
            run.Events);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task IgnoreOnError_FailingRepeatIteration_RunsTheRemainingIterations(
        TigerQueryExecutionMode executionMode)
    {
        await using var scratch = await ScratchTable.CreateAsync();
        var run = await scratch.RunAsync(executionMode, $"""
            :ON ERROR IGNORE
            INSERT INTO {scratch.TableName} (Marker) VALUES ('iteration');
            IF (SELECT COUNT(*) FROM {scratch.TableName} WHERE Marker = 'iteration') = 2
                RAISERROR('Second iteration failed.', 16, 1);
            GO 3
            """);

        Assert.Equal(ExecutionResultCode.Success, run.Result.ResultCode);
        Assert.Equal(1, run.Result.FailedBatches);
        Assert.Equal(2, run.Result.ExecutedBatches);
        Assert.Equal(3, (await scratch.ReadMarkersAsync()).Count);
    }

    [Theory]
    [InlineData(TigerQueryExecutionMode.Streaming)]
    [InlineData(TigerQueryExecutionMode.Prepared)]
    public async Task ASuccessfulScriptStillSucceedsAndReportsNoFailures(
        TigerQueryExecutionMode executionMode)
    {
        await using var scratch = await ScratchTable.CreateAsync();
        var run = await scratch.RunAsync(executionMode, $"""
            :ON ERROR EXIT
            PRINT 'informational only';
            INSERT INTO {scratch.TableName} (Marker) VALUES ('first');
            GO
            INSERT INTO {scratch.TableName} (Marker) VALUES ('second');
            GO 2
            """);

        Assert.Equal(ExecutionResultCode.Success, run.Result.ResultCode);
        Assert.Equal(0, run.Result.FailedBatches);
        Assert.Equal(3, run.Result.ExecutedBatches);
        Assert.Null(run.Result.Exception);
        Assert.Empty(run.Errors);
        Assert.Contains(run.Informational, message => message.Text.Contains("informational only", StringComparison.Ordinal));
        Assert.Equal(["first", "second", "second"], await scratch.ReadMarkersAsync());
    }

    [Fact]
    public async Task PreparedAndStreamingAgreeOnOutcomeEventsAndSideEffects()
    {
        await using var streamingScratch = await ScratchTable.CreateAsync();
        await using var preparedScratch = await ScratchTable.CreateAsync();

        var streaming = await streamingScratch.RunAsync(
            TigerQueryExecutionMode.Streaming,
            Script(streamingScratch.TableName));
        var prepared = await preparedScratch.RunAsync(
            TigerQueryExecutionMode.Prepared,
            Script(preparedScratch.TableName));

        Assert.Equal(streaming.Result.ResultCode, prepared.Result.ResultCode);
        Assert.Equal(streaming.Result.FailedBatches, prepared.Result.FailedBatches);
        Assert.Equal(streaming.Result.ExecutedBatches, prepared.Result.ExecutedBatches);
        Assert.Equal(streaming.Events, prepared.Events);
        Assert.Equal(
            streaming.Errors.Select(error => (error.Number, error.Severity, error.State, error.Text)),
            prepared.Errors.Select(error => (error.Number, error.Severity, error.State, error.Text)));
        Assert.Equal(
            await streamingScratch.ReadMarkersAsync(),
            await preparedScratch.ReadMarkersAsync());

        // Only prepared mode announces a plan, and it does so before any batch runs.
        Assert.Equal(0, streaming.PlanReadyCount);
        Assert.Equal(1, prepared.PlanReadyCount);
        Assert.Equal(3, prepared.PlanLogicalBatchCount);

        static string Script(string table) => $"""
            :ON ERROR EXIT
            INSERT INTO {table} (Marker) VALUES ('first');
            GO
            RAISERROR('Deliberate severity 16.', 16, 7);
            GO
            INSERT INTO {table} (Marker) VALUES ('later');
            GO
            """;
    }

    /// <summary>
    /// A real table outside the engine's own session, so side effects survive the run
    /// and "the later batch never executed" is observable.
    /// </summary>
    private sealed class ScratchTable : IAsyncDisposable
    {
        private readonly SqlServerE2eTestDatabase database;
        private readonly string connectionString;
        private readonly string bareName;

        private ScratchTable(
            SqlServerE2eTestDatabase database,
            string connectionString,
            string bareName)
        {
            this.database = database;
            this.connectionString = connectionString;
            this.bareName = bareName;
        }

        public string TableName => $"dbo.[{bareName}]";

        public static async Task<ScratchTable> CreateAsync()
        {
            var database = await SqlServerTestEnvironment.CreateDatabaseAsync();
            var bareName = $"TigerQueryLive_{Guid.NewGuid():N}";
            var scratch = new ScratchTable(database, database.ConnectionString, bareName);

            try
            {
                await scratch.ExecuteAsync(
                    $"CREATE TABLE dbo.[{bareName}] (Id INT IDENTITY(1,1) PRIMARY KEY, Marker NVARCHAR(100) NOT NULL);");
                return scratch;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async Task<LiveRun> RunAsync(TigerQueryExecutionMode executionMode, string script)
        {
            var run = new LiveRun();
            var options = new TigerQueryEngineOptions
            {
                ConnectionString = connectionString,
                ExecutionMode = executionMode,
                Mode = SqlCmdMode.SqlCmdEx,
                OnExecutionPlanReady = plan =>
                {
                    run.PlanReadyCount++;
                    run.PlanLogicalBatchCount = plan.LogicalBatchCount;
                },
                OnBatchStart = batch => run.Events.Add($"start:{batch.BatchNumber}:{batch.ExecutionIndex}"),
                OnBatchEnd = batch =>
                    run.Events.Add($"end:{batch.BatchNumber}:{batch.ExecutionIndex}:{batch.Success}"),
                OnMessage = (message, _) =>
                {
                    if (message.IsError)
                    {
                        run.Errors.Add(message);
                        run.Events.Add($"error:{message.Text}");
                    }
                    else
                    {
                        run.Informational.Add(message);
                    }
                }
            };

            var engine = new TigerQueryEngine(options);
            run.Result = await engine.RunFromStringAsync(script, TestContext.Current.CancellationToken);
            return run;
        }

        public async Task<IReadOnlyList<string>> ReadMarkersAsync()
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT Marker FROM dbo.[{bareName}] ORDER BY Id;";

            var markers = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                markers.Add(reader.GetString(0));

            return markers;
        }

        public ValueTask DisposeAsync() => database.DisposeAsync();

        private async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class LiveRun
    {
        public ExecutionResult Result { get; set; } = new();

        public List<string> Events { get; } = [];

        public List<SqlCmdMessage> Errors { get; } = [];

        public List<SqlCmdMessage> Informational { get; } = [];

        public int PlanReadyCount { get; set; }

        public int PlanLogicalBatchCount { get; set; }
    }
}
