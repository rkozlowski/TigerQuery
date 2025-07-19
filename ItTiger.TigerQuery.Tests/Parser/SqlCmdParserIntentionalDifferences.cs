using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Tests.Helpers;

namespace ItTiger.TigerQuery.Tests.Parser;

public class SqlCmdParserIntentionalDifferences
{
    /// <summary>
    /// This class contains tests that validate intentional differences between TigerQuery and sqlcmd.exe.
    ///
    /// Unlike SqlCmdParserKnownIssues (which tracks undesired divergences),
    /// the behaviors tested here are deliberate design choices made for clarity,
    /// usability, or forward compatibility.
    ///
    /// These tests document and defend TigerQuery’s chosen behavior when it
    /// intentionally deviates from the legacy sqlcmd engine, and help prevent accidental reversion.
    /// </summary>

    [Fact]
    // When go is followed by a variable that has non-numeric value, sqlcmd treats is as 0 (no error, no message).
    // TigerQuery fails on such value
    public async Task FailsWhenGoWithVarWithNonNumericValue()
    {
        var sql = ":SETVAR cnt Invalid\r\nGO\r\nPRINT('Something');\r\nGO $(cnt)\r\nPRINT('End');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        await Assert.ThrowsAsync<TigerQueryException>(() =>TestHelper.ParseBatchesAsync(sql, options));
    }

    [Fact]
    // When go is followed by a variable that has a very long number, sqlcmd goes into the loop.
    // TigerQuery fails on such value (expect 32-bit integer value)
    public async Task FailsWhenGoWithVarWithNonIntValue()
    {
        var sql = ":SETVAR cnt 99999999999999999999999999999999999999999999999999999999999\r\n"
            + "GO\r\nPRINT('Something');\r\nGO $(cnt)\r\nPRINT('End');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        await Assert.ThrowsAsync<TigerQueryException>(() => TestHelper.ParseBatchesAsync(sql, options));
    }

    [Fact]
    // When the count after go is specified as a numeric constant, sql cmd allows only digits, without the sign (+/-).
    // TigerQuery allows the number to be specified with a sign.
    public async Task SucceedsWithGoWithANumberWithPlus()
    {
        var sql = "PRINT('Something');\r\nGO +3\r\nPRINT('End');\r\nGO";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Equal(2, batches.Count);
        Assert.Equal(3, batches[0].ExecCount);
    }

    [Fact]
    // When the count after GO is specified as a numeric constant, sqlcmd allows only unsigned digits (e.g., GO 5).
    // It does not support leading signs like '+' or '-', and will fail or ignore unexpected characters.
    // 
    // TigerQuery intentionally allows the number to be specified with an explicit sign (e.g., GO +3 or GO -5).
    // This applies both to literal constants and to values resolved from variables.
    //
    // Current TigerQuery behavior interprets signed numbers as follows:
    // - A positive value (e.g., +3) is treated as expected: the batch is executed N times.
    // - A value of 0 or less (e.g., GO 0, GO -5, or GO $(x) where x = -1) results in the batch being parsed,
    //   but marked with an ExecCount <= 0. The execution engine is responsible for deciding whether to skip
    //   or raise an error, but by default, such batches are treated as no-ops and are not executed.
    //
    // This behavior is consistent across constant and variable-based counts,
    // and is intended to offer clarity and flexibility while avoiding sqlcmd’s silent behavior
    // (e.g., treating non-numeric variables as zero without warning).
    //
    // The handling of negative values may be revisited in future versions,
    // but is currently accepted as valid input with a defined no-op behavior.
    public async Task SkipsExecutionWhenGoCountIsZeroOrNegative()
    {
        var sql = "PRINT('Something');\r\nGO -5\r\nPRINT('End');\r\nGO";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        var batches = await TestHelper.ParseBatchesAsync(sql, options);

        Assert.Equal(2, batches.Count);
        Assert.Equal(-5, batches[0].ExecCount);
    }

    [Fact]
    // Documentation for sqlcmd specifies two options, exit & ignore for :on error command,
    // but the sqlcmd don't fail if other (invalid) option is specified.
    // TigerQuery fails on such option
    public async Task FailsWhenOnErrorOptionIsUnexpected()
    {
        var sql = ":ON ERROR DoNothing\r\nGO\r\nPRINT('Something');\r\nGO\r\n";
        var options = new TigerQueryEngineOptions
        {
            Mode = SqlCmdMode.SqlCmd
        };
        await Assert.ThrowsAsync<TigerQueryException>(() => TestHelper.ParseBatchesAsync(sql, options));
    }
}
