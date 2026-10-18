using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Core;

/// <summary>
/// A SQL-password profile whose stored password is unavailable after loading must be
/// reported as exactly that, before any login is attempted, and never by echoing the
/// protected value or the password.
/// </summary>
public sealed class StoredPasswordDiagnosticsTests : IDisposable
{
    private const string SyntheticPassword = "synthetic-stored-password-4be1";

    // Valid Base64 that no Windows account can unprotect: the at-rest shape of a store
    // copied from another user or computer.
    private const string ForeignCiphertext = "Zm9yZWlnbi1kcGFwaS1ibG9iLWZyb20tYW5vdGhlci11c2Vy";

    private readonly string storePath = Path.Combine(
        Path.GetTempPath(), "TigerQueryStoredPasswordTests", $"{Guid.NewGuid():N}.json");

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
    public void ResolvingAPasswordThatCannotBeDecryptedReportsTheStoreNotALoginFailure()
    {
        WriteStore(ForeignCiphertext, encryption: 1);
        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = storePath });

        var resolution = SqlServerConnectionResolver.Resolve(store, "copied");

        Assert.False(resolution.IsSuccess);
        Assert.Contains("Saved connection 'copied'", resolution.ErrorMessage);
        Assert.Contains("DPAPI-protected password that could not be decrypted here", resolution.ErrorMessage);
        Assert.Contains("the Windows account that saved it", resolution.ErrorMessage);
        Assert.DoesNotContain(ForeignCiphertext, resolution.ErrorMessage);
    }

    [Fact]
    public void ResolvingAPasswordInAnUnknownProtectionFormatNamesTheFormat()
    {
        WriteStore(ForeignCiphertext, encryption: 7);
        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = storePath });

        var resolution = SqlServerConnectionResolver.Resolve(store, "copied");

        Assert.False(resolution.IsSuccess);
        Assert.Contains("protected as '7'", resolution.ErrorMessage);
        Assert.DoesNotContain(ForeignCiphertext, resolution.ErrorMessage);
    }

    [Fact]
    public void ResolvingAProfileWhoseStoreDiscardedThePasswordSaysNoPasswordIsSaved()
    {
        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = storePath },
            new NonPersistingConnectionPasswordProtector());
        store.Add(SqlProfile("discarded"));

        var resolution = SqlServerConnectionResolver.Resolve(store, "discarded");

        Assert.False(resolution.IsSuccess);
        Assert.Contains("has no saved password", resolution.ErrorMessage);
        Assert.Contains("external password reference", resolution.ErrorMessage);
        Assert.DoesNotContain(SyntheticPassword, File.ReadAllText(storePath));
    }

    [Fact]
    public void ProfilesThatDoNotDependOnAStoredPasswordStillResolve()
    {
        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = storePath },
            new NonPersistingConnectionPasswordProtector());
        store.Add(new SqlServerConnectionProfile { Name = "integrated", Server = "srv" });
        var referenced = SqlProfile("referenced");
        referenced.PlainPassword = null;
        referenced.PasswordValue = SqlServerConnectionValue.External(new SqlServerExternalValueReference
        {
            Source = SqlServerExternalValueSource.EnvironmentVariable,
            Name = "TQ_TEST_PASSWORD"
        });
        store.Add(referenced);

        var integrated = SqlServerConnectionResolver.Resolve(store, "integrated");
        var external = SqlServerConnectionResolver.Resolve(
            store,
            "referenced",
            new SqlServerExternalValueResolutionOptions { EnvironmentReader = _ => SyntheticPassword });

        Assert.True(integrated.IsSuccess, integrated.ErrorMessage);
        Assert.True(external.IsSuccess, external.ErrorMessage);
        Assert.Contains(SyntheticPassword, external.ConnectionString);
    }

    [Fact]
    public void StoresReportWhetherTheirProtectorKeepsPasswords()
    {
        var options = new SqlServerConnectionStoreOptions { FilePath = storePath };

        Assert.False(new SqlServerConnectionStore(options, new NonPersistingConnectionPasswordProtector())
            .PersistsPasswords());
        Assert.False(new SqlServerConnectionStore(options, new NoOpConnectionPasswordProtector())
            .PersistsPasswords());
        if (OperatingSystem.IsWindows())
        {
            Assert.True(new SqlServerConnectionStore(options, new DpapiConnectionPasswordProtector())
                .PersistsPasswords());
        }

        Assert.False(File.Exists(storePath));
    }

    [Fact]
    public void DpapiProtectedPasswordsRoundTripWithoutPlainTextAtRest()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var store = new SqlServerConnectionStore(
            new SqlServerConnectionStoreOptions { FilePath = storePath },
            new DpapiConnectionPasswordProtector());
        store.Add(SqlProfile("protected"));

        var resolution = SqlServerConnectionResolver.Resolve(store, "protected");

        Assert.True(resolution.IsSuccess, resolution.ErrorMessage);
        Assert.Contains(SyntheticPassword, resolution.ConnectionString);
        Assert.DoesNotContain(SyntheticPassword, File.ReadAllText(storePath));
    }

    private static SqlServerConnectionProfile SqlProfile(string name) => new()
    {
        Name = name,
        Server = "srv",
        Authentication = AuthenticationType.SqlPassword,
        Username = "app-login",
        PlainPassword = SyntheticPassword
    };

    private void WriteStore(string encryptedPassword, int encryption)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        File.WriteAllText(storePath, $$"""
            [
              {
                "Name": "copied",
                "Server": "srv",
                "Authentication": 1,
                "Username": "app-login",
                "EncryptedPassword": "{{encryptedPassword}}",
                "PasswordEncryption": {{encryption}},
                "Encrypt": 1
              }
            ]
            """);
    }
}
