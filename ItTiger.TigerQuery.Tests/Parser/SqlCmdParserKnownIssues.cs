using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Events;
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
    // TigerQuery retains an undefined variable expression, as sqlcmd does without its fail-on-error option,
    // but it does not emit sqlcmd's "scripting variable not defined" diagnostic.
    // Adding that diagnostic requires a warning path from variable expansion to the parser/engine consumer.
    public async Task LeavesUndefinedVariableUnsubstitutedWithoutWarning()
    {
        var sql = "SELECT 'Start$(UnsetVar)End' AS [Test];\r\nGO\r\nPRINT('Something');\r\nGO";
        var messages = new List<SqlCmdMessage>();
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmdEx,
            OnMessage = (message, _) => messages.Add(message)
        };

        var (batches, _) = await TestHelper.ParseBatchesCtxAsync(sql, options);

        Assert.Equal(2, batches.Count);
        Assert.Contains("$(UnsetVar)", batches[0].Text); // No substitution
        Assert.Equal("PRINT('Something');\r\n", batches[1].Text);
        Assert.Empty(messages);
    }

    [Fact]
    // :ON ERROR IGNORE updates execution policy, but unresolved-variable expansion emits no diagnostic.
    // The expression therefore remains literal and parsing continues. Defining warning severity and its
    // relationship to execution policy requires coordinated parser/engine behavior.
    public async Task ContinuesWhenUnsetVarUsedWithOnErrorIgnore()
    {
        var sql = ":ON ERROR IGNORE\r\nGO\r\nSELECT 'Start$(UnsetVar)End' [Test];\r\nGO\r\nPRINT('Something');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var (batches, context) = await TestHelper.ParseBatchesCtxAsync(sql, options);

        Assert.Equal(2, batches.Count);
        Assert.Contains("Start$(UnsetVar)End", batches[0].Text);
        Assert.True(context.ContinueOnError);
    }

    [Fact]
    // :ON ERROR EXIT updates execution policy, but unresolved-variable expansion emits no diagnostic for
    // that policy to act on. The expression remains literal and parsing continues. Whether expansion
    // diagnostics should stop parsing or execution must be designed with the engine warning/error model.
    public async Task ContinuesWhenUnsetVarUsedWithOnErrorExit()
    {
        var sql = ":ON ERROR EXIT\r\nGO\r\nSELECT 'Start$(UnsetVar)End' [Test];\r\nGO\r\nPRINT('Something');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var (batches, context) = await TestHelper.ParseBatchesCtxAsync(sql, options);

        Assert.Equal(2, batches.Count);
        Assert.Contains("Start$(UnsetVar)End", batches[0].Text);
        Assert.False(context.ContinueOnError);
    }

}
