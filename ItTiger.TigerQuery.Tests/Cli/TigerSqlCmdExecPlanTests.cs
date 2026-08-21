using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Unit tests for the handoff plan, the piece of <c>exec</c> that decides everything
/// before a process exists. TigerCli owns the <c>--</c> split and binds the tail, so these
/// tests start from an already-bound child command line and touch neither SQL Server, the
/// connection store, nor the operating system; the substitution and redaction contracts
/// can therefore be locked exactly.
/// </summary>
public sealed class TigerSqlCmdExecPlanTests
{
    private const string Placeholder = TigerSqlCmdExecPlan.ConnectionStringPlaceholder;
    private const string Resolved = "Server=sql01;Database=AppDb;Password=not-a-real-secret";

    // ── Handoff validation ───────────────────────────────────────────

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
