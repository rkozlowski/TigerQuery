
using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;

namespace ItTiger.TigerQuery.Tests.Parser;

public class SqlCmdParserTests
{
    [Fact]
    public async Task SucceedsWithGoWithRepeatCount()
    {
        var sql = "SELECT 1\nGO 3";
        var options = new TigerQueryEngineOptions
        {            
            Mode = SqlCmdMode.Normal
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);
        
        Assert.Single(batches);
        Assert.Equal(3, batches[0].ExecCount);
    }

    const string SqlGoVar = ":setvar cnt 3\nGo\nPRINT('Something');\ngo $(cnt)";

    [Fact]
    public async Task FailsWhenGoWithVarInNormalMode()
    {

        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.Normal
        };
        await Assert.ThrowsAsync<TigerQueryException>(() =>
            TestHelper.ParseBatchesAsync(SqlGoVar, options));        
    }

    [Fact]
    public async Task SucceedsWithGoWithVarInSqlCmdMode()
    {
        var variables = new Dictionary<string, string>();
        variables["cnt"] = "+7";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd,
            Variables = variables
        };
        var batches = await TestHelper.ParseBatchesAsync(SqlGoVar, options);
        Assert.Single(batches);
        Assert.Equal(3, batches[0].ExecCount);
    }

    [Fact]
    public async Task SucceedsWithGoWithVarInSqlCmdExMode()
    {
        var variables = new Dictionary<string, string>();
        variables["cnt"] = "+7";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmdEx,
            Variables = variables
        };
        var batches = await TestHelper.ParseBatchesAsync(SqlGoVar, options);
        Assert.Single(batches);
        Assert.Equal(7, batches[0].ExecCount);
    }

    [Fact]
    public async Task SucceedsWithOnErrorIgnore()
    {
        var sql = ":ON ERROR ignore\r\nGO\r\nPRINT('Something');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd,
            ContinueOnError = false
        };
        var (batches, context) = await TestHelper.ParseBatchesCtxAsync(sql, options);

        Assert.Single(batches);
        Assert.True(context.ContinueOnError);
    }

    [Fact]
    public async Task SucceedsWithOnErrorExit()
    {
        var sql = ":ON ERROR Exit\r\nGO\r\nPRINT('Something');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd,
            ContinueOnError = true
        };
        var (batches, context) = await TestHelper.ParseBatchesCtxAsync(sql, options);

        Assert.Single(batches);
        Assert.False(context.ContinueOnError);
    }

    [Fact]
    // Empty batches (only whitespace or line breaks) should be ignored even when followed by GO or GO <count>

    public async Task SucceedsWithEmptyBatches()
    {
        var sql = "PRINT('Start');\r\nGO\r\nGO   \r    go\n  Go  \n   GO 10000   \r\nPRINT('End');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    // Non-empty batches (even if visually sparse) with real line breaks form distinct batches and are repeated accordingly
    public async Task SucceedsWithNonEmptyBatches()
    {
        var sql = "PRINT('Start');\r\n\t\r\nGO\n\nGO   \r  \n\t\t\n\r  go\n  Go  \n\r\n\r\n   GO 10000   \r\nPRINT('End');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Equal(5, batches.Count);
    }


    [Fact]
    // GO keywords within comments should not be treated as batch separators.
    // This test includes single-line comments (`-- GO`) and nested multi-line comments
    // (`/* GO ... GO /* GO */ */`) to ensure the parser correctly skips them.
    // Only the real batch separators should count.
    public async Task SucceedsWithMultiLineComment()
    {
        var sql = "PRINT('Start');\r\n-- GO 7\r\nGO\r\n/*\r\nGO 15\r\n*/\r\nGO\r\n/*\r\nGO 4\r\nGO\r\n/*\r\nGO\r\n/* GO 17 -- */\r\n*/\r\nGO\r\n-- */\r\n"
                + "GO\r\nPRINT('End');\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Equal(4, batches.Count);
    }

    [Fact]
    // Validates that :setvar supports a complex, multi-line quoted value.
    //
    // This test includes:
    // - Tabs and mixed casing in the :setvar keyword
    // - A multi-line string enclosed in double quotes (continuing across lines)
    // - Escaped double quotes (e.g., ""xxx"")
    // - Embedded "GO" lines and a multi-line comment block
    //
    // None of the GO keywords inside the quoted value should trigger batch separation.
    // The final PRINT('$(Test)') must correctly resolve the variable to the full value.
    //
    // Expected: 4 batches — one before :setvar, one for :setvar, one for PRINT('$(Test)'), and one for PRINT('End')
    public async Task SucceedsWithSetvarWithMultiLineValue()
    {
        var sql = "PRINT('Start');\r\nGO\r\n\t:seTvaR\tteSt    \"\r\nGO\r\n\t\"\"xxx\"\"    \r\n  /*  \t\r\n\"\r\nGO\r\n"
        + "PRINT('$(Test)');\r\nGO\r\nPRINT('End');";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Equal(4, batches.Count);
        Assert.Equal("PRINT('\r\nGO\r\n\t\"xxx\"    \r\n  /*  \t\r\n');\r\n", batches[2].Text);
    }

    [Fact]
    // Ensures that 'GO' and comments inside single-quoted multi-line strings are not treated as batch separators
    public async Task SucceedsWithMultiLineSingleQuotedStringContainingGo()
    {
        var sql = "PRINT('Line1\nGO\n/* still inside string */\n''escaped quote''');\nGO";
        var options = new TigerQueryEngineOptions { Mode = SqlCmdMode.SqlCmd };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Single(batches);
        Assert.Contains("GO", batches[0].Text); // Confirm GO wasn’t interpreted
    }

    [Fact]
    // Ensures that bracketed identifiers containing GO or comment tokens are not misinterpreted
    public async Task SucceedsWithBracketedIdentifiersContainingGo()
    {
        var sql = "SELECT [Column\nGO\n/* not a batch separator */\nName];\nGO";
        var options = new TigerQueryEngineOptions { Mode = SqlCmdMode.SqlCmd };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Single(batches);
        Assert.Contains("[Column", batches[0].Text);
    }

    [Fact]
    // Validates that multiple variables are set and substituted correctly
    public async Task SubstitutesMultipleVariablesCorrectly()
    {
        var sql = ":SETVAR A Hello\n:setvar B \"World\"\nGO\nPRINT('$(A), $(B)!');\nGO";
        var options = new TigerQueryEngineOptions { Mode = SqlCmdMode.SqlCmd };
        var (batches, context) = await TestHelper.ParseBatchesCtxAsync(sql, options);

        Assert.Equal("Hello", context.Variables["A"].Value);
        Assert.Equal("World", context.Variables["B"].Value);
        Assert.Equal(2, batches.Count);
        Assert.Contains("'Hello, World!'", batches[1].Text);
    }

    [Fact]
    // Variables should not be substituted in normal mode
    public async Task DoesNotSubstituteVariableInNormalMode()
    {
        var sql = "PRINT('$(shouldNotChange)');\nGO";
        var options = new TigerQueryEngineOptions { Mode = SqlCmdMode.Normal };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Single(batches);
        Assert.Contains("$(shouldNotChange)", batches[0].Text);
    }


    const string SqlMultipleVariableOverrides = """
    :setvar x "Abc"
    :setvar y "123"
    GO
    PRINT('$(x)-$(y)');
    GO
    :setvar x "deF"
    :setvar y "456"
    GO
    PRINT('$(x)-$(y)');
    GO
    """;

    [Fact]
    // In SqlCmd mode, variables are substituted and :setvar overrides programmatic variables.
    public async Task NoVariableSubstitutionInSqlCmdMode()
    {
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd,
            Variables = new Dictionary<string, string> { ["x"] = "xXx" }
        };

        var batches = await TestHelper.ParseBatchesAsync(SqlMultipleVariableOverrides, options);

        Assert.Equal(4, batches.Count);
        Assert.Contains("PRINT('Abc-123')", batches[1].Text);
        Assert.Contains("PRINT('deF-456')", batches[3].Text);
    }

    [Fact]
    // In SqlCmdEx mode, variables are substituted but :setvar doesn't override programmatic variables.
    public async Task VariableSubstitutionInSqlCmdExMode()
    {
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmdEx,
            Variables = new Dictionary<string, string> { ["x"] = "xXx" }
        };

        var batches = await TestHelper.ParseBatchesAsync(SqlMultipleVariableOverrides, options);

        Assert.Equal(4, batches.Count);
        Assert.Equal("PRINT('xXx-123');\r\n", batches[1].Text);
        Assert.Equal("PRINT('xXx-456');\r\n", batches[3].Text);
    }

}
