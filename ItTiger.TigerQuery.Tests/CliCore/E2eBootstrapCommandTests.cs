using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.CliCore;

[Collection(Cli.TigerCliAppCollection.Name)]
public sealed class E2eBootstrapCommandTests
{
    [Fact]
    public async Task NoExplicitOrHostDefaultNameReturnsValidationAndCreatesNothing()
    {
        using var storePath = new TempStorePath();
        var app = ContributionTestApp.Create(storePath.FilePath);

        var result = await app.RunAsync(
            "connections", "add-e2e-bootstrap",
            "--non-interactive", "--server", "srv");

        Assert.Equal((int)ContributionExitCode.Validation, result.ExitCode);
        Assert.Contains("name", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.False(storePath.Exists);
        Assert.False(Directory.Exists(storePath.DirectoryPath));
    }

    [Fact]
    public async Task HostDefaultNameIsUsedWhenNoOptionIsSupplied()
    {
        using var storePath = new TempStorePath();
        var app = ContributionTestApp.Create(
            storePath.FilePath,
            defaultE2eBootstrapConnectionName: "host-bootstrap");

        var result = await app.RunAsync(
            "connections", "add-e2e-bootstrap",
            "--non-interactive", "--server", "srv");

        Assert.Equal((int)ContributionExitCode.Ok, result.ExitCode);
        var metadata = app.Options.Store.Find("host-bootstrap")!.Metadata;
        Assert.Equal(SqlServerE2eMetadata.True, metadata[SqlServerE2eMetadata.Enabled]);
        Assert.Equal(SqlServerE2eMetadata.True, metadata[SqlServerE2eMetadata.Bootstrap]);
    }
}
