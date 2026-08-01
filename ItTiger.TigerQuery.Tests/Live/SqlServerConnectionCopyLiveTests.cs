using ItTiger.TigerQuery.Core;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Proves that a copied managed connection is a usable connection, not merely a
/// well-formed JSON entry: the copy is resolved by name from the same store and opened
/// against the real server.
/// </summary>
public sealed class SqlServerConnectionCopyLiveTests
{
    private const string SqlUserVariable = "TIGERQUERY_TEST_SQL_USER";
    private const string SqlPasswordVariable = "TIGERQUERY_TEST_SQL_PASSWORD";

    [Fact]
    public async Task AnIntegratedAuthenticationCopyResolvesAndOpensAgainstTheOverriddenDatabase()
    {
        var builder = new SqlConnectionStringBuilder(SqlServerTestEnvironment.RequireConnectionString());
        Assert.SkipUnless(builder.IntegratedSecurity, "The detected instance does not use integrated authentication.");

        using var temp = new TempStore();
        var source = ProfileFrom("bootstrap", builder);
        source.Database = "master";
        source.SetMetadata("app:Role", "Bootstrap");
        temp.Store.Add(source);

        var copy = temp.Store.Copy("bootstrap", new SqlServerConnectionCopyOptions
        {
            TargetName = "temporary",
            InitialCatalogOverride = "tempdb",
            MetadataToSet = new Dictionary<string, string>(StringComparer.Ordinal) { ["app:Role"] = "TestDatabase" }
        });

        Assert.Equal("tempdb", copy.Database);
        Assert.Equal("TestDatabase", copy.Metadata["app:Role"]);

        Assert.Equal("tempdb", await OpenAndReadDatabaseNameAsync(temp.Store, "temporary"));
        Assert.Equal("master", await OpenAndReadDatabaseNameAsync(temp.Store, "bootstrap"));

        Assert.True(temp.Store.Delete("temporary"));
        Assert.Null(temp.Store.Find("temporary"));
        Assert.Equal("master", await OpenAndReadDatabaseNameAsync(temp.Store, "bootstrap"));
    }

    [Fact]
    public async Task ASqlAuthenticationCopyKeepsTheProtectedPasswordUsable()
    {
        var user = Environment.GetEnvironmentVariable(SqlUserVariable);
        var password = Environment.GetEnvironmentVariable(SqlPasswordVariable);
        Assert.SkipWhen(
            string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password),
            $"Set {SqlUserVariable} and {SqlPasswordVariable} to run the SQL-authentication copy test.");

        var builder = new SqlConnectionStringBuilder(SqlServerTestEnvironment.RequireConnectionString());

        // The platform-default protector is used deliberately: on Windows the copy has to
        // carry a real DPAPI blob through the store and still open afterwards.
        using var temp = new TempStore(ConnectionPasswordProtector.CreateDefault());
        var source = ProfileFrom("bootstrap", builder);
        source.Database = "master";
        source.Authentication = AuthenticationType.SqlPassword;
        source.Username = user;
        source.PlainPassword = password;
        temp.Store.Add(source);

        var copy = temp.Store.Copy("bootstrap", new SqlServerConnectionCopyOptions
        {
            TargetName = "temporary",
            InitialCatalogOverride = "tempdb"
        });

        // The caller never sees plaintext, and the stored blob is duplicated as-is.
        Assert.Null(copy.PlainPassword);
        Assert.Equal(temp.Store.Find("bootstrap")!.EncryptedPassword, copy.EncryptedPassword);

        Assert.Equal("tempdb", await OpenAndReadDatabaseNameAsync(temp.Store, "temporary"));
        Assert.Equal("master", await OpenAndReadDatabaseNameAsync(temp.Store, "bootstrap"));
    }

    private static async Task<string> OpenAndReadDatabaseNameAsync(SqlServerConnectionStore store, string name)
    {
        var resolution = SqlServerConnectionResolver.Resolve(store, name);
        Assert.True(resolution.IsSuccess, resolution.ErrorMessage);

        await using var connection = new SqlConnection(resolution.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME();";
        return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static SqlServerConnectionProfile ProfileFrom(string name, SqlConnectionStringBuilder builder) => new()
    {
        Name = name,
        Server = builder.DataSource,
        Authentication = builder.IntegratedSecurity ? AuthenticationType.Integrated : AuthenticationType.SqlPassword,
        Encrypt = EncryptOption.Optional,
        TrustServerCertificate = true,
        ConnectTimeout = 5
    };

    private sealed class TempStore : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "TigerQueryConnectionCopyLiveTests",
            Guid.NewGuid().ToString("N"));

        public TempStore(IConnectionPasswordProtector? protector = null)
        {
            Directory.CreateDirectory(directory);
            Store = new SqlServerConnectionStore(
                new SqlServerConnectionStoreOptions { FilePath = Path.Combine(directory, "connections.json") },
                protector ?? new NoOpConnectionPasswordProtector());
        }

        public SqlServerConnectionStore Store { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
