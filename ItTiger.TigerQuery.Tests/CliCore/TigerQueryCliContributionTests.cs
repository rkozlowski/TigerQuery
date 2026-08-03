using System.Reflection;
using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;
using ItTiger.TigerQuery.Tests.Cli;

namespace ItTiger.TigerQuery.Tests.CliCore;

/// <summary>
/// Covers the TigerCli app contribution that carries <c>--tq-connection-store-file</c>:
/// what reaches the shared state, when the callback runs, how TigerCli's grammar applies
/// to the option, and which mistakes fail at <c>Build()</c> rather than at run time.
/// </summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class TigerQueryCliContributionTests : IDisposable
{
    private readonly TempStorePath _default = new("default.json");
    private readonly TempStorePath _explicitPath = new("explicit.json");
    private readonly TempStorePath _environment = new("environment.json");

    public void Dispose()
    {
        _default.Dispose();
        _explicitPath.Dispose();
        _environment.Dispose();
    }

    private ContributionTestApp Create(Func<string, string?>? environmentReader = null) =>
        ContributionTestApp.Create(_default.FilePath, environmentReader);

    // ---- The value reaches the shared state, and precedence is Core's ----

    [Fact]
    public async Task TheOptionValueReachesTheContributionStateAndSelectsTheStore()
    {
        var app = Create();

        var (exit, _, stdErr) = await app.RunAsync(
            "probe", "--non-interactive", "--tq-connection-store-file", _explicitPath.FilePath);

        Assert.Equal((int)ContributionExitCode.Ok, exit);
        Assert.Empty(stdErr);
        Assert.Equal(_explicitPath.FilePath, app.Options.ExplicitConnectionStoreFile);
        Assert.Equal(
            SqlServerConnectionStorePathSource.Explicit, app.Options.ResolvedStorePath!.Source);
        Assert.Equal([_explicitPath.FilePath], app.ProbedStorePaths);
    }

    [Fact]
    public async Task TheCallbackRunsWithNullAndTheHostDefaultWinsWhenNoOverrideExists()
    {
        var app = Create();

        var (exit, _, _) = await app.RunAsync("probe", "--non-interactive");

        Assert.Equal((int)ContributionExitCode.Ok, exit);
        Assert.Null(app.Options.ExplicitConnectionStoreFile);
        Assert.Equal(
            SqlServerConnectionStorePathSource.ApplicationDefault,
            app.Options.ResolvedStorePath!.Source);
        Assert.Equal([_default.FilePath], app.ProbedStorePaths);
    }

    [Fact]
    public async Task TheEnvironmentVariableWinsOverTheHostDefault()
    {
        var app = Create(EnvironmentWith(_environment.FilePath));

        var (exit, _, _) = await app.RunAsync("probe", "--non-interactive");

        Assert.Equal((int)ContributionExitCode.Ok, exit);
        Assert.Equal(
            SqlServerConnectionStorePathSource.EnvironmentVariable,
            app.Options.ResolvedStorePath!.Source);
        Assert.Equal([_environment.FilePath], app.ProbedStorePaths);
    }

    [Fact]
    public async Task TheCliOptionWinsOverTheEnvironmentVariable()
    {
        var app = Create(EnvironmentWith(_environment.FilePath));

        var (exit, _, _) = await app.RunAsync(
            "probe", "--non-interactive", "--tq-connection-store-file", _explicitPath.FilePath);

        Assert.Equal((int)ContributionExitCode.Ok, exit);
        Assert.Equal([_explicitPath.FilePath], app.ProbedStorePaths);
    }

    [Fact]
    public async Task ARelativeOptionValueIsReportedAsTheAbsolutePathItResolvedTo()
    {
        var app = Create();

        var (exit, _, _) = await app.RunAsync(
            "probe", "--non-interactive", "--tq-connection-store-file", "relative-store.json");

        Assert.Equal((int)ContributionExitCode.Ok, exit);
        Assert.Equal(
            Path.GetFullPath("relative-store.json"), app.Options.ResolvedStorePath!.FilePath);
        Assert.False(File.Exists(Path.GetFullPath("relative-store.json")));
    }

    // ---- A bad value stops the run cleanly, before binding ----

    [Fact]
    public async Task AnInvalidOptionValueEndsTheRunWithTheValidationExitKind()
    {
        var app = Create();

        var (exit, _, stdErr) = await app.RunAsync(
            "probe", "--non-interactive", "--tq-connection-store-file", "   ");

        Assert.Equal((int)ContributionExitCode.Validation, exit);
        Assert.Contains(TigerQueryCliContribution.ConnectionStoreFileOption, stdErr);
        Assert.Contains("does not fall back", stdErr);
        Assert.Empty(app.ProbedStorePaths);
    }

    [Fact]
    public async Task AnInvalidEnvironmentValueNamesTheVariableAndFailsEvenTheStorelessCommand()
    {
        var app = Create(EnvironmentWith("   "));

        var (exit, _, stdErr) = await app.RunAsync("probe", "--non-interactive");

        Assert.Equal((int)ContributionExitCode.Validation, exit);
        Assert.Contains(SqlServerConnectionStoreEnvironment.ConnectionStoreFile, stdErr);
        Assert.Empty(app.ProbedStorePaths);
    }

    [Fact]
    public async Task AValueNamingADirectoryIsRejectedWithoutTouchingTheFileSystem()
    {
        var app = Create();
        var directory = _explicitPath.DirectoryPath + Path.DirectorySeparatorChar;

        var (exit, _, stdErr) = await app.RunAsync(
            "probe", "--non-interactive", "--tq-connection-store-file", directory);

        Assert.Equal((int)ContributionExitCode.Validation, exit);
        Assert.Contains("names a directory", stdErr);
        Assert.False(Directory.Exists(_explicitPath.DirectoryPath));
    }

    [Fact]
    public async Task TheCallbackRunsBeforeCommandBindingSoItsErrorWinsOverAMissingArgument()
    {
        var app = Create();

        // `connections show` needs a name; supplying none is a binding-time failure. The
        // store error is reported instead, which is only possible if the callback ran first.
        var (exit, _, stdErr) = await app.RunAsync(
            "connections", "show", "--non-interactive", "--tq-connection-store-file", "   ");

        Assert.Equal((int)ContributionExitCode.Validation, exit);
        Assert.Contains(TigerQueryCliContribution.ConnectionStoreFileOption, stdErr);
    }

    [Fact]
    public async Task ValidationMessagesAreLocalizedThroughTheRunCulture()
    {
        var app = Create();

        var (exit, _, stdErr) = await app.RunAsync(
            "probe", "--non-interactive", "--culture", "pl-PL", "--tq-connection-store-file", "   ");

        Assert.Equal((int)ContributionExitCode.Validation, exit);
        Assert.Contains("Opcja", stdErr);
        Assert.Contains("nie przechodzi awaryjnie", stdErr);
    }

    // ---- TigerCli's grammar and argument rules apply unchanged ----

    [Fact]
    public async Task ARepeatedOccurrenceIsAnArgumentErrorRatherThanLastValueWins()
    {
        var app = Create();

        var (exit, _, _) = await app.RunAsync(
            "probe",
            "--non-interactive",
            "--tq-connection-store-file", _environment.FilePath,
            "--tq-connection-store-file", _explicitPath.FilePath);

        Assert.Equal((int)ContributionExitCode.Usage, exit);
        Assert.Empty(app.ProbedStorePaths);
    }

    [Fact]
    public async Task AMissingValueIsAnArgumentError()
    {
        var app = Create();

        var (exit, _, _) = await app.RunAsync(
            "probe", "--non-interactive", "--tq-connection-store-file");

        Assert.Equal((int)ContributionExitCode.Usage, exit);
        Assert.Empty(app.ProbedStorePaths);
    }

    [Fact]
    public async Task TheOptionMustFollowTheCommandPath()
    {
        var app = Create();

        var (exit, _, _) = await app.RunAsync(
            "--tq-connection-store-file", _explicitPath.FilePath, "probe", "--non-interactive");

        Assert.NotEqual((int)ContributionExitCode.Ok, exit);
        Assert.Empty(app.ProbedStorePaths);
    }

    [Fact]
    public async Task TheEqualsFormCarriesAValueThatBeginsWithADash()
    {
        var app = Create();

        var (exit, _, _) = await app.RunAsync(
            "probe", "--non-interactive", "--tq-connection-store-file=-dashed.json");

        Assert.Equal((int)ContributionExitCode.Ok, exit);
        Assert.Equal("-dashed.json", app.Options.ExplicitConnectionStoreFile);
        Assert.Equal([Path.GetFullPath("-dashed.json")], app.ProbedStorePaths);
        Assert.False(File.Exists(Path.GetFullPath("-dashed.json")));
    }

    // ---- Help shows the option and the variable, and invokes nothing ----

    [Fact]
    public async Task RootHelpAdvertisesTheOptionWithoutInvokingTheCallback()
    {
        var app = Create();

        var (exit, stdOut, _) = await app.RunAsync("--help");

        Assert.Equal((int)ContributionExitCode.Ok, exit);
        Assert.Contains(TigerQueryCliContribution.ConnectionStoreFileOption, stdOut);
        Assert.Null(app.Options.ResolvedStorePath);
        Assert.Null(app.Options.ExplicitConnectionStoreFile);
    }

    [Fact]
    public async Task CommandHelpAdvertisesTheOptionWithoutInvokingTheCallback()
    {
        var app = Create();

        var (exit, stdOut, _) = await app.RunAsync("connections", "list", "--help");

        Assert.Equal((int)ContributionExitCode.Ok, exit);
        Assert.Contains(TigerQueryCliContribution.ConnectionStoreFileOption, stdOut);
        Assert.Null(app.Options.ResolvedStorePath);
    }

    [Fact]
    public async Task EnvironmentHelpAdvertisesTheTigerQueryVariable()
    {
        var app = Create();

        var (exit, stdOut, _) = await app.RunAsync("--help-env");

        Assert.Equal((int)ContributionExitCode.Ok, exit);
        Assert.Contains(SqlServerConnectionStoreEnvironment.ConnectionStoreFile, stdOut);
        Assert.Null(app.Options.ResolvedStorePath);
    }

    [Fact]
    public async Task ContributedOptionAndEnvironmentDescriptionsUseTheRunCulture()
    {
        var app = Create();

        var optionHelp = await app.RunAsync("--culture", "pl-PL", "--help");
        var environmentHelp = await app.RunAsync("--culture", "pl-PL", "--help-env");

        Assert.Equal((int)ContributionExitCode.Ok, optionHelp.ExitCode);
        Assert.Contains("Użyj określonego pliku magazynu połączeń", optionHelp.StdOut);
        Assert.Equal((int)ContributionExitCode.Ok, environmentHelp.ExitCode);
        Assert.Contains("Wybiera plik magazynu połączeń", environmentHelp.StdOut);
    }

    // ---- The option is contributed, not a command setting ----

    [Fact]
    public void NoConnectionSettingsTypeDeclaresTheContributedOption()
    {
        var declared = typeof(SqlServerConnectionCommands).Assembly
            .GetTypes()
            .Where(type => typeof(TigerCliSettings).IsAssignableFrom(type))
            .SelectMany(type => type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(property => property.GetCustomAttributes<TigerCliOptionAttribute>())
            .SelectMany(attribute => attribute.Aliases)
            .ToList();

        Assert.NotEmpty(declared);
        Assert.DoesNotContain(
            TigerQueryCliContribution.ConnectionStoreFileOption,
            declared,
            StringComparer.OrdinalIgnoreCase);
    }

    // ---- Wiring mistakes fail at Build(), not mid-run ----

    [Fact]
    public void RegisteringTheContributionTwiceFailsAtBuild()
    {
        var options = new TigerQueryCliOptions { DefaultConnectionStoreFile = _default.FilePath };

        var builder = ContributionTestApp
            .Builder(new TigerQueryCliContribution(options))
            .AddContribution(new TigerQueryCliContribution(options));

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void AHostRegisteringTheSameEnvironmentVariableFailsAtBuild()
    {
        var options = new TigerQueryCliOptions { DefaultConnectionStoreFile = _default.FilePath };

        var builder = ContributionTestApp
            .Builder(new TigerQueryCliContribution(options))
            .AddEnvironmentVariable(
                SqlServerConnectionStoreEnvironment.ConnectionStoreFile,
                "A host registration that now collides with the contribution.");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void TheContributionRequiresItsOptions() =>
        Assert.Throws<ArgumentNullException>(() => new TigerQueryCliContribution(null!));

    private static Func<string, string?> EnvironmentWith(string value) =>
        name => name == SqlServerConnectionStoreEnvironment.ConnectionStoreFile ? value : null;
}
