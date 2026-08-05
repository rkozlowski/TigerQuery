using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.Tests.Cli;
using ItTiger.TigerSqlCmd;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Live;

/// <summary>
/// Runs the real <c>tiger-sqlcmd</c> executable against the host's configured E2E SQL
/// Server through a private connection store, so process exit codes and console text are
/// observed exactly as a script or agent would observe them.
/// </summary>
/// <remarks>
/// The private store holds only external references; the server, login, and password
/// reach the child through its environment and are never written to the store file. The
/// bootstrap is named <see cref="TigerSqlCmdApp.DefaultE2eBootstrapConnectionName"/> and
/// carries the same authorization metadata the shipped bootstrap command writes, so the
/// <c>e2e</c> commands resolve it exactly as they do in production.
/// </remarks>
internal sealed class LiveTigerSqlCmdHost : IDisposable
{
    private const string ServerVariable = "TQ_LIVE_HOST_SERVER";
    private const string UsernameVariable = "TQ_LIVE_HOST_USERNAME";
    private const string PasswordVariable = "TQ_LIVE_HOST_PASSWORD";

    /// <summary>The exact bootstrap name the shipped E2E commands expect.</summary>
    public const string BootstrapName = TigerSqlCmdApp.DefaultE2eBootstrapConnectionName;

    private readonly string directory;
    private readonly Dictionary<string, string> externalValues;

    private LiveTigerSqlCmdHost(
        string directory,
        SqlServerConnectionStore store,
        SqlConnectionStringBuilder bootstrapBuilder,
        Dictionary<string, string> externalValues)
    {
        this.directory = directory;
        this.externalValues = externalValues;
        Store = store;
        BootstrapBuilder = bootstrapBuilder;
    }

    /// <summary>Gets the private store the child process is pointed at.</summary>
    public SqlServerConnectionStore Store { get; }

    /// <summary>Gets the host's effective bootstrap connection settings.</summary>
    public SqlConnectionStringBuilder BootstrapBuilder { get; }

    /// <summary>Gets the private store file path passed to the child.</summary>
    public string StorePath => Store.FilePath;

    /// <summary>Resolves the host configuration and provisions the private store.</summary>
    /// <remarks>
    /// Skips through <see cref="SqlServerTestEnvironment"/> when the machine has no
    /// authorized E2E bootstrap; it never probes for a SQL Server.
    /// </remarks>
    public static LiveTigerSqlCmdHost Create()
    {
        var configuration = SqlServerTestEnvironment.RequireConfiguration(
            requireDatabaseCreation: true);
        var builder = configuration.Profile.BuildConnectionStringBuilder();

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(LiveTigerSqlCmdHost),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ServerVariable] = builder.DataSource
        };
        if (!builder.IntegratedSecurity)
        {
            values[UsernameVariable] = builder.UserID;
            values[PasswordVariable] = builder.Password;
        }

        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions
            {
                FilePath = Path.Combine(directory, "connections.json")
            },
            new NoOpConnectionPasswordProtector());

        var bootstrap = new SqlServerConnectionProfile
        {
            Name = BootstrapName,
            ServerValue = EnvironmentReference(ServerVariable),
            Database = "master",
            Authentication = builder.IntegratedSecurity
                ? AuthenticationType.Integrated
                : AuthenticationType.SqlPassword,
            Encrypt = SqlClientTestConversions.ToTigerQueryEncryptOption(builder.Encrypt),
            TrustServerCertificate = builder.TrustServerCertificate,
            ConnectTimeout = builder.ConnectTimeout
        };
        if (!builder.IntegratedSecurity)
        {
            bootstrap.UsernameValue = EnvironmentReference(UsernameVariable);
            bootstrap.PasswordValue = EnvironmentReference(PasswordVariable);
        }

        SqlServerE2eMetadata.AuthorizeNewBootstrapProfile(bootstrap, allowDatabaseCreation: true);
        store.Add(bootstrap);

        return new LiveTigerSqlCmdHost(directory, store, builder, values);
    }

    /// <summary>Builds an effective connection string for a database on the same server.</summary>
    public string ConnectionStringFor(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(BootstrapBuilder.ConnectionString)
        {
            InitialCatalog = databaseName
        };
        return builder.ConnectionString;
    }

    /// <summary>Runs the CLI with the private store selected explicitly.</summary>
    public Task<TigerSqlCmdProcessResult> RunAsync(params string[] arguments)
    {
        var environment = externalValues.ToDictionary(
            item => item.Key,
            item => (string?)item.Value,
            StringComparer.Ordinal);

        // The explicit option outranks the variable, but clearing it as well keeps a
        // developer machine's own selection out of the child entirely.
        environment[SqlServerConnectionStoreEnvironment.ConnectionStoreFile] = null;

        return TigerSqlCmdProcessRunner.RunAsync(
            environment,
            directory,
            [.. arguments, "--non-interactive", "--no-color",
                "--tq-connection-store-file", StorePath]);
    }

    /// <summary>Reads the exact matching database names on the server.</summary>
    public static async Task<IReadOnlyList<string>> DatabaseNamesAsync(
        string masterConnectionString,
        string exactName)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [name] FROM sys.databases WHERE [name] = @name;";
        command.Parameters.AddWithValue("@name", exactName);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            names.Add(reader.GetString(0));

        return names;
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
            // A leftover temp store is harmless; it holds no secret.
        }
    }

    private static SqlServerConnectionValue EnvironmentReference(string name) =>
        SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.EnvironmentVariable,
            Name = name
        });
}
