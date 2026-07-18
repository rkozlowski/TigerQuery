using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Core;

public sealed class SqlServerConnectionStoreMetadataQueryTests
{
    [Fact]
    public void QueryByMetadata_EqualsMatchesExactValueAndRejectsMismatch()
    {
        using var temp = new TempStore();
        temp.Add("matching", ("app.role", "worker"));
        temp.Add("different", ("app.role", "reader"));

        var matches = temp.Store.QueryByMetadata(
        [
            EqualsFilter("app.role", "worker")
        ]);

        Assert.Collection(matches, profile => Assert.Equal("matching", profile.Name));
    }

    [Fact]
    public void QueryByMetadata_IsSetMatchesNonEmptyValue()
    {
        using var temp = new TempStore();
        temp.Add("set", ("app.role", "worker"));
        temp.Add("missing");

        var matches = temp.Store.QueryByMetadata(
        [
            new SqlServerConnectionMetadataFilter
            {
                Key = "app.role",
                Operator = SqlServerConnectionMetadataFilterOperator.IsSet
            }
        ]);

        Assert.Collection(matches, profile => Assert.Equal("set", profile.Name));
    }

    [Fact]
    public void QueryByMetadata_IsSetMatchesEmptyValue()
    {
        using var temp = new TempStore();
        temp.Add("empty", ("app.role", ""));
        temp.Add("missing");

        var matches = temp.Store.QueryByMetadata(
        [
            new SqlServerConnectionMetadataFilter
            {
                Key = "app.role",
                Operator = SqlServerConnectionMetadataFilterOperator.IsSet
            }
        ]);

        Assert.Collection(matches, profile => Assert.Equal("empty", profile.Name));
    }

    [Fact]
    public void QueryByMetadata_IsNotSetMatchesOnlyMissingKey()
    {
        using var temp = new TempStore();
        temp.Add("empty", ("app.role", ""));
        temp.Add("missing");

        var matches = temp.Store.QueryByMetadata(
        [
            new SqlServerConnectionMetadataFilter
            {
                Key = "app.role",
                Operator = SqlServerConnectionMetadataFilterOperator.IsNotSet
            }
        ]);

        Assert.Collection(matches, profile => Assert.Equal("missing", profile.Name));
    }

    [Fact]
    public void QueryByMetadata_MultipleFiltersUseAndSemantics()
    {
        using var temp = new TempStore();
        temp.Add("both", ("app.role", "worker"), ("app.region", "west"));
        temp.Add("role-only", ("app.role", "worker"));
        temp.Add("region-only", ("app.region", "west"));

        var matches = temp.Store.QueryByMetadata(
        [
            EqualsFilter("app.role", "worker"),
            EqualsFilter("app.region", "west")
        ]);

        Assert.Collection(matches, profile => Assert.Equal("both", profile.Name));
    }

    [Fact]
    public void QueryByMetadata_DistinguishesKeyCase()
    {
        using var temp = new TempStore();
        temp.Add("lower", ("app.role", "worker"));
        temp.Add("upper", ("App.Role", "worker"));

        var matches = temp.Store.QueryByMetadata(
        [
            EqualsFilter("app.role", "worker")
        ]);

        Assert.Collection(matches, profile => Assert.Equal("lower", profile.Name));
    }

    [Fact]
    public void QueryByMetadata_DistinguishesValueCase()
    {
        using var temp = new TempStore();
        temp.Add("lower", ("app.role", "worker"));
        temp.Add("upper", ("app.role", "Worker"));

        var matches = temp.Store.QueryByMetadata(
        [
            EqualsFilter("app.role", "worker")
        ]);

        Assert.Collection(matches, profile => Assert.Equal("lower", profile.Name));
    }

    [Fact]
    public void QueryByMetadata_RejectsInvalidFilterShapes()
    {
        using var temp = new TempStore();

        Assert.Throws<ArgumentException>(() => temp.Store.QueryByMetadata(
        [
            EqualsFilter("", "value")
        ]));
        Assert.Throws<ArgumentException>(() => temp.Store.QueryByMetadata(
        [
            new SqlServerConnectionMetadataFilter
            {
                Key = "app.role",
                Operator = SqlServerConnectionMetadataFilterOperator.Equals
            }
        ]));
        Assert.Throws<ArgumentException>(() => temp.Store.QueryByMetadata(
        [
            new SqlServerConnectionMetadataFilter
            {
                Key = "app.role",
                Operator = SqlServerConnectionMetadataFilterOperator.IsSet,
                Value = ""
            }
        ]));
        Assert.Throws<ArgumentException>(() => temp.Store.QueryByMetadata(
        [
            new SqlServerConnectionMetadataFilter
            {
                Key = "app.role",
                Operator = SqlServerConnectionMetadataFilterOperator.IsNotSet,
                Value = "value"
            }
        ]));
        Assert.Throws<ArgumentException>(() => temp.Store.QueryByMetadata(
        [
            new SqlServerConnectionMetadataFilter
            {
                Key = "app.role",
                Operator = (SqlServerConnectionMetadataFilterOperator)999
            }
        ]));
    }

    [Fact]
    public void QueryByMetadata_PreservesStoreOrder()
    {
        using var temp = new TempStore();
        temp.Add("zeta", ("app.role", "worker"));
        temp.Add("alpha", ("app.role", "worker"));
        temp.Add("middle", ("app.role", "worker"));

        var matches = temp.Store.QueryByMetadata(
        [
            EqualsFilter("app.role", "worker")
        ]);

        Assert.Equal(["zeta", "alpha", "middle"], matches.Select(profile => profile.Name));
    }

    [Fact]
    public void QueryByMetadata_ProfilesWithoutMetadataParticipateInSetSemantics()
    {
        using var temp = new TempStore();
        temp.Add("without-metadata");
        temp.Add("with-metadata", ("app.role", "worker"));

        var equalsMatches = temp.Store.QueryByMetadata(
        [
            EqualsFilter("app.role", "worker")
        ]);
        var isNotSetMatches = temp.Store.QueryByMetadata(
        [
            new SqlServerConnectionMetadataFilter
            {
                Key = "app.role",
                Operator = SqlServerConnectionMetadataFilterOperator.IsNotSet
            }
        ]);

        Assert.Collection(equalsMatches, profile => Assert.Equal("with-metadata", profile.Name));
        Assert.Collection(isNotSetMatches, profile => Assert.Equal("without-metadata", profile.Name));
    }

    private static SqlServerConnectionMetadataFilter EqualsFilter(string key, string value) => new()
    {
        Key = key,
        Operator = SqlServerConnectionMetadataFilterOperator.Equals,
        Value = value
    };

    private sealed class TempStore : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            "TigerQueryMetadataQueryTests",
            Guid.NewGuid().ToString("N"));

        public TempStore()
        {
            Directory.CreateDirectory(directory);
            Store = new SqlServerConnectionStore(
                new SqlServerConnectionStoreOptions
                {
                    FilePath = Path.Combine(directory, "connections.json")
                },
                new NoOpConnectionPasswordProtector());
        }

        public SqlServerConnectionStore Store { get; }

        public void Add(string name, params (string Key, string Value)[] metadata)
        {
            var profile = new SqlServerConnectionProfile
            {
                Name = name,
                Server = $"{name}-server",
                Authentication = AuthenticationType.Integrated,
                Encrypt = EncryptOption.Mandatory
            };

            foreach (var (key, value) in metadata)
                profile.SetMetadata(key, value);

            Store.Add(profile);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; every test uses a unique directory.
            }
        }
    }
}
