using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;

namespace ItTiger.TigerQuery.Tests.Parser;

/// <summary>
/// Covers recognition, syntax validation, and ordering of the <c>:Out</c> and
/// <c>:Error</c> output directives, which the parser represents as internal
/// execution steps.
/// </summary>
public sealed class SqlCmdOutputDirectiveParserTests
{
    [Theory]
    // Casing.
    [InlineData(":out results.csv\r\n", "results.csv")]
    [InlineData(":OUT results.csv\r\n", "results.csv")]
    [InlineData(":OuT results.csv\r\n", "results.csv")]
    // Whitespace and line endings.
    [InlineData("\t:Out \t results.csv \t\r\n", "results.csv")]
    [InlineData(":Out results.csv\n", "results.csv")]
    [InlineData(":Out results.csv\r", "results.csv")]
    [InlineData(":Out results.csv", "results.csv")]
    // Trailing single-line comment.
    [InlineData(":Out results.csv -- destination\r\n", "results.csv")]
    // Quoted paths.
    [InlineData(":Out \"directory with spaces/results.csv\"\r\n", "directory with spaces/results.csv")]
    [InlineData(":Out \"quo\"\"ted.csv\"\r\n", "quo\"ted.csv")]
    [InlineData(":Out \"results.csv\"", "results.csv")]
    [InlineData(":Out \"results.csv\" -- destination\r\n", "results.csv")]
    // Absolute and relative paths.
    [InlineData(":Out C:\\temp\\results.csv\r\n", "C:\\temp\\results.csv")]
    [InlineData(":Out /var/log/results.csv\r\n", "/var/log/results.csv")]
    [InlineData(":Out ..\\up\\results.csv\r\n", "..\\up\\results.csv")]
    [InlineData(":Out ./down/results.csv\r\n", "./down/results.csv")]
    public async Task OutDirectiveAcceptsSupportedSyntax(string script, string expectedPath)
    {
        foreach (var mode in new[] { SqlCmdMode.SqlCmd, SqlCmdMode.SqlCmdEx })
        {
            var steps = await TestHelper.ParseStepsAsync(
                script,
                new TigerQueryEngineOptions { Mode = mode });

            var step = Assert.IsType<SetOutRouteStep>(Assert.Single(RouteSteps(steps)));
            Assert.Equal(expectedPath, step.Directive.Path);
            Assert.Same(steps[0], step);
        }
    }

    [Theory]
    [InlineData(":error errors.log\r\n", "errors.log")]
    [InlineData(":ERROR errors.log\r\n", "errors.log")]
    [InlineData(":Error \"log directory/errors.log\"\r\n", "log directory/errors.log")]
    [InlineData(":Error errors.log -- destination\r\n", "errors.log")]
    [InlineData(":Error /var/log/errors.log", "/var/log/errors.log")]
    public async Task ErrorDirectiveAcceptsSupportedSyntax(string script, string expectedPath)
    {
        var steps = await TestHelper.ParseStepsAsync(script);

        var step = Assert.IsType<SetErrorRouteStep>(Assert.Single(RouteSteps(steps)));
        Assert.Equal(expectedPath, step.Directive.Path);
        Assert.Same(steps[0], step);
    }

    [Theory]
    [InlineData(":Out $(Name).csv\r\n", "report.csv")]
    [InlineData(":Out $(Dir)/$(Name).csv\r\n", "exports/report.csv")]
    [InlineData(":Out \"$(Dir)/with spaces/$(Name).csv\"\r\n", "exports/with spaces/report.csv")]
    [InlineData(":Error $(Dir)/errors.log\r\n", "exports/errors.log")]
    // Undefined references stay literal, matching ordinary expansion.
    [InlineData(":Out $(Missing).csv\r\n", "$(Missing).csv")]
    public async Task DirectivePathExpandsVariables(string script, string expectedPath)
    {
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd,
            Variables = new Dictionary<string, string>
            {
                ["Name"] = "report",
                ["Dir"] = "exports"
            }
        };

        var steps = await TestHelper.ParseStepsAsync(script, options);

        var directive = Assert.Single(RouteSteps(steps)) switch
        {
            SetOutRouteStep outStep => outStep.Directive,
            SetErrorRouteStep errorStep => errorStep.Directive,
            var other => throw new InvalidOperationException($"Unexpected step {other.GetType().Name}.")
        };
        Assert.Equal(expectedPath, directive.Path);
    }

    [Fact]
    public async Task DirectivePathIsExpandedAtItsSourcePosition()
    {
        var script = """
            :setvar Target first.csv
            :Out $(Target)
            :setvar Target second.csv
            :Out $(Target)
            """;

        var steps = await TestHelper.ParseStepsAsync(script);

        Assert.Equal(["out:first.csv", "out:second.csv"], TestHelper.Describe(steps));
    }

    [Theory]
    [InlineData(":Out\r\n")]
    [InlineData(":Out \r\n")]
    [InlineData(":Out results.csv extra\r\n")]
    [InlineData(":Out results.csv extra more\r\n")]
    [InlineData(":Out \"\"\r\n")]
    [InlineData(":Out \"   \"\r\n")]
    [InlineData(":Out \"results.csv\" extra\r\n")]
    [InlineData(":Out \"results.csv\" \"second.csv\"\r\n")]
    [InlineData(":Out \"results.csv\" /* comment */\r\n")]
    [InlineData(":Out partly\"quoted.csv\"\r\n")]
    [InlineData(":Out results.csv /* comment */\r\n")]
    public async Task OutDirectiveRejectsUnsupportedSyntax(string script)
    {
        foreach (var mode in new[] { SqlCmdMode.SqlCmd, SqlCmdMode.SqlCmdEx })
        {
            var exception = await Assert.ThrowsAsync<TigerQueryException>(
                () => TestHelper.ParseStepsAsync(
                    script,
                    new TigerQueryEngineOptions { Mode = mode }));

            Assert.Equal("Incorrect syntax was encountered while parsing :Out.", exception.Message);
        }
    }

    [Theory]
    [InlineData(":Error\r\n")]
    [InlineData(":Error errors.log extra\r\n")]
    [InlineData(":Error \"\"\r\n")]
    [InlineData(":Error \"errors.log\" extra\r\n")]
    public async Task ErrorDirectiveRejectsUnsupportedSyntax(string script)
    {
        var exception = await Assert.ThrowsAsync<TigerQueryException>(
            () => TestHelper.ParseStepsAsync(script));

        Assert.Equal("Incorrect syntax was encountered while parsing :Error.", exception.Message);
    }

    [Fact]
    public async Task UnterminatedQuotedDirectivePathFails()
    {
        await Assert.ThrowsAsync<TigerQueryException>(
            () => TestHelper.ParseStepsAsync(":Out \"results.csv\r\n"));
    }

    [Fact]
    public async Task DirectivesRemainSqlTextInNormalMode()
    {
        var script = ":Out results.csv\r\n:Error errors.log\r\nSELECT 1;\r\nGO\r\n";

        var steps = await TestHelper.ParseStepsAsync(
            script,
            new TigerQueryEngineOptions { Mode = SqlCmdMode.Normal });

        var step = Assert.IsType<ExecuteBatchStep>(Assert.Single(steps));
        Assert.Equal(
            ":Out results.csv\r\n:Error errors.log\r\nSELECT 1;\r\n",
            step.Execution.Batch.Text);
    }

    [Fact]
    public async Task StepsPreserveExactSourceOrder()
    {
        var script = ":Out first.csv\r\n"
            + "SELECT 1;\r\nGO\r\n"
            + ":Error errors.log\r\n"
            + "SELECT 2;\r\nGO 2\r\n"
            + "SELECT 3;\r\n"
            + ":Out second.csv\r\nGO\r\n"
            + "SELECT 4;\r\n";

        var steps = await TestHelper.ParseStepsAsync(script);

        Assert.Equal(
            [
                "out:first.csv",
                "batch:SELECT 1;:1",
                "error:errors.log",
                "batch:SELECT 2;:2",
                "out:second.csv",
                "batch:SELECT 3;:1",
                "batch:SELECT 4;:1"
            ],
            TestHelper.Describe(steps));
    }

    [Fact]
    public async Task DirectiveAfterBufferedSqlIsOrderedBeforeThatBatch()
    {
        var script = "SELECT 1;\r\n:Out selected.csv\r\nGO\r\n";

        var steps = await TestHelper.ParseStepsAsync(script);

        Assert.Equal(
            ["out:selected.csv", "batch:SELECT 1;:1"],
            TestHelper.Describe(steps));
    }

    [Fact]
    public async Task DirectiveAfterBufferedSqlIsNotMovedAcrossAnEarlierBatch()
    {
        var script = "SELECT 1;\r\nGO\r\nSELECT 2;\r\n:Out selected.csv\r\nGO\r\n";

        var steps = await TestHelper.ParseStepsAsync(script);

        Assert.Equal(
            ["batch:SELECT 1;:1", "out:selected.csv", "batch:SELECT 2;:1"],
            TestHelper.Describe(steps));
    }

    [Fact]
    public async Task DirectiveBeforeFinalUnterminatedBatchIsOrderedBeforeIt()
    {
        var script = "SELECT 1;\r\n:Out selected.csv\r\nSELECT 2;\r\n";

        var steps = await TestHelper.ParseStepsAsync(script);

        Assert.Equal(
            ["out:selected.csv", "batch:SELECT 1;\r\nSELECT 2;:1"],
            TestHelper.Describe(steps));
    }

    [Fact]
    public async Task ConsecutiveDirectivesPreserveTheirOrder()
    {
        var script = "SELECT 1;\r\n"
            + ":Out first.csv\r\n"
            + ":Error errors.log\r\n"
            + ":Out second.csv\r\n"
            + "GO\r\n";

        var steps = await TestHelper.ParseStepsAsync(script);

        Assert.Equal(
            [
                "out:first.csv",
                "error:errors.log",
                "out:second.csv",
                "batch:SELECT 1;:1"
            ],
            TestHelper.Describe(steps));
    }

    [Fact]
    public async Task TrailingDirectiveWithoutFollowingBatchIsRetained()
    {
        var script = "SELECT 1;\r\nGO\r\n:Out trailing.csv\r\n";

        var steps = await TestHelper.ParseStepsAsync(script);

        Assert.Equal(
            ["batch:SELECT 1;:1", "out:trailing.csv"],
            TestHelper.Describe(steps));
    }

    [Fact]
    public async Task RepeatedRoutesToTheSamePathAreAllRetained()
    {
        var script = """
            :Out first.csv
            SELECT 1;
            GO
            :Out second.csv
            SELECT 2;
            GO
            :Out first.csv
            SELECT 3;
            GO
            """;

        var steps = await TestHelper.ParseStepsAsync(script);

        Assert.Equal(
            [
                "out:first.csv",
                "batch:SELECT 1;:1",
                "out:second.csv",
                "batch:SELECT 2;:1",
                "out:first.csv",
                "batch:SELECT 3;:1"
            ],
            TestHelper.Describe(steps));
    }

    [Fact]
    public async Task DirectivesDoNotDisturbOnErrorSnapshots()
    {
        var script = """
            :ON ERROR IGNORE
            :Out first.csv
            SELECT 1;
            GO
            SELECT 2;
            :ON ERROR EXIT
            :Error errors.log
            GO
            """;

        var steps = await TestHelper.ParseStepsAsync(script);

        Assert.Equal(
            [
                "out:first.csv",
                "batch:SELECT 1;:1",
                "error:errors.log",
                "batch:SELECT 2;:1"
            ],
            TestHelper.Describe(steps));
        Assert.Equal(
            [true, false],
            steps.OfType<ExecuteBatchStep>().Select(step => step.Execution.ContinueOnError));
    }

    [Theory]
    [InlineData("SELECT 1;\r\nGO\r\nSELECT 2;\r\nGO 2\r\n")]
    [InlineData(":Out first.csv\r\nSELECT 1;\r\nGO\r\n:Error errors.log\r\nSELECT 2;\r\nGO 2\r\n")]
    [InlineData("SELECT 1;\r\n:Out first.csv\r\nGO\r\nSELECT 2;\r\nGO 2\r\n:Out last.csv\r\n")]
    public async Task ReadBatchesAsyncReturnsTheSameBatchesWithoutRouteSteps(string script)
    {
        var options = new TigerQueryEngineOptions { Mode = SqlCmdMode.SqlCmd };

        var steps = await TestHelper.ParseStepsAsync(script, options);
        var batches = await TestHelper.ParseBatchesAsync(script, options);

        var stepBatches = steps.OfType<ExecuteBatchStep>().Select(step => step.Execution.Batch).ToList();
        Assert.Equal(stepBatches.Count, batches.Count);
        Assert.Equal(
            stepBatches.Select(batch => (batch.Text, batch.StartLine, batch.StartColumn, batch.ExecCount)),
            batches.Select(batch => (batch.Text, batch.StartLine, batch.StartColumn, batch.ExecCount)));
    }

    [Fact]
    public async Task ReadBatchesAsyncStillValidatesDirectiveSyntax()
    {
        var exception = await Assert.ThrowsAsync<TigerQueryException>(
            () => TestHelper.ParseBatchesAsync(
                "SELECT 1;\r\nGO\r\n:Out one.csv two.csv\r\n",
                new TigerQueryEngineOptions { Mode = SqlCmdMode.SqlCmd }));

        Assert.Equal("Incorrect syntax was encountered while parsing :Out.", exception.Message);
    }

    [Fact]
    public async Task UnknownColonDirectivesStillFail()
    {
        var exception = await Assert.ThrowsAsync<TigerQueryException>(
            () => TestHelper.ParseStepsAsync(":Outdated results.csv\r\n"));

        Assert.Equal("Incorrect syntax near ':'.", exception.Message);
    }

    [Fact]
    public async Task DirectiveRecordsItsSourcePosition()
    {
        var script = "SELECT 1;\r\nGO\r\n:Out results.csv\r\n";

        var steps = await TestHelper.ParseStepsAsync(script);

        var step = Assert.IsType<SetOutRouteStep>(steps[^1]);
        Assert.Equal(3, step.Directive.Line);
        Assert.Equal(1, step.Directive.Column);
    }

    private static List<ExecutionStep> RouteSteps(IEnumerable<ExecutionStep> steps)
    {
        return [.. steps.Where(step => step is SetOutRouteStep or SetErrorRouteStep)];
    }
}
