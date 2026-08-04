using System.Text.Json;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Core;

/// <summary>
/// Covers <see cref="SqlServerConnectionStore.Copy"/>: what it preserves, what it is
/// allowed to override, and what it refuses.
/// </summary>
public sealed class SqlServerConnectionCopyTests
{
    [Theory]
    [InlineData(SqlServerE2eMetadata.Enabled)]
    [InlineData(SqlServerE2eMetadata.SessionId)]
    [InlineData(SqlServerE2eMetadata.DatabaseName)]
    [InlineData(SqlServerE2eMetadata.AllowDatabaseDrop)]
    [InlineData("ittiger.e2e.future.setting")]
    public void Copy_RejectsReservedMetadataAssignmentsAndRemovals(string key)
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));

        var setError = Assert.Throws<ArgumentException>(() => temp.Store.Copy(
            "source",
            new SqlServerConnectionCopyOptions
            {
                TargetName = "set-target",
                MetadataToSet = new Dictionary<string, string> { [key] = "value" }
            }));
        var removeError = Assert.Throws<ArgumentException>(() => temp.Store.Copy(
            "source",
            new SqlServerConnectionCopyOptions
            {
                TargetName = "remove-target",
                MetadataToRemove = [key]
            }));

        Assert.Contains("reserved", setError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reserved", removeError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(temp.Store.Find("set-target"));
        Assert.Null(temp.Store.Find("remove-target"));
    }
    [Fact]
    public void Copy_IntegratedAuthenticationPreservesEveryPersistedField()
    {
        using var temp = new TempStore();
        var source = FullyPopulatedProfile("source");
        temp.Store.Add(source);

        var copy = temp.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = "copy" });

        Assert.Equal("copy", copy.Name);
        AssertEquivalentApartFrom(source, copy, nameof(SqlServerConnectionProfile.Name));

        var reloaded = temp.Store.Find("copy");
        Assert.NotNull(reloaded);
        AssertEquivalentApartFrom(source, reloaded, nameof(SqlServerConnectionProfile.Name));
    }

    [Fact]
    public void Copy_SqlAuthenticationPreservesUsernameAndProtectedPasswordExactly()
    {
        using var temp = new TempStore();
        var source = new SqlServerConnectionProfile
        {
            Name = "source",
            Server = "sql01",
            Database = "AppDb",
            Authentication = AuthenticationType.SqlPassword,
            Username = "app_user",
            EncryptedPassword = "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA-opaque-blob",
            PasswordEncryption = PasswordEncryptionType.DPAPI
        };
        temp.Store.Add(source);

        var copy = temp.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = "copy" });

        Assert.Equal(AuthenticationType.SqlPassword, copy.Authentication);
        Assert.Equal("app_user", copy.Username);
        Assert.Equal(source.EncryptedPassword, copy.EncryptedPassword);
        Assert.Equal(PasswordEncryptionType.DPAPI, copy.PasswordEncryption);

        // The at-rest copy never materializes plaintext for the caller.
        Assert.Null(copy.PlainPassword);
        Assert.Equal(source.EncryptedPassword, temp.RawEncryptedPassword("copy"));
    }

    [Fact]
    public void Copy_PreservesAProtectedBlobThisProcessCannotDecrypt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is a Windows-only protector.");

        using var temp = new TempStore(new DpapiConnectionPasswordProtector());
        const string Undecryptable = "bm90LWEtcmVhbC1kcGFwaS1ibG9i";
        temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "source",
            Server = "sql01",
            Database = "AppDb",
            Authentication = AuthenticationType.SqlPassword,
            Username = "app_user",
            EncryptedPassword = Undecryptable,
            PasswordEncryption = PasswordEncryptionType.DPAPI
        });

        var copy = temp.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = "copy" });

        Assert.Equal(Undecryptable, copy.EncryptedPassword);
        Assert.Equal(Undecryptable, temp.RawEncryptedPassword("copy"));
        Assert.Equal(Undecryptable, temp.RawEncryptedPassword("source"));
    }

    [Fact]
    public void Copy_RealDpapiBlobIsDuplicatedByteForByteAndStaysUsable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is a Windows-only protector.");

        using var temp = new TempStore(new DpapiConnectionPasswordProtector());
        temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "source",
            Server = "sql01",
            Database = "AppDb",
            Authentication = AuthenticationType.SqlPassword,
            Username = "app_user",
            PlainPassword = "correct horse battery staple"
        });
        var storedCipherText = temp.RawEncryptedPassword("source");
        Assert.False(string.IsNullOrEmpty(storedCipherText));

        var copy = temp.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = "copy" });

        Assert.Equal(storedCipherText, copy.EncryptedPassword);
        Assert.Equal(storedCipherText, temp.RawEncryptedPassword("copy"));
        Assert.Equal(storedCipherText, temp.RawEncryptedPassword("source"));

        // Ordinary resolution unprotects the copy, so it builds a usable connection string.
        var resolved = temp.Store.Find("copy");
        Assert.NotNull(resolved);
        var builder = resolved.BuildConnectionStringBuilder();
        Assert.Equal("app_user", builder.UserID);
        Assert.Equal("correct horse battery staple", builder.Password);

        var resolution = SqlServerConnectionResolver.Resolve(temp.Store, "copy");
        Assert.True(resolution.IsSuccess);
    }

    [Fact]
    public void Copy_DoesNotChurnTheCiphertextOfTheSourceOrOfUnrelatedProfiles()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is a Windows-only protector.");

        using var temp = new TempStore(new DpapiConnectionPasswordProtector());
        temp.Store.Add(SqlAuthProfile("source", "s3cret"));
        temp.Store.Add(SqlAuthProfile("unrelated", "another"));
        var sourceCipherText = temp.RawEncryptedPassword("source");
        var unrelatedCipherText = temp.RawEncryptedPassword("unrelated");

        temp.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = "copy" });

        Assert.Equal(sourceCipherText, temp.RawEncryptedPassword("source"));
        Assert.Equal(unrelatedCipherText, temp.RawEncryptedPassword("unrelated"));
    }

    [Fact]
    public void Copy_OverridesTheInitialCatalogAndCanClearIt()
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));

        var replaced = temp.Store.Copy(
            "source",
            new SqlServerConnectionCopyOptions { TargetName = "replaced", InitialCatalogOverride = "OtherDb" });
        var cleared = temp.Store.Copy(
            "source",
            new SqlServerConnectionCopyOptions { TargetName = "cleared", InitialCatalogOverride = "" });
        var preserved = temp.Store.Copy(
            "source",
            new SqlServerConnectionCopyOptions { TargetName = "preserved" });

        Assert.Equal("OtherDb", replaced.Database);
        Assert.Equal("OtherDb", replaced.BuildConnectionStringBuilder().InitialCatalog);
        Assert.Null(cleared.Database);
        Assert.Equal(string.Empty, cleared.BuildConnectionStringBuilder().InitialCatalog);
        Assert.Equal("SourceDb", preserved.Database);
    }

    [Fact]
    public void Copy_AppliesMetadataOverridesAndRemovalsAndPreservesEverythingElse()
    {
        using var temp = new TempStore();
        var source = FullyPopulatedProfile("source");
        source.SetMetadata("app:Role", "Bootstrap");
        source.SetMetadata("app:Owner", "team-a");
        source.SetMetadata("vendor:Untouched", "keep-me");
        temp.Store.Add(source);

        var copy = temp.Store.Copy("source", new SqlServerConnectionCopyOptions
        {
            TargetName = "copy",
            MetadataToSet = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["app:Role"] = "TestDatabase",
                ["app:RunId"] = "run-42"
            },
            MetadataToRemove = ["app:Owner"]
        });

        Assert.Equal("TestDatabase", copy.Metadata["app:Role"]);
        Assert.Equal("run-42", copy.Metadata["app:RunId"]);
        Assert.Equal("keep-me", copy.Metadata["vendor:Untouched"]);
        Assert.False(copy.Metadata.ContainsKey("app:Owner"));

        // The source keeps its own metadata unchanged.
        var reloadedSource = temp.Store.Find("source");
        Assert.NotNull(reloadedSource);
        Assert.Equal("Bootstrap", reloadedSource.Metadata["app:Role"]);
        Assert.Equal("team-a", reloadedSource.Metadata["app:Owner"]);
        Assert.False(reloadedSource.Metadata.ContainsKey("app:RunId"));
    }

    [Fact]
    public void Copy_MetadataKeysAreOrdinalAndCaseSensitive()
    {
        using var temp = new TempStore();
        var source = FullyPopulatedProfile("source");
        source.SetMetadata("app:Role", "Bootstrap");
        temp.Store.Add(source);

        var copy = temp.Store.Copy("source", new SqlServerConnectionCopyOptions
        {
            TargetName = "copy",
            MetadataToSet = new Dictionary<string, string>(StringComparer.Ordinal) { ["APP:ROLE"] = "Other" },
            MetadataToRemove = ["app:role"]
        });

        Assert.Equal("Bootstrap", copy.Metadata["app:Role"]);
        Assert.Equal("Other", copy.Metadata["APP:ROLE"]);
    }

    [Fact]
    public void Copy_LeavesTheSourceEntryByteForByteUnchanged()
    {
        using var temp = new TempStore();
        var source = FullyPopulatedProfile("source");
        source.SetMetadata("app:Role", "Bootstrap");
        temp.Store.Add(source);
        var before = temp.RawEntry("source");

        temp.Store.Copy("source", new SqlServerConnectionCopyOptions
        {
            TargetName = "copy",
            InitialCatalogOverride = "OtherDb",
            MetadataToSet = new Dictionary<string, string>(StringComparer.Ordinal) { ["app:Role"] = "TestDatabase" }
        });

        Assert.Equal(before, temp.RawEntry("source"));
    }

    [Fact]
    public void Copy_ProducesAnIndependentOptionsDictionary()
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));

        var copy = temp.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = "copy" });
        copy.Options!["Packet Size"] = "8192";

        var reloadedSource = temp.Store.Find("source");
        Assert.NotNull(reloadedSource);
        Assert.Equal("TigerQuery tests", reloadedSource.Options!["Application Name"]);
        Assert.False(reloadedSource.Options.ContainsKey("Packet Size"));
    }

    [Fact]
    public void Copy_SurvivesASaveLoadRoundTripAndCanBeDeletedThroughTheOrdinaryApi()
    {
        using var temp = new TempStore();
        var source = FullyPopulatedProfile("source");
        source.SetMetadata("app:Role", "Bootstrap");
        temp.Store.Add(source);

        var copy = temp.Store.Copy("source", new SqlServerConnectionCopyOptions
        {
            TargetName = "copy",
            InitialCatalogOverride = "OtherDb"
        });

        // A freshly constructed store over the same file sees exactly what was persisted.
        var reopened = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = temp.StorePath },
            new NoOpConnectionPasswordProtector());
        var loaded = reopened.Find("copy");
        Assert.NotNull(loaded);
        AssertEquivalentApartFrom(
            copy,
            loaded,
            nameof(SqlServerConnectionProfile.Name),
            nameof(SqlServerConnectionProfile.Database));
        Assert.Equal("copy", loaded.Name);
        Assert.Equal("OtherDb", loaded.Database);

        Assert.True(reopened.Delete("copy"));
        Assert.Null(reopened.Find("copy"));
        Assert.NotNull(reopened.Find("source"));
    }

    [Fact]
    public void Copy_RejectsAnExactDuplicateTargetNameAndLeavesTheStoreUnchanged()
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));
        temp.Store.Add(FullyPopulatedProfile("taken"));
        var before = temp.RawJson();

        var exception = Assert.Throws<InvalidOperationException>(
            () => temp.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = "taken" }));

        Assert.Contains("taken", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, temp.RawJson());
    }

    [Fact]
    public void Copy_TargetNameComparisonIsCaseSensitive()
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));
        temp.Store.Add(FullyPopulatedProfile("Taken"));

        var copy = temp.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = "taken" });

        Assert.Equal("taken", copy.Name);
        Assert.Equal(3, temp.Store.Load().Count);
    }

    [Fact]
    public void Copy_RejectsAMissingSourceAndLeavesTheStoreUnchanged()
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));
        var before = temp.RawJson();

        var exception = Assert.Throws<InvalidOperationException>(
            () => temp.Store.Copy("Source", new SqlServerConnectionCopyOptions { TargetName = "copy" }));

        Assert.Contains("Source", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, temp.RawJson());
    }

    [Fact]
    public void Copy_RejectsBlankNames()
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));

        Assert.Equal(
            "sourceName",
            Assert.Throws<ArgumentException>(
                () => temp.Store.Copy("  ", new SqlServerConnectionCopyOptions { TargetName = "copy" })).ParamName);
        Assert.Equal(
            "options",
            Assert.Throws<ArgumentException>(
                () => temp.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = " " })).ParamName);
        Assert.Throws<ArgumentNullException>(() => temp.Store.Copy("source", null!));
    }

    [Fact]
    public void Copy_RejectsInvalidMetadataMutationsAndLeavesTheStoreUnchanged()
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));
        var before = temp.RawJson();

        AssertRejectedOptions(temp, new SqlServerConnectionCopyOptions
        {
            TargetName = "copy",
            MetadataToSet = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = "value" }
        });
        AssertRejectedOptions(temp, new SqlServerConnectionCopyOptions
        {
            TargetName = "copy",
            MetadataToSet = new Dictionary<string, string>(StringComparer.Ordinal) { ["app:Role"] = null! }
        });
        AssertRejectedOptions(temp, new SqlServerConnectionCopyOptions
        {
            TargetName = "copy",
            MetadataToRemove = [""]
        });
        AssertRejectedOptions(temp, new SqlServerConnectionCopyOptions
        {
            TargetName = "copy",
            MetadataToSet = new Dictionary<string, string>(StringComparer.Ordinal) { ["app:Role"] = "value" },
            MetadataToRemove = ["app:Role"]
        });

        Assert.Equal(before, temp.RawJson());
    }

    [Fact]
    public void Copy_ValidatesTheResultAndLeavesTheStoreUnchangedWhenItIsInvalid()
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));
        var before = temp.RawJson();

        var exception = Assert.Throws<InvalidOperationException>(
            () => temp.Store.Copy(
                "source",
                new SqlServerConnectionCopyOptions { TargetName = "copy", InitialCatalogOverride = "" },
                SqlServerConnectionValidationPolicy.DatabaseRequired));

        Assert.Contains("Database is required.", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, temp.RawJson());
    }

    [Fact]
    public void Copy_DefaultValidationPolicyAllowsAServerLevelProfile()
    {
        using var temp = new TempStore();
        temp.Store.Add(FullyPopulatedProfile("source"));

        var copy = temp.Store.Copy(
            "source",
            new SqlServerConnectionCopyOptions { TargetName = "copy", InitialCatalogOverride = "" });

        Assert.Null(copy.Database);
    }

    [Fact]
    public void Copy_ValidatesCredentialPresenceWithoutRequiringPlaintext()
    {
        using var temp = new TempStore();
        temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "no-credentials",
            Server = "sql01",
            Database = "AppDb",
            Authentication = AuthenticationType.SqlPassword
        });
        temp.Store.Add(new SqlServerConnectionProfile
        {
            Name = "protected-only",
            Server = "sql01",
            Database = "AppDb",
            Authentication = AuthenticationType.SqlPassword,
            Username = "app_user",
            EncryptedPassword = "opaque",
            PasswordEncryption = PasswordEncryptionType.DPAPI
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => temp.Store.Copy("no-credentials", new SqlServerConnectionCopyOptions { TargetName = "bad" }));
        Assert.Contains("Username is required", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Password is required", exception.Message, StringComparison.Ordinal);

        var copy = temp.Store.Copy("protected-only", new SqlServerConnectionCopyOptions { TargetName = "good" });
        Assert.Equal("opaque", copy.EncryptedPassword);
    }

    [Fact]
    public void Copy_IsBoundToItsOwnStoreAndNeverReachesAnother()
    {
        using var first = new TempStore();
        using var second = new TempStore();
        first.Store.Add(FullyPopulatedProfile("source"));
        second.Store.Add(FullyPopulatedProfile("other"));

        first.Store.Copy("source", new SqlServerConnectionCopyOptions { TargetName = "copy" });

        Assert.NotNull(first.Store.Find("copy"));
        Assert.Null(second.Store.Find("copy"));
        Assert.Null(second.Store.Find("source"));

        // A profile that exists only in the other store is not a valid source here.
        Assert.Throws<InvalidOperationException>(
            () => first.Store.Copy("other", new SqlServerConnectionCopyOptions { TargetName = "cross-store" }));
        Assert.Null(first.Store.Find("cross-store"));
        Assert.Null(second.Store.Find("cross-store"));
    }

    private static void AssertRejectedOptions(TempStore temp, SqlServerConnectionCopyOptions options)
    {
        var exception = Assert.Throws<ArgumentException>(() => temp.Store.Copy("source", options));
        Assert.Equal("options", exception.ParamName);
    }

    private static SqlServerConnectionProfile SqlAuthProfile(string name, string password) => new()
    {
        Name = name,
        Server = "sql01",
        Database = "AppDb",
        Authentication = AuthenticationType.SqlPassword,
        Username = $"{name}_user",
        PlainPassword = password
    };

    // Deliberately sets every first-class property so that a field added later without
    // being carried by Copy shows up as an inequality rather than as silent data loss.
    private static SqlServerConnectionProfile FullyPopulatedProfile(string name)
    {
        var profile = new SqlServerConnectionProfile
        {
            Name = name,
            Server = "sql01\\INSTANCE,1433",
            Database = "SourceDb",
            Authentication = AuthenticationType.Integrated,
            Encrypt = EncryptOption.Mandatory,
            TrustServerCertificate = true,
            ApplicationIntent = ApplicationIntentOption.ReadOnly,
            ConnectTimeout = 42,
            MultiSubnetFailover = true,
            PersistSecurityInfo = false,
            Pooling = true,
            MinPoolSize = 2,
            MaxPoolSize = 25,
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Application Name"] = "TigerQuery tests"
            }
        };

        return profile;
    }

    // Compares the complete persisted contract rather than a hand-listed property set,
    // so a profile field introduced later is covered automatically.
    private static void AssertEquivalentApartFrom(
        SqlServerConnectionProfile expected,
        SqlServerConnectionProfile actual,
        params string[] ignoredProperties)
    {
        var expectedJson = Normalize(expected, ignoredProperties);
        var actualJson = Normalize(actual, ignoredProperties);
        Assert.Equal(expectedJson, actualJson);
    }

    private static string Normalize(SqlServerConnectionProfile profile, IReadOnlyCollection<string> ignoredProperties)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(profile));
        var kept = document.RootElement
            .EnumerateObject()
            .Where(property => !ignoredProperties.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.Value.GetRawText()}");

        return string.Join("\n", kept);
    }

    private sealed class TempStore : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "TigerQueryConnectionCopyTests",
            Guid.NewGuid().ToString("N"));

        public TempStore(IConnectionPasswordProtector? protector = null)
        {
            Directory.CreateDirectory(directory);
            StorePath = System.IO.Path.Combine(directory, "connections.json");
            Store = new SqlServerConnectionStore(
                new SqlServerConnectionStoreOptions { FilePath = StorePath },
                protector ?? new NoOpConnectionPasswordProtector());
        }

        public string StorePath { get; }

        public SqlServerConnectionStore Store { get; }

        public string RawJson() => File.ReadAllText(StorePath);

        public string RawEntry(string name)
        {
            using var document = JsonDocument.Parse(RawJson());
            return document.RootElement
                .EnumerateArray()
                .Single(element => element.GetProperty("Name").GetString() == name)
                .GetRawText();
        }

        public string? RawEncryptedPassword(string name)
        {
            using var document = JsonDocument.Parse(RawJson());
            var entry = document.RootElement
                .EnumerateArray()
                .Single(element => element.GetProperty("Name").GetString() == name);
            return entry.TryGetProperty("EncryptedPassword", out var value) ? value.GetString() : null;
        }

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
