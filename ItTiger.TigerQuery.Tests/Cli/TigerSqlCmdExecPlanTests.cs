using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Unit tests for the two pieces of <c>exec</c> that decide everything before a process
/// exists: the <c>--</c> split and the handoff plan. Neither touches SQL Server, the
/// connection store, or the operating system, so the substitution and redaction contracts
/// can be locked exactly.
/// </summary>
public sealed class TigerSqlCmdExecPlanTests
{
    private const string Placeholder = TigerSqlCmdExecPlan.ConnectionStringPlaceholder;
    private const string Resolved = "Server=sql01;Database=AppDb;Password=not-a-real-secret";

    // ── The -- split ─────────────────────────────────────────────────

    [Fact]
    public void Split_WithoutExec_LeavesEveryOtherCommandUntouched()
    {
        // A "--" anywhere else keeps whatever meaning TigerCli already gave it.
        var (host, child) = TigerSqlCmdChildCommandLine.Split(
            ["run", "-c", "local", "-q", "select 1", "--", "extra"]);

        Assert.Equal(["run", "-c", "local", "-q", "select 1", "--", "extra"], host);
        Assert.Null(child);
    }

    [Fact]
    public void Split_EmptyArguments_ReturnsNoChildCommandLine()
    {
        var (host, child) = TigerSqlCmdChildCommandLine.Split([]);

        Assert.Empty(host);
        Assert.Null(child);
    }

    [Fact]
    public void Split_ExecWithoutSeparator_ReportsNoChildCommandLine()
    {
        var (host, child) = TigerSqlCmdChildCommandLine.Split(["exec", "-c", "local"]);

        Assert.Equal(["exec", "-c", "local"], host);
        Assert.Null(child);
    }

    [Fact]
    public void Split_ExecWithSeparator_SplitsHostArgumentsFromTheChildCommandLine()
    {
        var (host, child) = TigerSqlCmdChildCommandLine.Split(
            ["exec", "-c", "local", "--", "my-tool", "--flag", "value"]);

        Assert.Equal(["exec", "-c", "local"], host);
        Assert.Equal(["my-tool", "--flag", "value"], child);
    }

    [Fact]
    public void Split_TrailingSeparator_ReportsAnEmptyChildCommandLine()
    {
        // Distinct from "no separator": the caller asked for a child but named none.
        var (host, child) = TigerSqlCmdChildCommandLine.Split(["exec", "-c", "local", "--"]);

        Assert.Equal(["exec", "-c", "local"], host);
        Assert.NotNull(child);
        Assert.Empty(child);
    }

    [Fact]
    public void Split_SecondSeparator_StaysAnOrdinaryChildArgument()
    {
        var (_, child) = TigerSqlCmdChildCommandLine.Split(
            ["exec", "-c", "local", "--", "my-tool", "--", "after"]);

        Assert.Equal(["my-tool", "--", "after"], child);
    }

    [Fact]
    public void Split_ExecIsMatchedTheWayTigerCliMatchesCommandPaths()
    {
        var (host, child) = TigerSqlCmdChildCommandLine.Split(["EXEC", "-c", "local", "--", "my-tool"]);

        Assert.Equal(["EXEC", "-c", "local"], host);
        Assert.Equal(["my-tool"], child);
    }

    // ── Handoff validation ───────────────────────────────────────────

    [Fact]
    public void TryCreate_NoSeparator_ExplainsHowToNameAChild()
    {
        Assert.False(TigerSqlCmdExecPlan.TryCreate(null, null, out var plan, out var error));

        Assert.Null(plan);
        Assert.Contains("'--'", error);
    }

    [Fact]
    public void TryCreate_NoExecutableAfterSeparator_Fails()
    {
        Assert.False(TigerSqlCmdExecPlan.TryCreate([], "DB", out var plan, out var error));

        Assert.Null(plan);
        Assert.Contains("No child executable", error);
    }

    [Fact]
    public void TryCreate_WhitespaceExecutable_Fails()
    {
        Assert.False(TigerSqlCmdExecPlan.TryCreate(["   "], "DB", out var plan, out var error));

        Assert.Null(plan);
        Assert.Contains("must not be empty", error);
    }

    [Fact]
    public void TryCreate_NoPlaceholderAndNoEnvironmentVariable_FailsAsMissingHandoff()
    {
        Assert.False(TigerSqlCmdExecPlan.TryCreate(
            ["my-tool", "--flag"], null, out var plan, out var error));

        Assert.Null(plan);
        Assert.Contains("No connection-string handoff", error);
        Assert.Contains(Placeholder, error);
        Assert.Contains("--connection-string-env", error);
    }

    [Fact]
    public void TryCreate_PlaceholderInTheExecutable_IsRejected()
    {
        Assert.False(TigerSqlCmdExecPlan.TryCreate(
            [Placeholder, "--flag"], null, out var plan, out var error));

        Assert.Null(plan);
        Assert.Contains("not into the child executable", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1DB")]
    [InlineData("DB-CONNECTION")]
    [InlineData("DB CONNECTION")]
    [InlineData("DB=CONNECTION")]
    [InlineData("DB.CONNECTION")]
    [InlineData("PATH;X")]
    public void TryCreate_InvalidEnvironmentVariableName_Fails(string name)
    {
        Assert.False(TigerSqlCmdExecPlan.TryCreate(["my-tool"], name, out var plan, out var error));

        Assert.Null(plan);
        Assert.Contains("not a valid environment-variable name", error);
    }

    [Theory]
    [InlineData("DB")]
    [InlineData("_DB")]
    [InlineData("TIGER_SQL_CONNECTION_STRING")]
    [InlineData("db_connection_1")]
    public void TryCreate_ValidEnvironmentVariableName_Succeeds(string name)
    {
        Assert.True(TigerSqlCmdExecPlan.TryCreate(["my-tool"], name, out var plan, out var error));

        Assert.Null(error);
        Assert.Equal(name, plan!.EnvironmentVariableName);
        Assert.False(plan.SubstitutesArguments);
    }

    [Fact]
    public void TryCreate_PlaceholderAlone_SelectsArgumentSubstitution()
    {
        Assert.True(TigerSqlCmdExecPlan.TryCreate(
            ["my-tool", $"/Target:{Placeholder}"], null, out var plan, out var error));

        Assert.Null(error);
        Assert.True(plan!.SubstitutesArguments);
        Assert.Null(plan.EnvironmentVariableName);
        Assert.Equal("my-tool", plan.Executable);
    }

    [Fact]
    public void TryCreate_BothHandoffs_AreAllowedTogether()
    {
        Assert.True(TigerSqlCmdExecPlan.TryCreate(
            ["my-tool", Placeholder], "DB", out var plan, out _));

        Assert.True(plan!.SubstitutesArguments);
        Assert.Equal("DB", plan.EnvironmentVariableName);
    }

    // ── Substitution ─────────────────────────────────────────────────

    [Fact]
    public void Materialize_ReplacesTheExactPlaceholderAndNothingElse()
    {
        Assert.True(TigerSqlCmdExecPlan.TryCreate(
            [
                "my-tool",
                Placeholder,
                "/Target:{connection-String}",   // wrong case: not the exact token
                "{connection-string }",          // extra space: not the exact token
                "$HOME %PATH% *.sql \"quoted\"", // no shell, environment, or glob expansion
                "--plain"
            ],
            null,
            out var plan,
            out _));

        var invocation = plan!.Materialize(Resolved);

        Assert.Equal(
            [
                Resolved,
                "/Target:{connection-String}",
                "{connection-string }",
                "$HOME %PATH% *.sql \"quoted\"",
                "--plain"
            ],
            invocation.Arguments);
        Assert.Equal("my-tool", invocation.Executable);
    }

    [Fact]
    public void Materialize_SubstitutesInsideALargerArgument()
    {
        Assert.True(TigerSqlCmdExecPlan.TryCreate(
            ["sqlpackage", $"/TargetConnectionString:{Placeholder}"], null, out var plan, out _));

        var invocation = plan!.Materialize(Resolved);

        Assert.Equal([$"/TargetConnectionString:{Resolved}"], invocation.Arguments);
    }

    [Fact]
    public void Materialize_SubstitutesEveryOccurrenceInEveryArgument()
    {
        Assert.True(TigerSqlCmdExecPlan.TryCreate(
            ["my-tool", $"/Source:{Placeholder}", $"a{Placeholder}b{Placeholder}c"],
            null,
            out var plan,
            out _));

        var invocation = plan!.Materialize(Resolved);

        Assert.Equal(
            [$"/Source:{Resolved}", $"a{Resolved}b{Resolved}c"],
            invocation.Arguments);
    }

    [Fact]
    public void Materialize_PreservesArgumentsContainingSpacesExactly()
    {
        Assert.True(TigerSqlCmdExecPlan.TryCreate(
            ["my tool.exe", "/Out:C:\\reports\\my report.sql", Placeholder],
            null,
            out var plan,
            out _));

        var invocation = plan!.Materialize(Resolved);

        Assert.Equal("my tool.exe", invocation.Executable);
        Assert.Equal(["/Out:C:\\reports\\my report.sql", Resolved], invocation.Arguments);
    }

    // ── Redaction ────────────────────────────────────────────────────

    [Fact]
    public void DescribeRedacted_ShowsThePlaceholderAndQuotesSpacedTokens()
    {
        Assert.True(TigerSqlCmdExecPlan.TryCreate(
            ["C:\\Program Files\\tool\\my-tool.exe", $"/Target:{Placeholder}", "a b"],
            "DB",
            out var plan,
            out _));

        // Materializing first proves the description is not built from a cached result.
        _ = plan!.Materialize(Resolved);
        var description = plan.DescribeRedacted();

        Assert.Equal(
            $"\"C:\\Program Files\\tool\\my-tool.exe\" /Target:{Placeholder} \"a b\"",
            description);
        Assert.DoesNotContain(Resolved, description, StringComparison.Ordinal);
    }

    // ── Ctrl+C policy ────────────────────────────────────────────────

    [Fact]
    public void ChildProcessCancellationScope_SuppressesTheFirstCtrlCAndNotTheSecond()
    {
        using var scope = new ChildProcessCancellationScope();

        // The first press keeps tiger-sqlcmd alive to report the child's exit code; the
        // second reaches the default handler so a child that ignores Ctrl+C cannot hang the
        // caller.
        Assert.True(scope.ShouldSuppress());
        Assert.False(scope.ShouldSuppress());
        Assert.False(scope.ShouldSuppress());
    }
}
