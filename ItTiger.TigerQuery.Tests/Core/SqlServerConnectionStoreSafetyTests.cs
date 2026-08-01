using System.Text.Json;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Core;

/// <summary>
/// Covers store selection and the mutation guarantees the store makes: one selected
/// file with no fallback, coordinated read-modify-write, and crash-safe replacement.
/// </summary>
public sealed class SqlServerConnectionStoreSafetyTests
{
    [Fact]
    public void FilePath_IsTheNormalizedAbsoluteSelectedPath()
    {
        using var temp = new TempDirectory();
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, temp.StorePath);
        var store = NewStore(relative);

        Assert.Equal(Path.GetFullPath(temp.StorePath), store.FilePath, PathComparer);
        Assert.True(Path.IsPathFullyQualified(store.FilePath));
    }

    [Fact]
    public void DefaultOptionsAndAnExplicitPathSelectDistinctStores()
    {
        using var temp = new TempDirectory();
        var shared = SqlServerConnectionStoreOptions.Shared("ItTiger.TigerQuery.Tests");
        var appSpecific = SqlServerConnectionStoreOptions.AppSpecific("ItTiger.TigerQuery.Tests", "SafetyTests");
        var explicitPath = new SqlServerConnectionStoreOptions { FilePath = temp.StorePath };

        Assert.NotEqual(shared.FilePath, appSpecific.FilePath);
        Assert.NotEqual(shared.FilePath, explicitPath.FilePath);
        Assert.NotEqual(appSpecific.FilePath, explicitPath.FilePath);

        // Constructing a store resolves the path and touches nothing on disk.
        Assert.Equal(Path.GetFullPath(shared.FilePath), new SqlServerConnectionStore(shared).FilePath, PathComparer);
        Assert.Equal(
            Path.GetFullPath(appSpecific.FilePath),
            new SqlServerConnectionStore(appSpecific).FilePath,
            PathComparer);
        Assert.Equal(
            Path.GetFullPath(temp.StorePath),
            new SqlServerConnectionStore(explicitPath).FilePath,
            PathComparer);
    }

    [Fact]
    public void APlatformDefaultStoreLocationSupportsTheFullOperationSet()
    {
        // A vendor name unique to this run so the real per-user default-path logic is
        // exercised without touching any shipped application's store.
        var vendor = $"ItTiger.TigerQuery.Tests-{Guid.NewGuid():N}";
        var options = SqlServerConnectionStoreOptions.AppSpecific(vendor, "DefaultStore");
        var vendorDirectory = Path.GetDirectoryName(Path.GetDirectoryName(options.FilePath))!;

        try
        {
            var store = new SqlServerConnectionStore(options, new NoOpConnectionPasswordProtector());
            Assert.Empty(store.Load());

            store.Add(Profile("bootstrap"));
            var copy = store.Copy("bootstrap", new SqlServerConnectionCopyOptions
            {
                TargetName = "temporary",
                InitialCatalogOverride = "ScratchDb"
            });

            Assert.Equal("ScratchDb", copy.Database);
            Assert.NotNull(store.Find("temporary"));
            Assert.True(store.Delete("temporary"));
            Assert.Null(store.Find("temporary"));
            Assert.NotNull(store.Find("bootstrap"));
            Assert.True(File.Exists(options.FilePath));
        }
        finally
        {
            if (Directory.Exists(vendorDirectory))
                Directory.Delete(vendorDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnExplicitStoreNeverFallsBackToAnotherStore()
    {
        using var populated = new TempDirectory();
        using var empty = new TempDirectory();
        var populatedStore = NewStore(populated.StorePath);
        var emptyStore = NewStore(empty.StorePath);
        populatedStore.Add(Profile("bootstrap"));

        // The selected file simply does not exist; nothing probes a default location.
        Assert.False(File.Exists(empty.StorePath));
        Assert.Empty(emptyStore.Load());
        Assert.Null(emptyStore.Find("bootstrap"));
        Assert.False(emptyStore.Exists("bootstrap"));
        Assert.Empty(emptyStore.QueryByMetadata([]));
        Assert.False(emptyStore.Delete("bootstrap"));
        Assert.Throws<InvalidOperationException>(
            () => emptyStore.Copy("bootstrap", new SqlServerConnectionCopyOptions { TargetName = "copy" }));

        Assert.False(File.Exists(empty.StorePath));
        Assert.NotNull(populatedStore.Find("bootstrap"));
        Assert.Null(populatedStore.Find("copy"));
    }

    [Fact]
    public void AMalformedExplicitStoreReportsThatErrorInsteadOfProbingElsewhere()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.StorePath, "{ this is not a connection array");
        var store = NewStore(temp.StorePath);

        Assert.Throws<JsonException>(() => store.Load());
        Assert.Throws<JsonException>(() => store.Find("anything"));
        Assert.Throws<JsonException>(
            () => store.Copy("anything", new SqlServerConnectionCopyOptions { TargetName = "copy" }));
    }

    [Fact]
    public async Task ConcurrentMutationsDoNotLoseUpdates()
    {
        using var temp = new TempDirectory();
        var store = NewStore(temp.StorePath);
        store.Add(Profile("seed"));

        const int Writers = 12;
        using var gate = new Barrier(Writers);
        var tasks = Enumerable.Range(0, Writers).Select(index => Task.Run(() =>
        {
            // A fresh store per worker proves the coordination is keyed by path, not by instance.
            var worker = NewStore(temp.StorePath);
            gate.SignalAndWait();

            switch (index % 3)
            {
                case 0:
                    worker.Add(Profile($"added-{index}"));
                    break;
                case 1:
                    worker.Copy("seed", new SqlServerConnectionCopyOptions { TargetName = $"copied-{index}" });
                    break;
                default:
                    worker.AddOrUpdate(Profile($"upserted-{index}"));
                    break;
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        var names = store.Load().Select(profile => profile.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Writers + 1, names.Count);
        for (var index = 0; index < Writers; index++)
        {
            var expected = (index % 3) switch
            {
                0 => $"added-{index}",
                1 => $"copied-{index}",
                _ => $"upserted-{index}"
            };
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public async Task ConcurrentDeletesAndAddsLeaveAConsistentStore()
    {
        using var temp = new TempDirectory();
        var store = NewStore(temp.StorePath);
        for (var index = 0; index < 10; index++)
            store.Add(Profile($"seed-{index}"));

        using var gate = new Barrier(10);
        var tasks = Enumerable.Range(0, 10).Select(index => Task.Run(() =>
        {
            var worker = NewStore(temp.StorePath);
            gate.SignalAndWait();
            worker.Delete($"seed-{index}");
            worker.Add(Profile($"replacement-{index}"));
        })).ToArray();

        await Task.WhenAll(tasks);

        var names = store.Load().Select(profile => profile.Name).ToList();
        Assert.Equal(10, names.Count);
        Assert.All(names, name => Assert.StartsWith("replacement-", name, StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedWriteLeavesThePreviousStoreIntactAndRemovesItsTemporaryFile()
    {
        using var temp = new TempDirectory();
        var store = NewStore(temp.StorePath);
        store.Add(Profile("bootstrap"));
        store.Add(Profile("second"));
        var before = File.ReadAllText(temp.StorePath);

        string? observedTemporaryFile = null;
        store.WriteFaultHook = temporaryPath =>
        {
            observedTemporaryFile = temporaryPath;
            throw new IOException("Simulated disk failure before the atomic replace.");
        };

        Assert.Throws<IOException>(() => store.Add(Profile("never-persisted")));

        Assert.NotNull(observedTemporaryFile);
        Assert.False(File.Exists(observedTemporaryFile));
        Assert.Empty(Directory.GetFiles(temp.Directory, "*.tmp-*"));
        Assert.Equal(before, File.ReadAllText(temp.StorePath));

        store.WriteFaultHook = null;
        var names = store.Load().Select(profile => profile.Name).ToList();
        Assert.Equal(["bootstrap", "second"], names);
    }

    [Fact]
    public void AFailedWriteIsNeverObservableAsPartialJson()
    {
        using var temp = new TempDirectory();
        var store = NewStore(temp.StorePath);
        store.Add(Profile("bootstrap"));

        store.WriteFaultHook = _ =>
        {
            // The destination still holds the previous complete document while a
            // replacement is in flight.
            var reader = NewStore(temp.StorePath);
            Assert.Collection(reader.Load(), profile => Assert.Equal("bootstrap", profile.Name));
            throw new IOException("Simulated failure.");
        };

        Assert.Throws<IOException>(
            () => store.Copy("bootstrap", new SqlServerConnectionCopyOptions { TargetName = "copy" }));

        store.WriteFaultHook = null;
        Assert.Null(store.Find("copy"));
    }

    [Fact]
    public void AFailedFirstWriteDoesNotLeaveAnEmptyStoreBehind()
    {
        using var temp = new TempDirectory();
        var store = NewStore(temp.StorePath);
        store.WriteFaultHook = _ => throw new IOException("Simulated failure.");

        Assert.Throws<IOException>(() => store.Add(Profile("bootstrap")));

        Assert.False(File.Exists(temp.StorePath));
        Assert.Empty(Directory.GetFiles(temp.Directory, "*.tmp-*"));
    }

    [Fact]
    public void MutationFailsWithATimeoutWhenAnotherHolderKeepsTheStoreLocked()
    {
        using var temp = new TempDirectory();
        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions
            {
                FilePath = temp.StorePath,
                MutationTimeout = TimeSpan.FromMilliseconds(150)
            },
            new NoOpConnectionPasswordProtector());

        Directory.CreateDirectory(temp.Directory);
        using (new FileStream(
            temp.StorePath + ".lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            Assert.Throws<TimeoutException>(() => store.Add(Profile("bootstrap")));
            Assert.False(File.Exists(temp.StorePath));
        }

        // Once the holder releases it, the very same operation succeeds.
        store.Add(Profile("bootstrap"));
        Assert.NotNull(store.Find("bootstrap"));
    }

    [Fact]
    public void ASuccessfulMutationLeavesNoLockOrTemporaryArtifactBehind()
    {
        using var temp = new TempDirectory();
        var store = NewStore(temp.StorePath);

        store.Add(Profile("bootstrap"));
        store.Copy("bootstrap", new SqlServerConnectionCopyOptions { TargetName = "copy" });
        store.Delete("copy");

        var leftovers = Directory.GetFiles(temp.Directory)
            .Where(path => !string.Equals(path, temp.StorePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(leftovers);
    }

    [Fact]
    public void MutationsPreserveJsonShapeMetadataOrderAndCaseSensitiveNames()
    {
        using var temp = new TempDirectory();
        var store = NewStore(temp.StorePath);
        var profile = Profile("bootstrap");
        profile.SetMetadata("z:last", "3");
        profile.SetMetadata("a:first", "1");
        profile.SetMetadata("m:middle", "2");
        store.Add(profile);
        store.Add(Profile("BOOTSTRAP"));

        var json = File.ReadAllText(temp.StorePath);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(
            ["bootstrap", "BOOTSTRAP"],
            document.RootElement.EnumerateArray().Select(element => element.GetProperty("Name").GetString()));
        Assert.Equal(
            ["a:first", "m:middle", "z:last"],
            document.RootElement[0].GetProperty("Metadata").EnumerateObject().Select(item => item.Name));
        Assert.False(document.RootElement[1].TryGetProperty("Metadata", out _));
        Assert.Contains("\n", json, StringComparison.Ordinal);
    }

    // The store normalizes with Path.GetFullPath, which preserves whatever drive-letter
    // casing the working directory happens to carry on Windows.
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static SqlServerConnectionStore NewStore(string path) =>
        new(
            new SqlServerConnectionStoreOptions { FilePath = path },
            new NoOpConnectionPasswordProtector());

    private static SqlServerConnectionProfile Profile(string name) => new()
    {
        Name = name,
        Server = "sql01",
        Database = "AppDb",
        Authentication = AuthenticationType.Integrated,
        Encrypt = EncryptOption.Mandatory
    };

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Directory = Path.Combine(
                Path.GetTempPath(),
                "TigerQueryStoreSafetyTests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            StorePath = Path.Combine(Directory, "connections.json");
        }

        public string Directory { get; }

        public string StorePath { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
