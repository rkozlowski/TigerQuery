using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Core;

/// <summary>
/// Covers store-path selection: explicit over environment over application default, the
/// refusal to fall back when a higher-priority source supplies an unusable value, and the
/// promise that resolving a path touches neither the file system nor a server.
/// </summary>
public sealed class SqlServerConnectionStorePathResolverTests
{
    private const string EnvironmentName = SqlServerConnectionStoreEnvironment.ConnectionStoreFile;

    [Fact]
    public void TheStandardEnvironmentVariableNameIsTheDocumentedOne()
    {
        Assert.Equal("TIGERQUERY_CONNECTION_STORE_FILE", SqlServerConnectionStoreEnvironment.ConnectionStoreFile);
        Assert.Equal(
            SqlServerConnectionStoreEnvironment.ConnectionStoreFile,
            new SqlServerConnectionStorePathOptions { DefaultFilePath = Absolute("default.json") }
                .EnvironmentVariableName);
    }

    [Fact]
    public void AnExplicitPathWinsAndTheEnvironmentIsNotEvenConsulted()
    {
        var reader = new RecordingEnvironment(Absolute("environment.json"));

        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            ExplicitFilePath = Absolute("explicit.json"),
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = reader.Read
        });

        AssertSelected(Absolute("explicit.json"), SqlServerConnectionStorePathSource.Explicit, resolution);
        Assert.Empty(reader.Requested);
    }

    [Fact]
    public void TheEnvironmentVariableWinsOverTheApplicationDefault()
    {
        var reader = new RecordingEnvironment(Absolute("environment.json"));

        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = reader.Read
        });

        AssertSelected(
            Absolute("environment.json"), SqlServerConnectionStorePathSource.EnvironmentVariable, resolution);
        Assert.Equal([EnvironmentName], reader.Requested);
    }

    [Fact]
    public void TheApplicationDefaultIsUsedWhenNoOverrideExists()
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = Unset
        });

        AssertSelected(
            Absolute("default.json"), SqlServerConnectionStorePathSource.ApplicationDefault, resolution);
    }

    [Fact]
    public void AConfiguredEnvironmentVariableNameIsTheOneRead()
    {
        var reader = new RecordingEnvironment(Absolute("custom.json"));

        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            DefaultFilePath = Absolute("default.json"),
            EnvironmentVariableName = "MYAPP_STORE_FILE",
            EnvironmentReader = reader.Read
        });

        AssertSelected(
            Absolute("custom.json"), SqlServerConnectionStorePathSource.EnvironmentVariable, resolution);
        Assert.Equal(["MYAPP_STORE_FILE"], reader.Requested);
    }

    [Fact]
    public void TheProcessEnvironmentIsReadWhenNoReaderIsInjected()
    {
        // A name nothing could have set, so the real lookup runs and finds nothing —
        // exercising the default reader without this test mutating process state.
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            DefaultFilePath = Absolute("default.json"),
            EnvironmentVariableName = $"TIGERQUERY_TEST_UNSET_{Guid.NewGuid():N}"
        });

        AssertSelected(
            Absolute("default.json"), SqlServerConnectionStorePathSource.ApplicationDefault, resolution);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnExplicitPathThatIsPresentButBlankFailsInsteadOfFallingThrough(string supplied)
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            ExplicitFilePath = supplied,
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = _ => Absolute("environment.json")
        });

        AssertFailed(
            SqlServerConnectionStorePathSource.Explicit,
            SqlServerConnectionStorePathError.Blank,
            resolution);
        Assert.Equal(supplied, resolution.AttemptedValue);
    }

    [Fact]
    public void AnInvalidExplicitPathFailsInsteadOfFallingThrough()
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            ExplicitFilePath = Absolute("st\0re.json"),
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = _ => Absolute("environment.json")
        });

        AssertFailed(
            SqlServerConnectionStorePathSource.Explicit,
            SqlServerConnectionStorePathError.Malformed,
            resolution);
    }

    [Fact]
    public void APresentButEmptyEnvironmentVariableIsAnErrorNotAnAbsence()
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = _ => string.Empty
        });

        AssertFailed(
            SqlServerConnectionStorePathSource.EnvironmentVariable,
            SqlServerConnectionStorePathError.Blank,
            resolution);
        Assert.Contains(EnvironmentName, resolution.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnsetEnvironmentVariableIsSkippedRatherThanFailed()
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = Unset
        });

        Assert.True(resolution.IsSuccess);
        Assert.Equal(SqlServerConnectionStorePathSource.ApplicationDefault, resolution.Source);
    }

    [Fact]
    public void AnInvalidEnvironmentVariableFailsInsteadOfFallingThrough()
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = _ => Absolute("st\0re.json")
        });

        AssertFailed(
            SqlServerConnectionStorePathSource.EnvironmentVariable,
            SqlServerConnectionStorePathError.Malformed,
            resolution);
    }

    [Fact]
    public void AnInvalidApplicationDefaultFailsAndNamesTheApplicationDefault()
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            DefaultFilePath = "   ",
            EnvironmentReader = Unset
        });

        AssertFailed(
            SqlServerConnectionStorePathSource.ApplicationDefault,
            SqlServerConnectionStorePathError.Blank,
            resolution);
    }

    [Fact]
    public void APathEndingInADirectorySeparatorIsRejected()
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            ExplicitFilePath = Absolute("store.json") + Path.DirectorySeparatorChar,
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = Unset
        });

        AssertFailed(
            SqlServerConnectionStorePathSource.Explicit,
            SqlServerConnectionStorePathError.MissingFileName,
            resolution);
    }

    [Fact]
    public void ABareRootIsRejected()
    {
        var root = Path.GetPathRoot(Path.GetFullPath("."));
        Assert.False(string.IsNullOrEmpty(root));

        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            ExplicitFilePath = root,
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = Unset
        });

        AssertFailed(
            SqlServerConnectionStorePathSource.Explicit,
            SqlServerConnectionStorePathError.MissingFileName,
            resolution);
    }

    [Theory]
    [InlineData(SqlServerConnectionStorePathSource.Explicit)]
    [InlineData(SqlServerConnectionStorePathSource.EnvironmentVariable)]
    [InlineData(SqlServerConnectionStorePathSource.ApplicationDefault)]
    public void ARelativePathFromAnySourceIsNormalizedToAbsolute(SqlServerConnectionStorePathSource source)
    {
        const string Relative = "relative-store.json";
        var options = new SqlServerConnectionStorePathOptions
        {
            ExplicitFilePath = source == SqlServerConnectionStorePathSource.Explicit ? Relative : null,
            DefaultFilePath = source == SqlServerConnectionStorePathSource.ApplicationDefault
                ? Relative
                : Absolute("default.json"),
            EnvironmentReader = source == SqlServerConnectionStorePathSource.EnvironmentVariable
                ? _ => Relative
                : Unset
        };

        var resolution = SqlServerConnectionStorePathResolver.Resolve(options);

        Assert.True(resolution.IsSuccess);
        Assert.Equal(source, resolution.Source);
        Assert.Equal(Path.GetFullPath(Relative), resolution.FilePath, PathComparer);
        Assert.True(Path.IsPathFullyQualified(resolution.FilePath!));
    }

    [Fact]
    public void ResolutionCreatesNothingOnDisk()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "TigerQueryStorePathTests", Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(root, "nested", "connections.json");
        Assert.False(Directory.Exists(root));

        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            ExplicitFilePath = storePath,
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = Unset
        });

        AssertSelected(storePath, SqlServerConnectionStorePathSource.Explicit, resolution);
        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(resolution.FilePath!));
    }

    [Fact]
    public void AMissingFileIsNotAResolutionConcern()
    {
        // Presence is store-opening policy, not path resolution: resolving a path to a
        // file that does not exist succeeds, and constructing a store over it still
        // creates nothing.
        var root = Path.Combine(
            Path.GetTempPath(), "TigerQueryStorePathTests", Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(root, "connections.json");

        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            ExplicitFilePath = storePath,
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = Unset
        });

        Assert.True(resolution.IsSuccess);

        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = resolution.FilePath! },
            new NoOpConnectionPasswordProtector());

        Assert.Equal(resolution.FilePath, store.FilePath, PathComparer);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void AFailedResolutionCarriesNoPath()
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(new SqlServerConnectionStorePathOptions
        {
            ExplicitFilePath = string.Empty,
            DefaultFilePath = Absolute("default.json"),
            EnvironmentReader = Unset
        });

        Assert.Null(resolution.FilePath);
        Assert.NotNull(resolution.ErrorMessage);
        Assert.NotEqual(SqlServerConnectionStorePathError.None, resolution.Error);
    }

    [Fact]
    public void NullOptionsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => SqlServerConnectionStorePathResolver.Resolve(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankEnvironmentVariableNameIsAHostConfigurationError(string name)
    {
        var options = new SqlServerConnectionStorePathOptions
        {
            DefaultFilePath = Absolute("default.json"),
            EnvironmentVariableName = name,
            EnvironmentReader = Unset
        };

        Assert.Throws<ArgumentException>(() => SqlServerConnectionStorePathResolver.Resolve(options));
    }

    private static void AssertSelected(
        string expected,
        SqlServerConnectionStorePathSource expectedSource,
        SqlServerConnectionStorePathResolution resolution)
    {
        Assert.True(resolution.IsSuccess, resolution.ErrorMessage);
        Assert.Equal(Path.GetFullPath(expected), resolution.FilePath, PathComparer);
        Assert.Equal(expectedSource, resolution.Source);
        Assert.Equal(SqlServerConnectionStorePathError.None, resolution.Error);
        Assert.Null(resolution.ErrorMessage);
        Assert.Null(resolution.AttemptedValue);
    }

    private static void AssertFailed(
        SqlServerConnectionStorePathSource expectedSource,
        SqlServerConnectionStorePathError expectedError,
        SqlServerConnectionStorePathResolution resolution)
    {
        Assert.False(resolution.IsSuccess);
        Assert.Null(resolution.FilePath);
        Assert.Equal(expectedSource, resolution.Source);
        Assert.Equal(expectedError, resolution.Error);
        Assert.False(string.IsNullOrWhiteSpace(resolution.ErrorMessage));
    }

    private static string Absolute(string fileName) =>
        Path.Combine(Path.GetTempPath(), "TigerQueryStorePathTests", fileName);

    private static string? Unset(string name) => null;

    // Path.GetFullPath preserves whatever drive-letter casing the working directory carries.
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class RecordingEnvironment(string? value)
    {
        public List<string> Requested { get; } = [];

        public string? Read(string name)
        {
            Requested.Add(name);
            return value;
        }
    }
}
