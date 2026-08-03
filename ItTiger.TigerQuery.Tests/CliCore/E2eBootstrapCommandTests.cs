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
        Assert.Equal(
            SqlServerE2eMetadata.True,
            app.Options.Store.Find("host-bootstrap")!.Metadata[SqlServerE2eMetadata.Enabled]);
    }
}
