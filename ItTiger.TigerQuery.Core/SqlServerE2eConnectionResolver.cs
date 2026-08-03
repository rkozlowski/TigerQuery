using System.Text.Json;

namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Resolves the TigerQuery E2E bootstrap connection profile from a connection store,
/// using only store contents and reserved metadata.
/// </summary>
/// <remarks>
/// <para>
/// This is the authorization boundary for E2E infrastructure, and it is deliberately dull.
/// It reads one store, looks up one name, checks the reserved metadata in
/// <see cref="SqlServerE2eMetadata"/>, and returns. It does not search for SQL Server
/// instances; it does not try <c>.</c>, <c>(local)</c>, <c>localhost</c>, LocalDB, named
/// instances, ports, services, or containers; it does not open a
/// <see cref="Microsoft.Data.SqlClient.SqlConnection"/>, test credentials, check
/// reachability, or inspect permissions on a server. A profile is eligible because it says
/// so, never because it answers.
/// </para>
/// <para>
/// Selection is always by name — <see cref="SqlServerE2eConnectionResolutionOptions.ConnectionName"/>,
/// then <see cref="SqlServerE2eConnectionResolutionOptions.DefaultConnectionName"/> — and
/// the selected profile must carry explicit bootstrap authorization metadata. With no
/// name, a store holding exactly one authorized bootstrap profile still
/// resolves nothing: implicit single-profile selection can be added later without breaking
/// a caller, while removing it later would be a silent safety regression. Ambiguity is
/// reported, never settled by taking the first candidate.
/// </para>
/// <para>
/// Every failure is a value, not an exception, so a test suite can branch on
/// <see cref="SqlServerE2eResolutionStatus.NotConfigured"/> and skip. The one thing that
/// does throw is a null store, which is a caller bug rather than a configuration state.
/// </para>
/// </remarks>
public static class SqlServerE2eConnectionResolver
{
    /// <summary>Resolves the E2E bootstrap profile, or explains why it could not.</summary>
    /// <param name="store">The connection store to read. Nothing else is consulted.</param>
    /// <param name="options">
    /// The name to select by and the permissions to require; treated as an empty
    /// options object when null, which resolves nothing because no name is available.
    /// </param>
    /// <returns>
    /// A resolution carrying the authorized profile, or a status and diagnostics
    /// explaining the refusal. It never carries a profile unless
    /// <see cref="SqlServerE2eConnectionResolution.Status"/> is
    /// <see cref="SqlServerE2eResolutionStatus.Resolved"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A store file that does not exist reads as an empty store and produces
    /// <see cref="SqlServerE2eResolutionStatus.NotConfigured"/>, which is what makes a
    /// fresh clone inert. A store file that exists but cannot be read or parsed produces
    /// <see cref="SqlServerE2eResolutionStatus.Invalid"/> rather than being mistaken for an
    /// empty one.
    /// </para>
    /// <para>
    /// The only I/O is loading the store. No connection is opened, no server is contacted,
    /// and nothing is written — resolving is safe to call on every test run, including runs
    /// that will skip.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/> is <see langword="null"/>.
    /// </exception>
    public static SqlServerE2eConnectionResolution Resolve(
        SqlServerConnectionStore store,
        SqlServerE2eConnectionResolutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        options ??= new SqlServerE2eConnectionResolutionOptions();

        IReadOnlyList<SqlServerConnectionProfile> profiles;
        try
        {
            profiles = store.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable store is not an empty store. Reporting it as NotConfigured
            // would turn "your store is corrupt" into a silent skip.
            return Failed(
                SqlServerE2eResolutionStatus.Invalid,
                requestedName: null,
                candidateNames: [],
                [$"The connection store '{store.FilePath}' could not be read: {ex.Message}"]);
        }

        var candidateNames = profiles
            .Where(profile => SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Enabled)
                == SqlServerE2eFlagState.True
                && SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Bootstrap)
                == SqlServerE2eFlagState.True)
            .Select(profile => profile.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!TrySelectName(options, out var requestedName, out var isHostDefault, out var nameError))
            return Failed(SqlServerE2eResolutionStatus.Invalid, null, candidateNames, [nameError!]);

        if (requestedName is null)
            return NoNameSupplied(candidateNames);

        var matches = profiles.Where(profile => profile.Name == requestedName).ToList();

        if (matches.Count == 0)
        {
            // A caller who names a profile made a claim that turned out false; a host
            // convention name that nobody has created yet is simply not set up.
            return isHostDefault
                ? Failed(
                    SqlServerE2eResolutionStatus.NotConfigured,
                    requestedName,
                    candidateNames,
                    [
                        $"The host's default E2E bootstrap connection '{requestedName}' does not exist "
                        + $"in the connection store '{store.FilePath}'."
                    ])
                : Failed(
                    SqlServerE2eResolutionStatus.Invalid,
                    requestedName,
                    candidateNames,
                    [
                        $"The E2E bootstrap connection '{requestedName}' was not found in the "
                        + $"connection store '{store.FilePath}'."
                    ]);
        }

        if (matches.Count > 1)
        {
            // Profile names are unique through Add, but a hand-edited or Save-written store
            // can hold duplicates. Find would return the first; this must not.
            return Failed(
                SqlServerE2eResolutionStatus.Ambiguous,
                requestedName,
                candidateNames,
                [
                    $"The connection store '{store.FilePath}' holds {matches.Count} profiles named "
                    + $"'{requestedName}'. TigerQuery does not choose between them; remove the "
                    + "duplicates so one profile carries the name."
                ]);
        }

        var candidate = matches[0];
        var errors = Authorize(candidate, options);

        return errors.Count == 0
            ? new SqlServerE2eConnectionResolution
            {
                Status = SqlServerE2eResolutionStatus.Resolved,
                Profile = candidate,
                RequestedName = requestedName
            }
            : Failed(SqlServerE2eResolutionStatus.Invalid, requestedName, candidateNames, errors, candidate);
    }

    /// <summary>
    /// Picks the name to select by, keeping the caller's name and the host's convention
    /// distinguishable because they fail differently.
    /// </summary>
    /// <remarks>
    /// A present-but-blank name is rejected rather than skipped, for the same reason a
    /// present-but-empty store-path environment variable is: falling through from a value
    /// someone supplied to one they did not is exactly the quiet substitution this contract
    /// exists to prevent.
    /// </remarks>
    private static bool TrySelectName(
        SqlServerE2eConnectionResolutionOptions options,
        out string? name,
        out bool isHostDefault,
        out string? error)
    {
        name = null;
        isHostDefault = false;
        error = null;

        if (options.ConnectionName is { } requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                error = "The requested E2E bootstrap connection name is present but blank.";
                return false;
            }

            name = requested;
            return true;
        }

        if (options.DefaultConnectionName is { } hostDefault)
        {
            if (string.IsNullOrWhiteSpace(hostDefault))
            {
                error = "The host's default E2E bootstrap connection name is present but blank.";
                return false;
            }

            name = hostDefault;
            isHostDefault = true;
            return true;
        }

        return true;
    }

    /// <summary>
    /// Answers the "no name at all" case without ever selecting a profile, whatever the
    /// store happens to contain.
    /// </summary>
    private static SqlServerE2eConnectionResolution NoNameSupplied(IReadOnlyList<string> candidateNames)
    {
        if (candidateNames.Count > 1)
        {
            return Failed(
                SqlServerE2eResolutionStatus.Ambiguous,
                requestedName: null,
                candidateNames,
                [
                    $"No E2E bootstrap connection name was supplied and {candidateNames.Count} profiles "
                    + $"are marked {SqlServerE2eMetadata.Enabled}={SqlServerE2eMetadata.True} and "
                    + $"{SqlServerE2eMetadata.Bootstrap}={SqlServerE2eMetadata.True}. "
                    + "TigerQuery never picks one for you; name the connection explicitly or "
                    + "configure a default bootstrap name."
                ]);
        }

        // One authorized profile is still not a nomination. Naming it is cheap; guessing
        // at it is a permanent invitation to run E2E work against the wrong server.
        var detail = candidateNames.Count == 1
            ? $"One profile ('{candidateNames[0]}') is marked "
                + $"{SqlServerE2eMetadata.Enabled}={SqlServerE2eMetadata.True} and "
                + $"{SqlServerE2eMetadata.Bootstrap}={SqlServerE2eMetadata.True}, but TigerQuery never "
                + "selects a bootstrap connection implicitly."
            : $"No profile in the connection store is marked with both "
                + $"{SqlServerE2eMetadata.Enabled}={SqlServerE2eMetadata.True} and "
                + $"{SqlServerE2eMetadata.Bootstrap}={SqlServerE2eMetadata.True}.";

        return Failed(
            SqlServerE2eResolutionStatus.NotConfigured,
            requestedName: null,
            candidateNames,
            [$"No E2E bootstrap connection name was supplied. {detail}"]);
    }

    /// <summary>
    /// Collects every reason the named profile may not be used, rather than stopping at the
    /// first, so one run of a test suite tells a developer everything to fix.
    /// </summary>
    private static List<string> Authorize(
        SqlServerConnectionProfile profile,
        SqlServerE2eConnectionResolutionOptions options)
    {
        var errors = new List<string>();
        var name = profile.Name;

        switch (SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Enabled))
        {
            case SqlServerE2eFlagState.True:
                break;

            case SqlServerE2eFlagState.Absent:
                errors.Add(
                    $"The connection '{name}' is not authorized for TigerQuery E2E use: metadata "
                    + $"'{SqlServerE2eMetadata.Enabled}' is not set. A usable connection is not an "
                    + "authorized one; the key must be set deliberately.");
                break;

            case SqlServerE2eFlagState.False:
                errors.Add(
                    $"The connection '{name}' sets '{SqlServerE2eMetadata.Enabled}' to "
                    + $"'{SqlServerE2eMetadata.False}', which withholds E2E authorization.");
                break;

            default:
                errors.Add(MalformedFlag(profile, SqlServerE2eMetadata.Enabled));
                break;
        }

        switch (SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.Bootstrap))
        {
            case SqlServerE2eFlagState.True:
                break;

            case SqlServerE2eFlagState.Absent:
                errors.Add(
                    $"The connection '{name}' is not authorized as the TigerQuery E2E bootstrap: "
                    + $"metadata '{SqlServerE2eMetadata.Bootstrap}' is not set. The expected profile "
                    + "name identifies a candidate but does not authorize it as bootstrap.");
                break;

            case SqlServerE2eFlagState.False:
                errors.Add(
                    $"The connection '{name}' sets '{SqlServerE2eMetadata.Bootstrap}' to "
                    + $"'{SqlServerE2eMetadata.False}', which withholds bootstrap authorization.");
                break;

            default:
                errors.Add(MalformedFlag(profile, SqlServerE2eMetadata.Bootstrap));
                break;
        }

        var creation = SqlServerE2eMetadata.ReadFlag(profile, SqlServerE2eMetadata.AllowDatabaseCreation);

        // A malformed reserved value is a configuration error whether or not this caller
        // needs the permission: the next caller will need it, and by then the typo reads as
        // an intentional denial.
        if (creation == SqlServerE2eFlagState.Malformed)
            errors.Add(MalformedFlag(profile, SqlServerE2eMetadata.AllowDatabaseCreation));

        if (options.RequireDatabaseCreationPermission)
        {
            if (creation == SqlServerE2eFlagState.Absent)
            {
                errors.Add(
                    $"The connection '{name}' does not permit database creation: metadata "
                    + $"'{SqlServerE2eMetadata.AllowDatabaseCreation}' is not set. E2E authorization "
                    + "alone never implies it.");
            }
            else if (creation == SqlServerE2eFlagState.False)
            {
                errors.Add(
                    $"The connection '{name}' sets '{SqlServerE2eMetadata.AllowDatabaseCreation}' to "
                    + $"'{SqlServerE2eMetadata.False}', which forbids database creation.");
            }
        }

        var policy = options.ValidationPolicy ?? SqlServerConnectionValidationPolicy.DatabaseOptional;
        foreach (var validationError in SqlServerConnectionValidator.ValidateComplete(profile, policy))
            errors.Add($"The connection '{name}' is not a valid connection profile: {validationError}");

        return errors;
    }

    private static string MalformedFlag(SqlServerConnectionProfile profile, string key) =>
        $"The connection '{profile.Name}' sets '{key}' to '{profile.Metadata[key]}', which is neither "
        + $"'{SqlServerE2eMetadata.True}' nor '{SqlServerE2eMetadata.False}'. Reserved TigerQuery "
        + "metadata is matched exactly, and a value it does not recognize is a configuration error "
        + "rather than a denial.";

    /// <summary>
    /// Builds a failed resolution, scrubbing credential material out of the diagnostics on
    /// the way.
    /// </summary>
    /// <remarks>
    /// Most of these strings are composed here from names and metadata keys and could not
    /// carry a secret. The exceptions are the messages
    /// <see cref="SqlServerConnectionValidator.ValidateComplete"/> forwards from
    /// <see cref="Microsoft.Data.SqlClient.SqlConnectionStringBuilder"/>, and any metadata
    /// value echoed back. Filtering at the one place every failure is constructed makes
    /// "diagnostics carry no passwords" a property of the type rather than a habit.
    /// </remarks>
    private static SqlServerE2eConnectionResolution Failed(
        SqlServerE2eResolutionStatus status,
        string? requestedName,
        IReadOnlyList<string> candidateNames,
        IReadOnlyList<string> errors,
        SqlServerConnectionProfile? profile = null) =>
        new()
        {
            Status = status,
            RequestedName = requestedName,
            CandidateNames = candidateNames,
            Errors = [.. errors.Select(error => Redact(error, profile))]
        };

    private static string Redact(string message, SqlServerConnectionProfile? profile)
    {
        if (profile is null)
            return message;

        return profile.RedactSensitiveValues(message);
    }
}
