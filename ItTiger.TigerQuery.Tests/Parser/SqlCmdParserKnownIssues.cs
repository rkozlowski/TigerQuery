using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Tests.Parser;

public class SqlCmdParserKnownIssues
{
    /// <summary>
    /// This class captures edge cases where TigerQuery behaves differently than sqlcmd.exe,
    /// especially where TigerQuery is too permissive or fails to enforce expected constraints.
    /// 
    /// These tests serve three purposes:
    /// 1. To document known divergences from official sqlcmd behavior.
    /// 2. To guard against silent regressions or accidental reinforcement of incorrect behavior.
    /// 3. To prepare for future tightening of the parser or engine logic.
    ///
    /// Tests in this class are expected to be temporary.
    /// Once the behavior is corrected (e.g. an invalid construct is rejected), the test should be:
    /// - Moved to the appropriate positive/negative test class (e.g., SqlCmdParserTests), and
    /// - Modified to assert the correct failure or corrected behavior.
    /// 
    /// Tests in this class must not be skipped. They must fail visibly if behavior changes,
    /// acting as an explicit reminder to revisit the parser's handling of that construct.
    /// </summary>


    [Fact]
    // sqlcmd expect :setvar to be ended by end of line, end of stream or single line comment
    // TigerQuery parser allows for much more when the value is in double quotes, which is dangerous
    public async Task SucceedsWithSetvarEndedByMultiLineComment()
    {
        var sql = ":SETVAR x \"Some value\" /* xxxx */\nGO    --xxxx\r\n PRINT   \t ( '$(x)' )  ;  \r\n\r\nGO ";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);
        
        Assert.Equal(2, batches.Count);
    }

    
    [Fact]
    // sqlcmd allows the count after go to be either a positive integer or a single variable.
    // TigerQuery parser allows for more that a single variable being specified, concantenating their value
    public async Task SucceedsWithGoWithMultipleVars()
    {
        var sql = ":SETVAR one \"1\" -- 1\n    \t\t:SetVar\ttwo\t\t2\t-- 2\rgO    \r\n\tPRINT\t('!!!');\r\rGO $(one)$(two)\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Equal(2, batches.Count);
        Assert.Equal(12, batches[1].ExecCount);
    }

    [Fact]
    // TigerQuery does not substitute undefined variables, matching sqlcmd.exe behavior.
    // SSMS in SqlCmd mode throws a fatal error, but TigerQuery and sqlcmd.exe continue.
    public async Task LeavesUndefinedVariableUnsubstituted()
    {
        var sql = "SELECT 'Start$(UnsetVar)End' AS [Test];\r\nGO\r\nPRINT('Something');\r\nGO";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmdEx
        };

        var (batches, context) = await TestHelper.ParseBatchesCtxAsync(sql, options);

        Assert.Equal(2, batches.Count);
        Assert.Contains("$(UnsetVar)", batches[0].Text); // No substitution
        Assert.Equal("PRINT('Something');\r\n", batches[1].Text);
    }

    [Fact]
    // Validates TigerQuery behavior when an unset scripting variable is used.
    // 
    // In this case, '$(UnsetVar)' is not defined. 
    // TigerQuery emits no warning, and the string is not substituted — it is left as-is.
    //
    // This diverges from both:
    // - sqlcmd.exe, which emits a warning but continues execution (if :ON ERROR IGNORE is in effect)
    // - SSMS in SqlCmd mode, which *fails immediately* with a fatal scripting error
    //
    // TigerQuery’s behavior is currently more permissive than both, and does not honor :ON ERROR at all.
    // Once this is corrected (i.e., emitting a warning and respecting the error policy), 
    // this test should be updated and moved out of SqlCmdParserKnownIssues.
    //
    // NOTE: This test is linked with `FailsWhenUnsetVarUsedWithOnErrorExit`. Once one is fixed, both must be updated and moved (likely to SqlCmdParserIntentionalDifferences).
    public async Task SucceedsWhenUnsetVarUsedWithOnErrorIgnore()
    {
        var sql = ":ON ERROR IGNORE\r\nGO\r\nSELECT 'Start$(UnsetVar)End' [Test];\r\nGO\r\nPRINT('Something');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Equal(2, batches.Count);
        Assert.Contains("Start$(UnsetVar)End", batches[0].Text);
    }

    [Fact]
    // Validates TigerQuery behavior when :ON ERROR EXIT is used and an unset variable is encountered.
    //
    // Expected (based on sqlcmd.exe):
    // - Emit warning: `'UnsetVar' scripting variable not defined.`
    // - Respect :ON ERROR EXIT and terminate execution after the unresolved variable
    //
    // Actual TigerQuery behavior:
    // - Emits no warning
    // - Continues execution (second batch is executed)
    // - Ignores the :ON ERROR directive entirely
    //
    // SSMS in SqlCmd mode behaves *differently again*: it treats unresolved variables as fatal errors.
    //
    // This test documents current behavior, which is incorrect and must be addressed in the future.
    //
    // NOTE: This test is linked with `SucceedsWhenUnsetVarUsedWithOnErrorIgnore`. Once one is fixed, both must be updated and moved (likely to SqlCmdParserIntentionalDifferences).
    public async Task FailsWhenUnsetVarUsedWithOnErrorExit()
    {
        var sql = ":ON ERROR EXIT\r\nGO\r\nSELECT 'Start$(UnsetVar)End' [Test];\r\nGO\r\nPRINT('Something');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        // NOTE: TigerQuery should stop at the first batch, but currently continues — so test passes with 2 batches.
        Assert.Equal(2, batches.Count);
        Assert.Contains("Start$(UnsetVar)End", batches[0].Text);
    }

}
