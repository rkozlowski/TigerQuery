using System.Text.Json;
using ItTiger.TigerQuery.Core;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Core;

public sealed class SqlServerConnectionProfileMetadataTests
{
    [Fact]
    public void Load_OldJsonWithoutMetadata_DefaultsToEmptyMetadata()
    {
        using var temp = new TempStore();
        File.WriteAllText(
            temp.FilePath,
            """
            [
              {
                "Name": "legacy",
                "Server": "legacy-server",
                "Database": "legacy-database",
                "Authentication": 0,
                "PasswordEncryption": 0,
                "Encrypt": 1
              }
            ]
            """);

        var profile = Assert.Single(temp.Store.Load());

        Assert.Equal("legacy", profile.Name);
        Assert.Equal("legacy-server", profile.Server);
        Assert.Equal("legacy-database", profile.Database);
        Assert.Empty(profile.Metadata);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsMultipleOpaqueMetadataKeysInOrdinalOrder()
    {
        using var temp = new TempStore();
        var profile = ServerProfile();
        profile.SetMetadata("ittiger.zeta.marker", "last");
        profile.SetMetadata("ittiger.tigerwrap.role", "automated-test-host");
        profile.SetMetadata("ittiger.alpha.marker", "first");

        temp.Store.Add(profile);

        var json = File.ReadAllText(temp.FilePath);
        var alphaIndex = json.IndexOf("ittiger.alpha.marker", StringComparison.Ordinal);
        var tigerWrapIndex = json.IndexOf("ittiger.tigerwrap.role", StringComparison.Ordinal);
        var zetaIndex = json.IndexOf("ittiger.zeta.marker", StringComparison.Ordinal);
        Assert.True(alphaIndex < tigerWrapIndex);
        Assert.True(tigerWrapIndex < zetaIndex);

        var loaded = Assert.Single(temp.Store.Load());
        Assert.Equal(3, loaded.Metadata.Count);
        Assert.Equal("automated-test-host", loaded.Metadata["ittiger.tigerwrap.role"]);
        Assert.Equal("first", loaded.Metadata["ittiger.alpha.marker"]);
        Assert.Equal("last", loaded.Metadata["ittiger.zeta.marker"]);
    }

    [Fact]
    public void Save_EmptyMetadataIsOmitted_AndReloadsAsEmpty()
    {
        using var temp = new TempStore();

        temp.Store.Add(ServerProfile());

        Assert.DoesNotContain("\"Metadata\"", File.ReadAllText(temp.FilePath));
        Assert.Empty(Assert.Single(temp.Store.Load()).Metadata);
    }

    [Fact]
    public void MetadataHelpers_ReplaceAndRemoveOneEntry()
    {
        var profile = ServerProfile();
        profile.SetMetadata("ittiger.app.role", "old");
        profile.SetMetadata("ittiger.app.other", "preserved");

        profile.SetMetadata("ittiger.app.role", "new");
        var removed = profile.RemoveMetadata("ittiger.app.role");

        Assert.True(removed);
        Assert.False(profile.RemoveMetadata("ittiger.app.role"));
        Assert.False(profile.Metadata.ContainsKey("ittiger.app.role"));
        Assert.Equal("preserved", profile.Metadata["ittiger.app.other"]);
    }

    [Fact]
    public void MetadataKeys_UseOrdinalComparison_AndPreserveCaseDistinctKeys()
    {
        using var temp = new TempStore();
        var profile = ServerProfile();
        profile.SetMetadata("ittiger.app.role", "lower");
        profile.SetMetadata("ItTiger.App.Role", "mixed");
        profile.SetMetadata("ittiger.app.role", "replacement");

        temp.Store.Add(profile);
        var loaded = Assert.Single(temp.Store.Load());

        Assert.Equal(2, loaded.Metadata.Count);
        Assert.Equal("replacement", loaded.Metadata["ittiger.app.role"]);
        Assert.Equal("mixed", loaded.Metadata["ItTiger.App.Role"]);
    }

    [Fact]
    public void StoreUpdate_PreservesUnknownMetadata_AndSupportsTargetedChanges()
    {
        using var temp = new TempStore();
        var profile = ServerProfile();
        profile.SetMetadata("ittiger.tigerwrap.role", "automated-test-host");
        profile.SetMetadata("another.application.setting", "keep-me");
        temp.Store.Add(profile);

        var update = temp.Store.Find("host")!;
        update.Server = "updated-server";
        update.SetMetadata("ittiger.tigerwrap.role", "replacement");
        temp.Store.AddOrUpdate(update);

        var reloaded = temp.Store.Find("host")!;
        Assert.Equal("updated-server", reloaded.Server);
        Assert.Equal("replacement", reloaded.Metadata["ittiger.tigerwrap.role"]);
        Assert.Equal("keep-me", reloaded.Metadata["another.application.setting"]);

        Assert.True(reloaded.RemoveMetadata("ittiger.tigerwrap.role"));
        temp.Store.AddOrUpdate(reloaded);

        var afterRemoval = temp.Store.Find("host")!;
        Assert.False(afterRemoval.Metadata.ContainsKey("ittiger.tigerwrap.role"));
        Assert.Equal("keep-me", afterRemoval.Metadata["another.application.setting"]);
    }

    [Fact]
    public void Resolve_ServerLevelProfileExcludesDatabaseAndMetadata()
    {
        using var temp = new TempStore();
        var profile = ServerProfile();
        profile.SetMetadata("ittiger.tigerwrap.role", "automated-test-host");
        temp.Store.Add(profile);

        var resolution = SqlServerConnectionResolver.Resolve(temp.Store, "host");

        Assert.True(resolution.IsSuccess, resolution.ErrorMessage);
        var builder = new SqlConnectionStringBuilder(resolution.ConnectionString);
        Assert.Equal("test-server", builder.DataSource);
        Assert.Equal(string.Empty, builder.InitialCatalog);
        Assert.DoesNotContain("ittiger.tigerwrap.role", resolution.ConnectionString);
        Assert.DoesNotContain("automated-test-host", resolution.ConnectionString);
    }

    [Fact]
    public void Load_MalformedMetadataUsesExistingJsonFailurePath()
    {
        using var temp = new TempStore();
        File.WriteAllText(
            temp.FilePath,
            """
            [
              {
                "Name": "host",
                "Server": "test-server",
                "Metadata": {
                  "ittiger.app.invalid": { "nested": true }
                }
              }
            ]
            """);

        Assert.Throws<JsonException>(() => temp.Store.Load());
    }

    [Fact]
    public void Load_NullMetadataValueUsesExistingJsonFailurePath()
    {
        using var temp = new TempStore();
        File.WriteAllText(
            temp.FilePath,
            """
            [
              {
                "Name": "host",
                "Server": "test-server",
                "Metadata": {
                  "ittiger.app.invalid": null
                }
              }
            ]
            """);

        Assert.Throws<JsonException>(() => temp.Store.Load());
    }

    private static SqlServerConnectionProfile ServerProfile() => new()
    {
        Name = "host",
        Server = "test-server",
        Database = null,
        Authentication = AuthenticationType.Integrated,
        Encrypt = EncryptOption.Mandatory
    };

    private sealed class TempStore : IDisposable
    {
        private readonly string directory;

        public TempStore()
        {
            directory = Path.Combine(
                Path.GetTempPath(),
                "TigerQueryMetadataTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, "connections.json");
            Store = new SqlServerConnectionStore(
                new SqlServerConnectionStoreOptions { FilePath = FilePath },
                new NoOpConnectionPasswordProtector());
        }

        public string FilePath { get; }
        public SqlServerConnectionStore Store { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; each test uses a unique directory.
            }
        }
    }
}
