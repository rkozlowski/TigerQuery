using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Sockets;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Core;

/// <summary>
/// Covers the E2E authorization boundary: that a profile is eligible only because it says
/// so, that the bootstrap connection is chosen by name and never inferred, that every
/// refusal is a distinguishable outcome rather than a silent one, and that resolving
/// contacts no server.
/// </summary>
public sealed class SqlServerE2eConnectionResolverTests
{
    private const string Bootstrap = "tiger-sqlcmd-e2e";

    // ---- Authorization is explicit ----

    [Fact]
    public void AnOrdinaryProfileIsNeverAnE2eProfile()
    {
        using var temp = new TempStore();
        temp.Seed(Profile(Bootstrap));

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });

        // It exists, it is valid, it would connect. None of that is authorization.
        AssertRefused(SqlServerE2eResolutionStatus.Invalid, resolution);
        Assert.Contains(
            resolution.Errors,
            error => error.Contains(SqlServerE2eMetadata.Enabled, StringComparison.Ordinal));
        Assert.Empty(resolution.CandidateNames);
    }

    [Fact]
    public void AnEnabledProfileNamedExplicitlyResolves()
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile(Bootstrap)));

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });

        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, resolution.Status);
        Assert.NotNull(resolution.Profile);
        Assert.Equal(Bootstrap, resolution.Profile!.Name);
        Assert.Equal(Bootstrap, resolution.RequestedName);
        Assert.Empty(resolution.Errors);
        Assert.Empty(resolution.CandidateNames);
    }

    [Fact]
    public void TheHostConfiguredDefaultNameResolvesTheSameWay()
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile(Bootstrap)));

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            DefaultConnectionName = Bootstrap
        });

        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(Bootstrap, resolution.Profile!.Name);
    }

    [Fact]
    public void AnExplicitNameWinsOverTheHostConfiguredDefault()
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile(Bootstrap)), Enabled(Profile("other-e2e")));

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = "other-e2e",
            DefaultConnectionName = Bootstrap
        });

        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, resolution.Status);
        Assert.Equal("other-e2e", resolution.Profile!.Name);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData(" true ")]
    [InlineData("")]
    public void NonCanonicalBooleanMetadataIsRejectedWithItsOwnError(string value)
    {
        using var temp = new TempStore();
        var profile = Profile(Bootstrap);
        profile.SetReservedMetadata(SqlServerE2eMetadata.Enabled, value);
        temp.Seed(profile);

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });

        AssertRefused(SqlServerE2eResolutionStatus.Invalid, resolution);

        // Specifically a malformed-value complaint, not the "you never set it" one: the
        // author did set it, and telling them otherwise sends them looking in the wrong place.
        var error = Assert.Single(resolution.Errors);
        Assert.Contains("neither", error, StringComparison.Ordinal);
        Assert.Contains(SqlServerE2eMetadata.Enabled, error, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrongCaseMetadataKeyConfersNothing()
    {
        using var temp = new TempStore();
        var profile = Profile(Bootstrap);
        profile.SetMetadata("ITTIGER.E2E.ENABLED", SqlServerE2eMetadata.True);
        temp.Seed(profile);

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });

        AssertRefused(SqlServerE2eResolutionStatus.Invalid, resolution);
        Assert.Empty(resolution.CandidateNames);
    }

    [Fact]
    public void AnUnknownReservedKeyIsIgnoredRatherThanRejected()
    {
        using var temp = new TempStore();
        var profile = Enabled(Profile(Bootstrap));
        profile.SetReservedMetadata("ittiger.e2e.something-from-a-later-release", "whatever");
        temp.Seed(profile);

        // Forward compatibility: a store written by a newer TigerQuery must still resolve.
        Assert.Equal(
            SqlServerE2eResolutionStatus.Resolved,
            Resolve(temp, new SqlServerE2eConnectionResolutionOptions { ConnectionName = Bootstrap }).Status);
    }

    // ---- Database creation is a separate, explicit permission ----

    [Fact]
    public void DatabaseCreationRequiresItsOwnMetadataAndIsNeverImpliedByAuthorization()
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile(Bootstrap)));

        var readOnly = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });
        var creating = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap,
            RequireDatabaseCreationPermission = true
        });

        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, readOnly.Status);
        AssertRefused(SqlServerE2eResolutionStatus.Invalid, creating);
        Assert.Contains(
            creating.Errors,
            error => error.Contains(SqlServerE2eMetadata.AllowDatabaseCreation, StringComparison.Ordinal));
    }

    [Fact]
    public void AnExplicitDatabaseCreationPermissionIsHonored()
    {
        using var temp = new TempStore();
        var profile = Enabled(Profile(Bootstrap));
        profile.SetReservedMetadata(SqlServerE2eMetadata.AllowDatabaseCreation, SqlServerE2eMetadata.True);
        temp.Seed(profile);

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap,
            RequireDatabaseCreationPermission = true
        });

        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, resolution.Status);
    }

    [Fact]
    public void AnExplicitlyDeniedDatabaseCreationPermissionIsRefused()
    {
        using var temp = new TempStore();
        var profile = Enabled(Profile(Bootstrap));
        profile.SetReservedMetadata(SqlServerE2eMetadata.AllowDatabaseCreation, SqlServerE2eMetadata.False);
        temp.Seed(profile);

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap,
            RequireDatabaseCreationPermission = true
        });

        AssertRefused(SqlServerE2eResolutionStatus.Invalid, resolution);
    }

    [Fact]
    public void AMalformedCreationPermissionFailsEvenWhenTheCallerDoesNotNeedIt()
    {
        using var temp = new TempStore();
        var profile = Enabled(Profile(Bootstrap));
        profile.SetReservedMetadata(SqlServerE2eMetadata.AllowDatabaseCreation, "True");
        temp.Seed(profile);

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });

        // Left alone, the typo reads as an intentional denial to the next caller that does
        // need the permission. Reserved metadata is either well-formed or an error.
        AssertRefused(SqlServerE2eResolutionStatus.Invalid, resolution);
        Assert.Contains(
            resolution.Errors,
            error => error.Contains(SqlServerE2eMetadata.AllowDatabaseCreation, StringComparison.Ordinal));
    }

    // ---- Bootstrap identity comes from a name, never from authorization or order ----

    [Fact]
    public void NoNameMeansNoSelectionEvenWithExactlyOneAuthorizedProfile()
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile(Bootstrap)));

        var resolution = Resolve(temp, null);

        AssertRefused(SqlServerE2eResolutionStatus.NotConfigured, resolution);
        Assert.Equal([Bootstrap], resolution.CandidateNames);
        Assert.Null(resolution.RequestedName);
    }

    [Fact]
    public void NoNameWithSeveralAuthorizedProfilesIsAmbiguousAndNeverTakesTheFirst()
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile("first-e2e")), Enabled(Profile("second-e2e")));

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions());

        AssertRefused(SqlServerE2eResolutionStatus.Ambiguous, resolution);
        Assert.Equal(["first-e2e", "second-e2e"], resolution.CandidateNames);
    }

    [Fact]
    public void AnAuthorizedProfileUnderAnotherNameIsNotTheBootstrapProfile()
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile("someone-elses-e2e")));

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            DefaultConnectionName = Bootstrap
        });

        // Authorization says "this profile may be used for E2E work". It never says
        // "this profile is the bootstrap connection".
        AssertRefused(SqlServerE2eResolutionStatus.NotConfigured, resolution);
        Assert.Equal(Bootstrap, resolution.RequestedName);
        Assert.Equal(["someone-elses-e2e"], resolution.CandidateNames);
    }

    [Fact]
    public void DuplicateNamesAreAmbiguousRatherThanFirstWins()
    {
        using var temp = new TempStore();
        var first = Enabled(Profile(Bootstrap));
        first.Server = "sql-one";
        var second = Enabled(Profile(Bootstrap));
        second.Server = "sql-two";

        // Add refuses duplicates, but a hand-edited store or a Save can produce them.
        temp.Store.Save([first, second]);

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });

        AssertRefused(SqlServerE2eResolutionStatus.Ambiguous, resolution);
        Assert.Equal([Bootstrap], resolution.CandidateNames);
    }

    // ---- Missing configuration is a safe, distinguishable state ----

    [Fact]
    public void AnAbsentStoreFileIsNotConfiguredAndCreatesNothing()
    {
        using var temp = new TempStore();
        Assert.False(File.Exists(temp.FilePath));

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            DefaultConnectionName = Bootstrap
        });

        AssertRefused(SqlServerE2eResolutionStatus.NotConfigured, resolution);
        Assert.False(File.Exists(temp.FilePath));
    }

    [Fact]
    public void AConfiguredDefaultNameThatDoesNotExistYetIsNotConfigured()
    {
        using var temp = new TempStore();
        temp.Seed(Profile("dev"), Profile("prod"));

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            DefaultConnectionName = Bootstrap
        });

        // The host named a convention; the developer has simply not set it up. That is a
        // skip, not a fault.
        AssertRefused(SqlServerE2eResolutionStatus.NotConfigured, resolution);
    }

    [Fact]
    public void AnExplicitNameThatDoesNotExistIsInvalid()
    {
        using var temp = new TempStore();
        temp.Seed(Profile("dev"));

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });

        // The caller asserted this profile exists, and it does not. Reporting it as a
        // skip would let a whole E2E suite pass by silently doing nothing.
        AssertRefused(SqlServerE2eResolutionStatus.Invalid, resolution);
        Assert.Equal(Bootstrap, resolution.RequestedName);
    }

    [Fact]
    public void AnUnreadableStoreIsInvalidRatherThanEmpty()
    {
        using var temp = new TempStore();
        File.WriteAllText(temp.FilePath, "{ this is not a connection array");

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            DefaultConnectionName = Bootstrap
        });

        AssertRefused(SqlServerE2eResolutionStatus.Invalid, resolution);
        Assert.Contains(
            resolution.Errors,
            error => error.Contains(temp.FilePath, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void APresentButBlankNameIsAnErrorRatherThanAnAbsence(string blank)
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile(Bootstrap)));

        var explicitly = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = blank,
            DefaultConnectionName = Bootstrap
        });
        var byDefault = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            DefaultConnectionName = blank
        });

        // Falling through from a name someone supplied to one they did not is exactly the
        // quiet substitution this contract exists to prevent.
        AssertRefused(SqlServerE2eResolutionStatus.Invalid, explicitly);
        AssertRefused(SqlServerE2eResolutionStatus.Invalid, byDefault);
    }

    // ---- Structural validation still applies ----

    [Fact]
    public void AnAuthorizedButStructurallyInvalidProfileIsRefused()
    {
        using var temp = new TempStore();
        var profile = Enabled(Profile(Bootstrap));
        profile.Server = string.Empty;
        temp.Seed(profile);

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });

        AssertRefused(SqlServerE2eResolutionStatus.Invalid, resolution);
        Assert.Contains(resolution.Errors, error => error.Contains("Server", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCallerChoosesWhetherAnInitialDatabaseIsRequired()
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile(Bootstrap)));

        var serverLevel = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap
        });
        var databaseRequired = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap,
            ValidationPolicy = SqlServerConnectionValidationPolicy.DatabaseRequired
        });

        // A bootstrap profile normally names a server, because the databases it will work
        // with do not exist yet.
        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, serverLevel.Status);
        AssertRefused(SqlServerE2eResolutionStatus.Invalid, databaseRequired);
    }

    [Fact]
    public void EveryRefusedOutcomeReportsAtLeastOneReasonAndNoProfile()
    {
        using var temp = new TempStore();
        temp.Seed(Profile("ordinary"), Enabled(Profile("first-e2e")), Enabled(Profile("second-e2e")));

        SqlServerE2eConnectionResolution[] refusals =
        [
            Resolve(temp, new SqlServerE2eConnectionResolutionOptions()),
            Resolve(temp, new SqlServerE2eConnectionResolutionOptions { ConnectionName = "ordinary" }),
            Resolve(temp, new SqlServerE2eConnectionResolutionOptions { ConnectionName = "absent" }),
            Resolve(temp, new SqlServerE2eConnectionResolutionOptions { DefaultConnectionName = "absent" })
        ];

        Assert.All(refusals, resolution =>
        {
            Assert.NotEqual(SqlServerE2eResolutionStatus.Resolved, resolution.Status);
            Assert.Null(resolution.Profile);
            Assert.NotEmpty(resolution.Errors);
            Assert.All(resolution.Errors, error => Assert.False(string.IsNullOrWhiteSpace(error)));
        });
    }

    [Fact]
    public void ANullStoreIsACallerBugRatherThanAnOutcome() =>
        Assert.Throws<ArgumentNullException>(() => SqlServerE2eConnectionResolver.Resolve(null!));

    // ---- Diagnostics carry no secrets ----

    [Fact]
    public void DiagnosticsNeverEchoCredentialMaterial()
    {
        const string Secret = "correct-horse-battery-staple";
        using var temp = new TempStore(new EchoingPasswordProtector());

        var profile = Profile(Bootstrap);
        profile.Authentication = AuthenticationType.SqlPassword;
        profile.Username = "sa";
        profile.PlainPassword = Secret;

        // The password value in a reserved metadata slot is contrived, but it is the one
        // path by which caller-controlled text reaches a diagnostic, so it is the path the
        // redaction has to survive.
        profile.SetReservedMetadata(SqlServerE2eMetadata.Enabled, Secret);
        temp.Seed(profile);

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap,
            RequireDatabaseCreationPermission = true
        });

        AssertRefused(SqlServerE2eResolutionStatus.Invalid, resolution);
        Assert.All(
            resolution.Errors,
            error => Assert.DoesNotContain(Secret, error, StringComparison.Ordinal));
        Assert.All(
            resolution.CandidateNames,
            name => Assert.DoesNotContain(Secret, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Round-trips the password in the clear so a loaded profile carries both the plain and
    /// the persisted form, which is what the redaction guard has to cover.
    /// </summary>
    private sealed class EchoingPasswordProtector : IConnectionPasswordProtector
    {
        public void ProtectForSave(SqlServerConnectionProfile profile)
        {
            if (!string.IsNullOrEmpty(profile.PlainPassword))
                profile.EncryptedPassword = profile.PlainPassword;
        }

        public void UnprotectAfterLoad(SqlServerConnectionProfile profile) =>
            profile.PlainPassword = profile.EncryptedPassword;
    }

    // ---- Resolution never touches SQL Server ----

    [Fact]
    public void ResolvingConstructsNoSqlConnection()
    {
        using var temp = new TempStore();
        temp.Seed(Enabled(Profile(Bootstrap)));

        // The listener is process-wide and other collections run in parallel, so events are
        // correlated by this thread's activity id. Resolution is synchronous, so anything it
        // did would be tagged with it.
        var activity = Guid.NewGuid();
        using var sqlClientEvents = new SqlClientEventProbe(activity);
        EventSource.SetCurrentThreadActivityId(activity, out var previous);
        try
        {
            // A positive control, so "no events" cannot pass by the probe being deaf.
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(
                "Data Source=sql01;Connect Timeout=1"))
            {
                _ = connection.ConnectionString;
            }

            Assert.True(sqlClientEvents.Count > 0, "The probe never observed SqlClient at all.");
            var beforeResolution = sqlClientEvents.Count;

            var resolution = SqlServerE2eConnectionResolver.Resolve(
                temp.Store,
                new SqlServerE2eConnectionResolutionOptions { ConnectionName = Bootstrap });

            Assert.Equal(SqlServerE2eResolutionStatus.Resolved, resolution.Status);
            Assert.Equal(beforeResolution, sqlClientEvents.Count);
        }
        finally
        {
            EventSource.SetCurrentThreadActivityId(previous);
        }
    }

    [Fact]
    public void ResolvingNeverContactsTheServerNamedByTheProfile()
    {
        using var listener = new AcceptCounter();
        using var temp = new TempStore();

        var profile = Enabled(Profile(Bootstrap));
        profile.Server = listener.Endpoint;
        profile.SetReservedMetadata(SqlServerE2eMetadata.AllowDatabaseCreation, SqlServerE2eMetadata.True);
        temp.Seed(profile);

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            ConnectionName = Bootstrap,
            RequireDatabaseCreationPermission = true
        });

        Assert.Equal(SqlServerE2eResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(0, listener.Accepted);

        // And the socket really would have noticed.
        listener.ConnectOnce();
        Assert.Equal(1, listener.Accepted);
    }

    [Fact]
    public void AnUnconfiguredRunTouchesNothingReachable()
    {
        using var listener = new AcceptCounter();
        using var temp = new TempStore();

        // Profiles pointing at the classic discovery targets, none of them authorized.
        foreach (var server in new[] { ".", "(local)", "localhost", @"(localdb)\MSSQLLocalDB", listener.Endpoint })
        {
            var profile = Profile($"dev-{server.GetHashCode(StringComparison.Ordinal)}");
            profile.Server = server;
            temp.Store.Add(profile);
        }

        var resolution = Resolve(temp, new SqlServerE2eConnectionResolutionOptions
        {
            DefaultConnectionName = Bootstrap
        });

        AssertRefused(SqlServerE2eResolutionStatus.NotConfigured, resolution);
        Assert.Empty(resolution.CandidateNames);
        Assert.Equal(0, listener.Accepted);
    }

    // ---- Helpers ----

    private static SqlServerE2eConnectionResolution Resolve(
        TempStore temp,
        SqlServerE2eConnectionResolutionOptions? options) =>
        SqlServerE2eConnectionResolver.Resolve(temp.Store, options);

    private static void AssertRefused(
        SqlServerE2eResolutionStatus expected,
        SqlServerE2eConnectionResolution resolution)
    {
        Assert.Equal(expected, resolution.Status);
        Assert.Null(resolution.Profile);
        Assert.NotEmpty(resolution.Errors);
    }

    private static SqlServerConnectionProfile Profile(string name) => new()
    {
        Name = name,
        Server = "sql01",
        Authentication = AuthenticationType.Integrated,
        Encrypt = EncryptOption.Mandatory
    };

    private static SqlServerConnectionProfile Enabled(SqlServerConnectionProfile profile)
    {
        profile.SetReservedMetadata(SqlServerE2eMetadata.Enabled, SqlServerE2eMetadata.True);
        return profile;
    }

    private sealed class TempStore : IDisposable
    {
        private readonly string directory;

        public TempStore(IConnectionPasswordProtector? passwordProtector = null)
        {
            directory = Path.Combine(
                Path.GetTempPath(), "TigerQueryE2eResolverTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, "connections.json");
            Store = new SqlServerConnectionStore(
                new SqlServerConnectionStoreOptions { FilePath = FilePath },
                passwordProtector ?? new NoOpConnectionPasswordProtector());
        }

        public string FilePath { get; }

        public SqlServerConnectionStore Store { get; }

        public void Seed(params SqlServerConnectionProfile[] profiles)
        {
            foreach (var profile in profiles)
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
            }
        }
    }

    /// <summary>
    /// A loopback socket standing in for a SQL Server that is reachable. Nothing may
    /// connect to it during resolution, however reachable it is.
    /// </summary>
    private sealed class AcceptCounter : IDisposable
    {
        private readonly TcpListener listener;
        private int accepted;

        public AcceptCounter()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Endpoint = $"127.0.0.1,{((IPEndPoint)listener.LocalEndpoint).Port}";
            _ = AcceptLoopAsync();
        }

        public string Endpoint { get; }

        public int Accepted => Volatile.Read(ref accepted);

        public void ConnectOnce()
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Accepted == 0 && DateTime.UtcNow < deadline)
                Thread.Sleep(10);
        }

        public void Dispose() => listener.Stop();

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    Interlocked.Increment(ref accepted);
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or InvalidOperationException)
            {
                // The listener was stopped; that is how this loop ends.
            }
        }
    }

    /// <summary>
    /// Counts SqlClient activity raised on one thread. Constructing a
    /// <see cref="Microsoft.Data.SqlClient.SqlConnection"/> is enough to register here, so
    /// a zero count covers more than "no connection was opened".
    /// </summary>
    private sealed class SqlClientEventProbe(Guid activity) : EventListener
    {
        private const string SourceName = "Microsoft.Data.SqlClient.EventSource";

        private int count;

        public int Count => Volatile.Read(ref count);

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == SourceName)
                EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name == SourceName && eventData.ActivityId == activity)
                Interlocked.Increment(ref count);
        }
    }
}
