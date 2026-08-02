using System.Globalization;
using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore.Resources;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Contributes TigerQuery's <c>--tq-connection-store-file</c> global option, and help
/// metadata for the connection-store environment variable, to a TigerCli application.
/// </summary>
/// <remarks>
/// <para>
/// Register one instance with <c>TigerCliAppBuilder.AddContribution(...)</c> and give its
/// <see cref="Options"/> to <see cref="SqlServerConnectionCommandOptions.TigerQuery"/> and
/// to every host command or service that reads connections. Registering the contribution
/// and mounting the <c>connections</c> command group are separate opt-ins: an app can
/// accept the option for its own commands without the group, or mount the group without
/// the option.
/// </para>
/// <para>
/// TigerCli owns the mechanics — name validation, parsing, rejecting a repeated occurrence
/// or a missing value, help placement, and invoking the callback exactly once per command
/// run with null when the option is absent. This type owns only what the option means: it
/// hands the value to <see cref="SqlServerConnectionStorePathResolver"/> and records the
/// result on <see cref="Options"/>. TigerCli never reads the environment variable; the
/// registration below is help text, and the lookup belongs to the resolver.
/// </para>
/// <para>
/// The option follows TigerCli's grammar despite being app-wide in meaning: write it after
/// the command path and any positional arguments, and use
/// <c>--tq-connection-store-file=&lt;path&gt;</c> when the path begins with <c>-</c>.
/// </para>
/// </remarks>
public sealed class TigerQueryCliContribution : ITigerCliAppContribution
{
    /// <summary>The canonical long name of the contributed store-path option.</summary>
    public const string ConnectionStoreFileOption = "--tq-connection-store-file";

    /// <summary>Initializes the contribution over host-owned, run-shared state.</summary>
    /// <param name="options">
    /// The state this contribution fills in and the rest of the application reads.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public TigerQueryCliContribution(TigerQueryCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
    }

    /// <summary>Gets the state the callback writes to and the application reads from.</summary>
    public TigerQueryCliOptions Options { get; }

    /// <summary>Registers the store-path option and the environment-variable help entry.</summary>
    /// <param name="builder">The contribution-scoped app configuration builder.</param>
    /// <remarks>
    /// TigerCli calls this during <c>Build()</c>, which is where a duplicate option name or
    /// a duplicate environment-variable name fails. A host that already registered
    /// <see cref="SqlServerConnectionStoreEnvironment.ConnectionStoreFile"/> itself must
    /// drop that registration when it adopts this contribution.
    /// <para>
    /// The option and variable descriptions are literal English: TigerCli 0.9.1 accepts no
    /// resource key here, and <c>Build()</c> runs before <c>--culture</c> is resolved. The
    /// callback's validation messages are localized, because the callback does receive the
    /// run's culture.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> is <see langword="null"/>.
    /// </exception>
    public void Configure(TigerCliAppContributionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.GlobalOptions.AddOptionalString(
            name: ConnectionStoreFileOption,
            valueName: "path",
            description: "Use a specific TigerQuery connection-store file for this run.",
            apply: Apply);

        builder.AddEnvironmentVariable(
            SqlServerConnectionStoreEnvironment.ConnectionStoreFile,
            $"Selects the TigerQuery connection-store file when {ConnectionStoreFileOption} "
            + "is not supplied.");
    }

    /// <summary>
    /// Resolves the run's store path from the supplied value, the environment, and the host
    /// default, and records it on <see cref="Options"/>.
    /// </summary>
    /// <remarks>
    /// Resolution happens here rather than at first store access because it is inert —
    /// string work and one environment read, no file system — and because a misconfigured
    /// path is worth reporting as a clean validation error before binding rather than from
    /// inside a handler. The cost is that a bad
    /// <see cref="SqlServerConnectionStoreEnvironment.ConnectionStoreFile"/> also fails
    /// commands that never touch the store, which is the intended fail-fast behavior. The
    /// store itself is still built lazily, on first access to
    /// <see cref="TigerQueryCliOptions.Store"/>.
    /// </remarks>
    private TigerCliValidationResult Apply(TigerCliGlobalOptionContext context, string? value)
    {
        var resolution = SqlServerConnectionStorePathResolver.Resolve(
            new SqlServerConnectionStorePathOptions
            {
                ExplicitFilePath = value,
                DefaultFilePath = Options.DefaultConnectionStoreFile,
                EnvironmentReader = Options.EnvironmentReader
            });

        Options.ApplyResolution(value, resolution);

        return resolution.IsSuccess
            ? TigerCliValidationResult.Success()
            : TigerCliValidationResult.Error(Describe(resolution, context.Culture));
    }

    /// <summary>
    /// Renders a failed resolution in the run's culture, naming the source that supplied
    /// the unusable value so the user knows where to look.
    /// </summary>
    private static string Describe(
        SqlServerConnectionStorePathResolution resolution,
        CultureInfo culture)
    {
        var subject = resolution.Source switch
        {
            SqlServerConnectionStorePathSource.Explicit => string.Format(
                culture,
                Localize("The {0} option", culture),
                ConnectionStoreFileOption),
            SqlServerConnectionStorePathSource.EnvironmentVariable => string.Format(
                culture,
                Localize("The {0} environment variable", culture),
                SqlServerConnectionStoreEnvironment.ConnectionStoreFile),
            _ => Localize("The application default connection-store file path", culture)
        };

        var template = resolution.Error switch
        {
            SqlServerConnectionStorePathError.Blank =>
                "{0} is present but empty. TigerQuery does not fall back to another connection store.",
            SqlServerConnectionStorePathError.Malformed =>
                "{0} is not a valid file path: '{1}'. TigerQuery does not fall back to another connection store.",
            _ =>
                "{0} names a directory, not a connection-store file: '{1}'. TigerQuery does not fall back to another connection store."
        };

        return string.Format(
            culture, Localize(template, culture), subject, resolution.AttemptedValue);
    }

    /// <summary>
    /// Looks up a source-text key in the connection-command resources, falling back to the
    /// key itself — which is the English text — when no translation exists.
    /// </summary>
    private static string Localize(string sourceText, CultureInfo culture) =>
        SqlServerConnectionCommandStrings.ResourceManager.GetString(sourceText, culture)
        ?? sourceText;
}
