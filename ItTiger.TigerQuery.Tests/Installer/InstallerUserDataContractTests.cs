namespace ItTiger.TigerQuery.Tests.Installer;

/// <summary>
/// The Windows installer owns only what it installs. Uninstall leaves the shared connection
/// store, which other Tiger tools may use, and every file the user created, so no installer
/// script may refer to a per-user or shared data location at all — not to delete it, not to
/// write it. Deleting shared data has to stay a separate, explicitly requested operation.
/// </summary>
public sealed class InstallerUserDataContractTests
{
    // Matched case-insensitively. "appdata" also covers {userappdata}, {localappdata},
    // {commonappdata}, {%APPDATA}, and GetEnv('APPDATA'). The vendor folder is matched as a
    // path segment, because the publisher's web address carries the same name.
    private static readonly string[] UserDataReferences =
    [
        "appdata",
        "userdocs",
        "userprofile",
        @"\ItTiger.net",
        @"ItTiger.net\",
        "sqlserver-connections"
    ];

    [Fact]
    public void InstallerScriptsNeverReferToUserOrSharedData()
    {
        var installerDirectory = Path.Combine(FindRepositoryRoot(), "ItTiger.TigerSqlCmd.Installer");
        var scripts = Directory.GetFiles(installerDirectory, "*.iss");

        Assert.NotEmpty(scripts);
        foreach (var script in scripts)
        {
            var text = File.ReadAllText(script);
            foreach (var reference in UserDataReferences)
            {
                Assert.False(
                    text.Contains(reference, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(script)} refers to '{reference}'. The installer must not touch "
                        + "the shared connection store or other user data; see the uninstall section of README.md.");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TigerQuery.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"TigerQuery.sln was not found above '{AppContext.BaseDirectory}'.");
    }
}
