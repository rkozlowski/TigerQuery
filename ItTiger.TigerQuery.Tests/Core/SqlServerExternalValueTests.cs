using System.Text.Json;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Core;

/// <summary>Phase 6 coverage for persisted external connection values.</summary>
public sealed class SqlServerExternalValueTests : IDisposable
{
    private readonly string storePath = Path.Combine(
        Path.GetTempPath(), "TigerQueryExternalValueTests", $"{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try
        {
            File.Delete(storePath);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a unique test file.
        }
    }

    [Fact]
    public void LegacyStringValuesLoadAndRemainLiteralStrings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        File.WriteAllText(storePath, """
            [
              {
                "Name": "legacy",
                "Server": "legacy-server",
                "Database": "legacy-db",
                "Authentication": 1,
                "Username": "legacy-user",
                "EncryptedPassword": "opaque",
                "PasswordEncryption": 1,
                "Encrypt": 1
              }
            ]
            """);

        var store = CreateStore();
        var profile = Assert.Single(store.Load());

        Assert.Equal("legacy-server", profile.ServerValue.LiteralValue);
        Assert.Equal("legacy-db", profile.DatabaseValue!.LiteralValue);
        Assert.Equal("legacy-user", profile.UsernameValue!.LiteralValue);

        store.Save([profile]);
        var persisted = File.ReadAllText(storePath);
        Assert.Contains("\"Server\": \"legacy-server\"", persisted);
        Assert.Contains("\"Database\": \"legacy-db\"", persisted);
        Assert.Contains("\"Username\": \"legacy-user\"", persisted);
        Assert.DoesNotContain("\"Source\"", persisted);
    }

    [Fact]
    public void LegacyNullServerRemainsAValidationErrorInsteadOfAFormatCrash()
    {
        var profile = JsonSerializer.Deserialize<SqlServerConnectionProfile>("""
            {
              "Name": "legacy-null",
              "Server": null,
              "Authentication": 0,
              "Encrypt": 1
            }
            """)!;

        var errors = SqlServerConnectionValidator.ValidateComplete(
            profile,
            SqlServerConnectionValidationPolicy.DatabaseOptional);

        Assert.Contains("Server is required.", errors);
    }

    [Fact]
    public void FieldReferencesResolveLazilyAcrossSensitiveAndNonSensitiveFields()
    {
        var reads = new List<string>();
        var profile = new SqlServerConnectionProfile
        {
            Name = "external-fields",
            ServerValue = EnvironmentReference("TQ_SERVER"),
            DatabaseValue = TextFileReference("database.txt"),
            Authentication = AuthenticationType.SqlPassword,
            UsernameValue = JsonFileReference("credentials.json", "username"),
            PasswordValue = JsonFileReference("credentials.json", "password"),
            Encrypt = EncryptOption.Mandatory
        };
        var options = new SqlServerExternalValueResolutionOptions
        {
            EnvironmentReader = name =>
            {
                reads.Add($"env:{name}");
                return "sql.example.test";
            },
            FileReader = path =>
            {
                reads.Add($"file:{path}");
                return path == "database.txt"
                    ? "Reporting\n"
                    : "{\"username\":\"ci_user\",\"password\":\"top-secret\"}";
            }
        };

        Assert.Empty(reads);
        Assert.Empty(SqlServerConnectionValidator.ValidateComplete(
            profile,
            SqlServerConnectionValidationPolicy.DatabaseOptional));
        Assert.Empty(reads);

        var builder = profile.BuildConnectionStringBuilder(options);

        Assert.Equal("sql.example.test", builder.DataSource);
        Assert.Equal("Reporting\n", builder.InitialCatalog);
        Assert.Equal("ci_user", builder.UserID);
        Assert.Equal("top-secret", builder.Password);
        Assert.Equal(
            ["env:TQ_SERVER", "file:database.txt", "file:credentials.json", "file:credentials.json"],
            reads);
    }

    [Fact]
    public void FullConnectionStringReferenceResolvesAtEffectiveBuildTime()
    {
        var readCount = 0;
        var profile = new SqlServerConnectionProfile
        {
            Name = "full",
            ConnectionStringValue = EnvironmentReference("TQ_CONNECTION_STRING")
        };
        var options = new SqlServerExternalValueResolutionOptions
        {
            EnvironmentReader = _ =>
            {
                readCount++;
                return "Server=full-server;Database=full-db;Integrated Security=true;Encrypt=false";
            }
        };

        Assert.Equal(0, readCount);
        Assert.Empty(SqlServerConnectionValidator.ValidateComplete(
            profile,
            SqlServerConnectionValidationPolicy.DatabaseRequired));
        Assert.Equal(0, readCount);

        var builder = profile.BuildConnectionStringBuilder(options);

        Assert.Equal(1, readCount);
        Assert.Equal("full-server", builder.DataSource);
        Assert.Equal("full-db", builder.InitialCatalog);
        Assert.True(builder.IntegratedSecurity);
    }

    [Fact]
    public void FullConnectionStringAndFieldModesAreStrictlyMutuallyExclusive()
    {
        var profile = new SqlServerConnectionProfile
        {
            Name = "mixed",
            ConnectionStringValue = EnvironmentReference("TQ_CONNECTION_STRING"),
            Server = "must-not-win"
        };

        var errors = SqlServerConnectionValidator.ValidateComplete(
            profile,
            SqlServerConnectionValidationPolicy.DatabaseOptional);
        var exception = Assert.Throws<InvalidOperationException>(
            () => profile.BuildConnectionStringBuilder());

        Assert.Contains(errors, error => error.Contains("cannot be combined", StringComparison.Ordinal));
        Assert.Contains("cannot be combined", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalFieldsDoNotDisableConnectionOptionValidation()
    {
        var profile = FieldProfile(EnvironmentReference("TQ_SERVER"));
        profile.Options = new Dictionary<string, string>
        {
            ["not-a-sqlclient-option"] = "value"
        };

        var errors = SqlServerConnectionValidator.ValidateComplete(
            profile,
            SqlServerConnectionValidationPolicy.DatabaseOptional);

        Assert.Contains(errors, error =>
            error.Contains("not supported", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CredentialReferencesRequireSqlAuthenticationWithoutBeingRead()
    {
        var reads = 0;
        var profile = FieldProfile(SqlServerConnectionValue.Literal("server"));
        profile.PasswordValue = EnvironmentReference("TQ_PASSWORD");

        var errors = SqlServerConnectionValidator.ValidateComplete(
            profile,
            SqlServerConnectionValidationPolicy.DatabaseOptional);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            profile.BuildConnectionStringBuilder(new SqlServerExternalValueResolutionOptions
            {
                EnvironmentReader = _ =>
                {
                    reads++;
                    return "secret";
                }
            }));

        Assert.Contains(errors, error => error.Contains(
            "require SQL password authentication",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains("require SQL password authentication", exception.Message);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void ExternalAndProtectedPasswordRepresentationsCannotBeCombined()
    {
        var profile = FieldProfile(SqlServerConnectionValue.Literal("server"));
        profile.Authentication = AuthenticationType.SqlPassword;
        profile.Username = "user";
        profile.EncryptedPassword = "opaque-secret";
        profile.PasswordEncryption = PasswordEncryptionType.DPAPI;
        profile.PasswordValue = EnvironmentReference("TQ_PASSWORD");

        var errors = SqlServerConnectionValidator.ValidateComplete(
            profile,
            SqlServerConnectionValidationPolicy.DatabaseOptional);
        var exception = Assert.Throws<InvalidOperationException>(
            () => profile.BuildConnectionStringBuilder());

        Assert.Contains(errors, error => error.Contains("cannot be combined", StringComparison.Ordinal));
        Assert.DoesNotContain("opaque-secret", exception.Message);
    }

    [Theory]
    [InlineData(null, "missing")]
    [InlineData("", "empty")]
    public void MissingOrEmptyRequiredEnvironmentValueFailsClearlyAndSafely(
        string? externalValue,
        string expected)
    {
        var profile = FieldProfile(EnvironmentReference("TQ_SAFE_NAME"));

        var exception = Assert.Throws<SqlServerExternalValueException>(() =>
            profile.BuildConnectionStringBuilder(new SqlServerExternalValueResolutionOptions
            {
                EnvironmentReader = _ => externalValue
            }));

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TQ_SAFE_NAME", exception.Message);
    }

    [Theory]
    [InlineData("not json", "malformed JSON")]
    [InlineData("[]", "top-level JSON object")]
    [InlineData("{\"other\":\"value\"}", "missing")]
    [InlineData("{\"password\":42}", "JSON string")]
    public void KeyedJsonFailuresAreClearAndNeverIncludeFileContents(
        string fileContents,
        string expected)
    {
        const string ContentsMarker = "other";
        var profile = FieldProfile(SqlServerConnectionValue.Literal("server"));
        profile.Authentication = AuthenticationType.SqlPassword;
        profile.Username = "user";
        profile.PasswordValue = JsonFileReference("credentials.json", "password");

        var exception = Assert.Throws<SqlServerExternalValueException>(() =>
            profile.BuildConnectionStringBuilder(new SqlServerExternalValueResolutionOptions
            {
                FileReader = _ => fileContents
            }));

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fileContents, exception.Message, StringComparison.Ordinal);
        if (fileContents.Contains(ContentsMarker, StringComparison.Ordinal))
            Assert.DoesNotContain(ContentsMarker, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFileFailsClearlyWithoutLeakingTheReaderException()
    {
        const string Secret = "filesystem-detail-secret";
        var profile = FieldProfile(TextFileReference("missing-server.txt"));

        var exception = Assert.Throws<SqlServerExternalValueException>(() =>
            profile.BuildConnectionStringBuilder(new SqlServerExternalValueResolutionOptions
            {
                FileReader = _ => throw new FileNotFoundException(Secret)
            }));

        Assert.Contains("missing-server.txt", exception.Message);
        Assert.Contains("could not be read", exception.Message);
        Assert.DoesNotContain(Secret, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void UnknownSourceFailsInsteadOfBecomingALiteral()
    {
        const string UnknownSource = "FutureSecretProvider";
        var json = $$"""
            {
              "Name": "future",
              "Server": { "Source": "{{UnknownSource}}", "Name": "TQ_SERVER" },
              "Authentication": 0,
              "Encrypt": 1
            }
            """;

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SqlServerConnectionProfile>(json));

        Assert.Contains("Source", exception.Message);
        Assert.Contains("not supported", exception.Message);
        Assert.DoesNotContain(UnknownSource, exception.Message);
    }

    [Fact]
    public void ReaderExceptionsAreWrappedWithoutLeakingTheirMessages()
    {
        const string Secret = "reader-exception-secret";
        var profile = FieldProfile(EnvironmentReference("TQ_SERVER"));

        var exception = Assert.Throws<SqlServerExternalValueException>(() =>
            profile.BuildConnectionStringBuilder(new SqlServerExternalValueResolutionOptions
            {
                EnvironmentReader = _ => throw new InvalidOperationException(Secret)
            }));

        Assert.Contains("could not be read", exception.Message);
        Assert.DoesNotContain(Secret, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void MalformedProgrammaticReferencesCannotBePersisted()
    {
        var value = SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.File,
            Path = "secret.json",
            Format = SqlServerExternalFileFormat.Json
        });

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Serialize(value));

        Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.json", exception.Message);
    }

    [Fact]
    public void ResolvedValuesAreNeverWrittenBack()
    {
        const string Secret = "resolved-password-never-persist";
        var profile = FieldProfile(EnvironmentReference("TQ_SERVER"));
        profile.Authentication = AuthenticationType.SqlPassword;
        profile.UsernameValue = EnvironmentReference("TQ_USERNAME");
        profile.PasswordValue = EnvironmentReference("TQ_PASSWORD");
        var before = JsonSerializer.Serialize(profile);

        _ = profile.BuildConnectionStringBuilder(new SqlServerExternalValueResolutionOptions
        {
            EnvironmentReader = name => name switch
            {
                "TQ_SERVER" => "server",
                "TQ_USERNAME" => "user",
                "TQ_PASSWORD" => Secret,
                _ => null
            }
        });

        var after = JsonSerializer.Serialize(profile);
        Assert.Equal(before, after);
        Assert.DoesNotContain(Secret, after);
        Assert.Contains("TQ_PASSWORD", after);

        var store = CreateStore();
        store.Add(profile);
        var persistedBeforeResolution = File.ReadAllText(storePath);
        var resolution = SqlServerConnectionResolver.Resolve(
            store,
            profile.Name,
            new SqlServerExternalValueResolutionOptions
            {
                EnvironmentReader = name => name switch
                {
                    "TQ_SERVER" => "server",
                    "TQ_USERNAME" => "user",
                    "TQ_PASSWORD" => Secret,
                    _ => null
                }
            });

        Assert.True(resolution.IsSuccess, resolution.ErrorMessage);
        Assert.Equal(persistedBeforeResolution, File.ReadAllText(storePath));
        Assert.DoesNotContain(Secret, File.ReadAllText(storePath));
    }

    [Fact]
    public void CopyPreservesReferencesWithoutResolvingThem()
    {
        var reads = 0;
        var store = CreateStore();
        var source = FieldProfile(EnvironmentReference("TQ_SERVER"));
        source.Name = "source";
        source.DatabaseValue = JsonFileReference("config.json", "database");
        store.Add(source);

        var copy = store.Copy(
            "source",
            new SqlServerConnectionCopyOptions { TargetName = "copy" });

        Assert.Equal(0, reads);
        Assert.True(copy.ServerValue.IsReference);
        Assert.Equal("TQ_SERVER", copy.ServerValue.Reference!.Name);
        Assert.True(copy.DatabaseValue!.IsReference);
        Assert.Equal("database", copy.DatabaseValue.Reference!.Key);

        var builder = copy.BuildConnectionStringBuilder(new SqlServerExternalValueResolutionOptions
        {
            EnvironmentReader = _ =>
            {
                reads++;
                return "copied-server";
            },
            FileReader = _ =>
            {
                reads++;
                return "{\"database\":\"copied-db\"}";
            }
        });
        Assert.Equal(2, reads);
        Assert.Equal("copied-server", builder.DataSource);
        Assert.Equal("copied-db", builder.InitialCatalog);
    }

    [Fact]
    public void CopyDatabaseOverrideReplacesOnlyThatReferenceWithALiteral()
    {
        var store = CreateStore();
        var source = FieldProfile(EnvironmentReference("TQ_SERVER"));
        source.Name = "source";
        source.DatabaseValue = EnvironmentReference("TQ_DATABASE");
        store.Add(source);

        var copy = store.Copy(
            "source",
            new SqlServerConnectionCopyOptions
            {
                TargetName = "copy",
                InitialCatalogOverride = "copy-db"
            });

        Assert.True(copy.ServerValue.IsReference);
        Assert.False(copy.DatabaseValue!.IsReference);
        Assert.Equal("copy-db", copy.DatabaseValue.LiteralValue);
    }

    [Fact]
    public void FullConnectionStringCopyPreservesTheReferenceAndRejectsDatabaseOverride()
    {
        var store = CreateStore();
        store.Add(new SqlServerConnectionProfile
        {
            Name = "source",
            ConnectionStringValue = EnvironmentReference("TQ_CONNECTION_STRING")
        });

        var copy = store.Copy(
            "source",
            new SqlServerConnectionCopyOptions { TargetName = "copy" });
        var exception = Assert.Throws<InvalidOperationException>(() => store.Copy(
            "source",
            new SqlServerConnectionCopyOptions
            {
                TargetName = "invalid-copy",
                InitialCatalogOverride = "db"
            }));

        Assert.Equal("TQ_CONNECTION_STRING", copy.ConnectionStringValue!.Reference!.Name);
        Assert.Contains("cannot be combined", exception.Message);
        Assert.Null(store.Find("invalid-copy"));
    }

    [Fact]
    public void ResolverRedactsMalformedFullConnectionStringValue()
    {
        const string Secret = "Password=do-not-leak;this is not valid";
        var store = CreateStore();
        store.Add(new SqlServerConnectionProfile
        {
            Name = "bad-full",
            ConnectionStringValue = EnvironmentReference("TQ_CONNECTION_STRING")
        });

        var result = SqlServerConnectionResolver.Resolve(
            store,
            "bad-full",
            new SqlServerExternalValueResolutionOptions { EnvironmentReader = _ => Secret });

        Assert.False(result.IsSuccess);
        Assert.Contains("not valid", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, result.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-leak", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceDescriptionsNeverResolveAndLiteralConnectionStringsAreRedacted()
    {
        var fullReference = new SqlServerConnectionProfile
        {
            ConnectionStringValue = TextFileReference("/run/secrets/sql-connection")
        };
        var fullLiteral = new SqlServerConnectionProfile
        {
            ConnectionStringValue = SqlServerConnectionValue.Literal(
                "Server=private;Password=secret")
        };

        Assert.Equal(
            "text file '/run/secrets/sql-connection'",
            fullReference.DescribeConnectionString());
        Assert.Equal("<redacted literal>", fullLiteral.DescribeConnectionString());
    }

    private SqlServerConnectionStore CreateStore() => new(
        new SqlServerConnectionStoreOptions { FilePath = storePath },
        new NonPersistingConnectionPasswordProtector());

    private static SqlServerConnectionProfile FieldProfile(SqlServerConnectionValue server) => new()
    {
        Name = "profile",
        ServerValue = server,
        Authentication = AuthenticationType.Integrated,
        Encrypt = EncryptOption.Mandatory
    };

    private static SqlServerConnectionValue EnvironmentReference(string name) =>
        SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.EnvironmentVariable,
            Name = name
        });

    private static SqlServerConnectionValue TextFileReference(string path) =>
        SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.File,
            Path = path,
            Format = SqlServerExternalFileFormat.Text
        });

    private static SqlServerConnectionValue JsonFileReference(string path, string key) =>
        SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.File,
            Path = path,
            Format = SqlServerExternalFileFormat.Json,
            Key = key
        });
}
