using ItTiger.TigerQuery.Output;

namespace ItTiger.TigerQuery.Tests.Output;

/// <summary>
/// Covers resolution against the run's single base directory.
/// </summary>
public sealed class OutputPathResolverTests
{
    private static readonly string BaseDirectory = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "tigerquery-resolver-base"));

    [Fact]
    public void RelativePathsResolveAgainstTheBaseDirectory()
    {
        var resolved = OutputPathResolver.Resolve(BaseDirectory, "report.csv");

        Assert.Equal(Path.Combine(BaseDirectory, "report.csv"), resolved);
        Assert.True(Path.IsPathFullyQualified(resolved));
    }

    [Fact]
    public void NestedAndDottedRelativePathsAreCanonicalized()
    {
        var resolved = OutputPathResolver.Resolve(
            BaseDirectory,
            Path.Combine("exports", "..", "exports", "report.csv"));

        Assert.Equal(Path.Combine(BaseDirectory, "exports", "report.csv"), resolved);
    }

    [Fact]
    public void AbsolutePathsAreUsedUnchanged()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "absolute-report.csv"));

        Assert.Equal(absolute, OutputPathResolver.Resolve(BaseDirectory, absolute));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPathsAreRejected(string path)
    {
        var exception = Assert.Throws<OutputRoutingException>(
            () => OutputPathResolver.Resolve(BaseDirectory, path));

        Assert.Contains("must not be empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMessageCompanionKeepsTheCompleteResultPath()
    {
        var resolved = OutputPathResolver.Resolve(BaseDirectory, "report.csv");

        Assert.Equal(resolved + ".messages.log", OutputPathResolver.GetMessageCompanionPath(resolved));
    }

    [Fact]
    public void PathComparisonFollowsThePlatform()
    {
        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.Equal(expected, OutputPathResolver.PathComparer.Equals("/tmp/Report.csv", "/tmp/report.csv"));
    }
}
