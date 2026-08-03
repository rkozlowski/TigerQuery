using ItTiger.TigerQuery.Core;
using ItTiger.TigerSqlCmd;

namespace ItTiger.TigerQuery.Tests.Cli;

public sealed class TigerSqlCmdExternalProcessTests : IDisposable
{
    private const string ExternalConnectionString = "TQ_PHASE8_BAD_CONNECTION";
    private const string Secret = "phase8-process-secret-must-not-appear";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "TigerSqlCmdExternalProcessTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExternalProcessResolvesReferenceAndRedactsMalformedSecretValue()
    {
        Directory.CreateDirectory(directory);
        var storePath = Path.Combine(directory, "connections.json");
        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = storePath },
            new NoOpConnectionPasswordProtector());
        store.Add(new SqlServerConnectionProfile
        {
            Name = "bad-external",
            ConnectionStringValue = SqlServerConnectionValue.External(
                new SqlServerExternalValueReference
                {
                    Source = SqlServerExternalValueSource.EnvironmentVariable,
                    Name = ExternalConnectionString
                })
        });

        var result = await TigerSqlCmdProcessRunner.RunAsync(
            new Dictionary<string, string?>
            {
                [SqlServerConnectionStoreEnvironment.ConnectionStoreFile] = storePath,
                [ExternalConnectionString] =
                    $"Server=private;Password={Secret};Definitely Not A Keyword=value"
            },
            directory,
            "run", "--non-interactive",
            "--connection", "bad-external",
            "--query", "SELECT 1;",
            "--verbosity", "Silent");

        var output = result.StdOut + result.StdErr;
        Assert.Equal((int)TigerSqlCmdExitCode.ConnectionFailed, result.ExitCode);
        Assert.Contains("external values", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, output, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ExternalConnectionString, File.ReadAllText(storePath));
        Assert.DoesNotContain(Secret, File.ReadAllText(storePath), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
