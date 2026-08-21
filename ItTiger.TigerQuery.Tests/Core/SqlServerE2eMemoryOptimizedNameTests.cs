using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.E2e;

namespace ItTiger.TigerQuery.Tests.Core;

/// <summary>
/// Pins the generated names memory-optimized provisioning puts into SQL Server: they are
/// derived from the exact owned database, and they are derivable only for a database that
/// already passes the protected E2E guards.
/// </summary>
public sealed class SqlServerE2eMemoryOptimizedNameTests
{
    private const string Owned = "_TQ_E2E_session_0123456789abcdef0123456789abcdef";

    [Fact]
    public void EveryNameIsDerivedFromTheExactOwnedDatabase()
    {
        Assert.Equal(Owned + "_MOD_FG", SqlServerE2eNames.MemoryOptimizedFilegroup(Owned));
        Assert.Equal(Owned + "_MOD_FILE", SqlServerE2eNames.MemoryOptimizedContainer(Owned));
        Assert.Equal(Owned + "_MOD_DIR", SqlServerE2eNames.MemoryOptimizedContainerDirectory(Owned));
    }

    /// <summary>
    /// Two databases never collide, so a second provisioned database on the same server
    /// cannot be handed a container directory the first one already owns.
    /// </summary>
    [Fact]
    public void DifferentDatabasesProduceDifferentContainerDirectories()
    {
        var first = SqlServerE2eNames.Database("probe", Guid.NewGuid().ToString("N"));
        var second = SqlServerE2eNames.Database("probe", Guid.NewGuid().ToString("N"));

        Assert.NotEqual(
            SqlServerE2eNames.MemoryOptimizedContainerDirectory(first),
            SqlServerE2eNames.MemoryOptimizedContainerDirectory(second));
    }

    /// <summary>
    /// The longest name part the generator accepts still leaves every derived name inside
    /// <c>sysname</c>, so the safe path is also the reachable one.
    /// </summary>
    [Fact]
    public void TheLongestGeneratedDatabaseStillYieldsUsableNames()
    {
        var longest = SqlServerE2eNames.Database(new string('a', 64), Guid.NewGuid().ToString("N"));

        Assert.True(SqlServerE2eNames.MemoryOptimizedFilegroup(longest).Length <= 128);
        Assert.True(SqlServerE2eNames.MemoryOptimizedContainer(longest).Length <= 128);
        Assert.True(SqlServerE2eNames.MemoryOptimizedContainerDirectory(longest).Length <= 128);
    }

    /// <summary>
    /// A name that is not an owned E2E database cannot produce one of these, which is what
    /// keeps a path separator, a quote, or a traversal segment out of the container path.
    /// </summary>
    [Theory]
    [InlineData("master")]
    [InlineData("_TQ_E2E_")]
    [InlineData("_TQ_E2E_a\\..\\..\\evil")]
    [InlineData("_TQ_E2E_a'; DROP DATABASE x --")]
    [InlineData("_TQ_E2E_a/b")]
    [InlineData("_TQ_E2E_a b")]
    public void ANameThatIsNotAnOwnedE2eDatabaseIsRefused(string databaseName)
    {
        Assert.Throws<InvalidOperationException>(
            () => SqlServerE2eNames.MemoryOptimizedContainerDirectory(databaseName));
        Assert.Throws<InvalidOperationException>(
            () => SqlServerE2eNames.MemoryOptimizedFilegroup(databaseName));
        Assert.Throws<InvalidOperationException>(
            () => SqlServerE2eNames.MemoryOptimizedContainer(databaseName));
    }

    /// <summary>
    /// The provisioning batch binds every generated name rather than pasting a caller value
    /// into its text, and it names the exact database it was asked about.
    /// </summary>
    [Fact]
    public void ProvisioningParametersCarryTheGeneratedNamesForTheExactDatabase()
    {
        var parameters = SqlServerE2eMemoryOptimizedProvisioning.BuildParameters(Owned);

        Assert.Equal(Owned, parameters["@databaseName"]);
        Assert.Equal(SqlServerE2eNames.MemoryOptimizedFilegroup(Owned), parameters["@filegroupName"]);
        Assert.Equal(SqlServerE2eNames.MemoryOptimizedContainer(Owned), parameters["@containerName"]);
        Assert.Equal(
            SqlServerE2eNames.MemoryOptimizedContainerDirectory(Owned),
            parameters["@containerDirectory"]);

        // The database name reaches the script only through the bound parameter: no literal
        // occurrence of it exists in the script text.
        Assert.DoesNotContain(Owned, SqlServerE2eMemoryOptimizedProvisioning.Script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The script asks SQL Server where its data files live instead of assuming a path, and
    /// it refuses to report success on a database it did not actually provision.
    /// </summary>
    [Fact]
    public void TheProvisioningScriptResolvesItsPathFromServerMetadataAndVerifiesTheResult()
    {
        var script = SqlServerE2eMemoryOptimizedProvisioning.Script;

        Assert.Contains("SERVERPROPERTY('InstanceDefaultDataPath')", script, StringComparison.Ordinal);
        Assert.Contains("sys.master_files", script, StringComparison.Ordinal);
        Assert.Contains("CONTAINS MEMORY_OPTIMIZED_DATA", script, StringComparison.Ordinal);
        Assert.Contains("sys.filegroups", script, StringComparison.Ordinal);
        // Generic SQL Server support only: no application schema is created.
        Assert.DoesNotContain("CREATE TABLE", script, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Provisioning is opt-in; the default create options change nothing.</summary>
    [Fact]
    public void TheCreateOptionsDefaultToTheOrdinaryDisposableDatabase()
    {
        var options = new SqlServerE2eCreateOptions();

        Assert.False(options.MemoryOptimized);
        Assert.Null(options.DatabaseNamePart);
        Assert.Null(options.ConnectionNamePart);
    }
}
