using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Core;

/// <summary>
/// Covers the reserved E2E metadata grammar: the exact keys, the exact values, and the
/// refusal to guess at anything else. A spelling this file rejects is a spelling that can
/// never authorize E2E work.
/// </summary>
public sealed class SqlServerE2eMetadataTests
{
    [Fact]
    public void TheReservedKeysAndValuesAreTheDocumentedLiterals()
    {
        Assert.Equal("ittiger.e2e.", SqlServerE2eMetadata.ReservedKeyPrefix);
        Assert.Equal("ittiger.e2e.enabled", SqlServerE2eMetadata.Enabled);
        Assert.Equal("ittiger.e2e.bootstrap", SqlServerE2eMetadata.Bootstrap);
        Assert.Equal("ittiger.e2e.allow-database-create", SqlServerE2eMetadata.AllowDatabaseCreation);
        Assert.Equal("ittiger.e2e.session-id", SqlServerE2eMetadata.SessionId);
        Assert.Equal("ittiger.e2e.database.name", SqlServerE2eMetadata.DatabaseName);
        Assert.Equal("ittiger.e2e.database.allow-drop", SqlServerE2eMetadata.AllowDatabaseDrop);
        Assert.Equal("true", SqlServerE2eMetadata.True);
        Assert.Equal("false", SqlServerE2eMetadata.False);
    }

    [Fact]
    public void AnAbsentKeyIsAbsentRatherThanFalse()
    {
        Assert.Equal(
            SqlServerE2eFlagState.Absent,
            SqlServerE2eMetadata.ReadFlag(Profile(), SqlServerE2eMetadata.Enabled));
    }

    [Theory]
    [InlineData("true", SqlServerE2eFlagState.True)]
    [InlineData("false", SqlServerE2eFlagState.False)]
    public void TheCanonicalValuesAreAccepted(string value, SqlServerE2eFlagState expected)
    {
        var profile = Profile();
        profile.SetReservedMetadata(SqlServerE2eMetadata.Enabled, value);

        Assert.Equal(expected, SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Enabled));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("tRue")]
    [InlineData(" true")]
    [InlineData("true ")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("Y")]
    [InlineData("on")]
    [InlineData("")]
    [InlineData("False")]
    [InlineData("0")]
    public void EveryOtherSpellingIsMalformedRatherThanFalse(string value)
    {
        var profile = Profile();
        profile.SetReservedMetadata(SqlServerE2eMetadata.Enabled, value);

        // The distinction is the whole point: a typo must fail loudly instead of quietly
        // revoking an authorization its author believed they had written.
        Assert.Equal(
            SqlServerE2eFlagState.Malformed,
            SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Enabled));
    }

    [Theory]
    [InlineData("ITTIGER.E2E.ENABLED")]
    [InlineData("Ittiger.E2e.Enabled")]
    [InlineData("ittiger.e2e.Enabled")]
    [InlineData("ittiger.e2e.enabled ")]
    public void AKeyThatDiffersInCaseOrSpacingConfersNothing(string key)
    {
        var profile = Profile();
        if (SqlServerE2eMetadata.IsReservedKey(key))
            profile.SetReservedMetadata(key, SqlServerE2eMetadata.True);
        else
            profile.SetMetadata(key, SqlServerE2eMetadata.True);

        Assert.Equal(
            SqlServerE2eFlagState.Absent,
            SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Enabled));
    }

    [Fact]
    public void TheReservedFlagsAreReadIndependently()
    {
        var profile = Profile();
        profile.SetReservedMetadata(SqlServerE2eMetadata.Enabled, SqlServerE2eMetadata.True);

        Assert.Equal(
            SqlServerE2eFlagState.True,
            SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Enabled));
        Assert.Equal(
            SqlServerE2eFlagState.Absent,
            SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Bootstrap));
        Assert.Equal(
            SqlServerE2eFlagState.Absent,
            SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.AllowDatabaseCreation));
    }

    [Theory]
    [InlineData(SqlServerE2eMetadata.Enabled)]
    [InlineData(SqlServerE2eMetadata.Bootstrap)]
    [InlineData(SqlServerE2eMetadata.SessionId)]
    [InlineData(SqlServerE2eMetadata.DatabaseName)]
    [InlineData(SqlServerE2eMetadata.AllowDatabaseDrop)]
    [InlineData("ittiger.e2e.future.setting")]
    public void GenericProfileMutationsRejectKnownAndUnknownReservedKeys(string key)
    {
        var profile = Profile();
        profile.SetReservedMetadata(key, "existing");

        var setError = Assert.Throws<ArgumentException>(() => profile.SetMetadata(key, "new"));
        var removeError = Assert.Throws<ArgumentException>(() => profile.RemoveMetadata(key));

        Assert.Contains("reserved", setError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reserved", removeError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("existing", profile.Metadata[key]);
    }

    [Fact]
    public void TigerQueryOwnedAuthorizationWritesOnlyTheCanonicalPermissionKeys()
    {
        var profile = Profile();

        SqlServerE2eMetadata.AuthorizeNewProfile(profile, allowDatabaseCreation: true);

        Assert.Equal(2, profile.Metadata.Count);
        Assert.Equal(SqlServerE2eMetadata.True, profile.Metadata[SqlServerE2eMetadata.Enabled]);
        Assert.Equal(
            SqlServerE2eMetadata.True,
            profile.Metadata[SqlServerE2eMetadata.AllowDatabaseCreation]);
    }

    [Fact]
    public void TigerQueryOwnedBootstrapAuthorizationWritesAllCanonicalBootstrapKeys()
    {
        var profile = Profile();

        SqlServerE2eMetadata.AuthorizeNewBootstrapProfile(
            profile,
            allowDatabaseCreation: true);

        Assert.Equal(3, profile.Metadata.Count);
        Assert.Equal(SqlServerE2eMetadata.True, profile.Metadata[SqlServerE2eMetadata.Enabled]);
        Assert.Equal(SqlServerE2eMetadata.True, profile.Metadata[SqlServerE2eMetadata.Bootstrap]);
        Assert.Equal(
            SqlServerE2eMetadata.True,
            profile.Metadata[SqlServerE2eMetadata.AllowDatabaseCreation]);
    }

    [Theory]
    [InlineData("ittiger.e2e.enabled", true)]
    [InlineData("ittiger.e2e.anything.at.all", true)]
    [InlineData("ittiger.e2e.", true)]
    [InlineData("ittiger.anything.at.all", false)]
    [InlineData("ittiger.", false)]
    [InlineData("ITTIGER.e2e.enabled", false)]
    [InlineData("yourvendor.yourapp.role", false)]
    [InlineData("ittige", false)]
    [InlineData("", false)]
    public void TheReservedNamespaceIsMatchedOrdinally(string key, bool expected) =>
        Assert.Equal(expected, SqlServerE2eMetadata.IsReservedKey(key));

    [Fact]
    public void ArgumentGuardsRejectNullAndEmptyInput()
    {
        Assert.Throws<ArgumentNullException>(() => SqlServerE2eMetadata.IsReservedKey(null!));
        Assert.Throws<ArgumentNullException>(
            () => SqlServerE2eMetadata.ReadFlag(null!, SqlServerE2eMetadata.Enabled));
        Assert.Throws<ArgumentNullException>(() => SqlServerE2eMetadata.ReadFlag(Profile(), null!));
        Assert.Throws<ArgumentException>(() => SqlServerE2eMetadata.ReadFlag(Profile(), string.Empty));
    }

    private static SqlServerConnectionProfile Profile() => new()
    {
        Name = "bootstrap",
        Server = "sql01",
        Authentication = AuthenticationType.Integrated
    };
}
