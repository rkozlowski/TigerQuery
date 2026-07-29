using ItTiger.TigerQuery.Engine;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Engine;

public sealed class PreparedExecutionPlanTests
{
    [Theory]
    [InlineData("", 0, 0L)]
    [InlineData("SELECT 1", 1, 1L)]
    [InlineData("SELECT 1\r\nGO 3\r\n", 1, 3L)]
    [InlineData("SELECT 1\r\nGO 2\r\nSELECT 2\r\nGO 3\r\n", 2, 5L)]
    [InlineData("SELECT 1\r\nGO 0\r\nSELECT 2\r\nGO -2\r\n", 2, 0L)]
    public async Task PreparationCalculatesLogicalAndExecutionCounts(
        string script,
        int logicalBatchCount,
        long totalExecutionCount)
    {
        var plan = await PrepareAsync(script);

        Assert.Equal(logicalBatchCount, plan.LogicalBatchCount);
        Assert.Equal(totalExecutionCount, plan.TotalExecutionCount);
    }

    [Fact]
    public async Task VariableBasedRepeatCountIsIncluded()
    {
        var plan = await PrepareAsync(
            ":setvar RepeatCount 4\r\nGO\r\nSELECT 1\r\nGO $(RepeatCount)\r\n");

        var executionBatch = Assert.Single(plan.Batches);
        Assert.Equal(4, executionBatch.Batch.ExecCount);
        Assert.Equal(4L, plan.TotalExecutionCount);
    }

    [Fact]
    public async Task RepeatCountDoesNotExpandPlanEntries()
    {
        var plan = await PrepareAsync("SELECT 1\r\nGO 100\r\n");

        var executionBatch = Assert.Single(plan.Batches);
        Assert.Equal(100, executionBatch.Batch.ExecCount);
        Assert.Equal(100L, plan.TotalExecutionCount);
    }

    [Fact]
    public async Task TotalExecutionCountUsesLong()
    {
        var plan = await PrepareAsync(
            $"SELECT 1\r\nGO {int.MaxValue}\r\n"
            + $"SELECT 2\r\nGO {int.MaxValue}\r\n");

        Assert.Equal(2, plan.LogicalBatchCount);
        Assert.Equal(2L * int.MaxValue, plan.TotalExecutionCount);
        Assert.IsType<long>(plan.TotalExecutionCount);
    }

    [Fact]
    public async Task SetvarExpansionIsPreserved()
    {
        var plan = await PrepareAsync(
            ":setvar Value expanded\r\nGO\r\nPRINT '$(Value)';\r\nGO\r\n");

        var executionBatch = Assert.Single(plan.Batches);
        Assert.Contains("PRINT 'expanded';", executionBatch.Batch.Text);
    }

    [Fact]
    public async Task SqlCmdExProgrammaticVariablesRemainProtected()
    {
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmdEx,
            Variables = new Dictionary<string, string>
            {
                ["Value"] = "programmatic"
            }
        };

        var plan = await PrepareAsync(
            ":setvar Value script\r\nGO\r\nPRINT '$(Value)';\r\nGO\r\n",
            options);

        var executionBatch = Assert.Single(plan.Batches);
        Assert.Contains("PRINT 'programmatic';", executionBatch.Batch.Text);
    }

    [Fact]
    public async Task UnresolvedOrdinaryVariableRemainsLiteral()
    {
        var plan = await PrepareAsync("PRINT '$(Missing)';\r\nGO\r\n");

        var executionBatch = Assert.Single(plan.Batches);
        Assert.Contains("$(Missing)", executionBatch.Batch.Text);
    }

    [Fact]
    public async Task UnresolvedVariableUsedAsGoCountFailsPreparation()
    {
        await Assert.ThrowsAsync<TigerQueryException>(
            () => PrepareAsync("SELECT 1\r\nGO $(Missing)\r\n"));
    }

    [Fact]
    public async Task ContinueOnErrorIsCapturedForEachLogicalBatch()
    {
        var script = """
            :ON ERROR EXIT
            SELECT 1;
            GO
            :ON ERROR IGNORE
            SELECT 2;
            GO
            :ON ERROR EXIT
            SELECT 3;
            GO
            """;

        var plan = await PrepareAsync(script);

        Assert.Equal(
            [false, true, false],
            plan.Batches.Select(batch => batch.ContinueOnError));
    }

    [Fact]
    public async Task DirectiveBeforeTerminatingGoAffectsBufferedBatch()
    {
        var plan = await PrepareAsync(
            "SELECT 1;\r\n:ON ERROR IGNORE\r\nGO\r\n"
            + "SELECT 2;\r\n:ON ERROR EXIT\r\nGO\r\n");

        Assert.Equal(
            [true, false],
            plan.Batches.Select(batch => batch.ContinueOnError));
    }

    [Fact]
    public async Task SourceLineAndColumnArePreserved()
    {
        var plan = await PrepareAsync(
            ":setvar Value 1\r\nGO\r\n"
            + "  SELECT $(Value);\r\nGO\r\n");

        var executionBatch = Assert.Single(plan.Batches);
        Assert.Equal(3, executionBatch.Batch.StartLine);
        Assert.Equal(1, executionBatch.Batch.StartColumn);
    }

    private static async Task<PreparedExecutionPlan> PrepareAsync(
        string script,
        TigerQueryEngineOptions? options = null)
    {
        options ??= new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };

        await using var connection = new SqlConnection();
        var context = new QueryExecutionContext(options, connection);
        using var reader = new StringReader(script);
        var parser = new SqlCmdParser(reader, options, context);

        return await TigerQueryEngine.PrepareExecutionPlanAsync(
            parser,
            context,
            TestContext.Current.CancellationToken);
    }
}
