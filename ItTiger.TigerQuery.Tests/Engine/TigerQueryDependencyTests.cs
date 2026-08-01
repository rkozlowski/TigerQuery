using System.Reflection;

namespace ItTiger.TigerQuery.Tests.Engine;

/// <summary>
/// Protects the layering boundary that output routing must not break:
/// <c>ItTiger.TigerQuery</c> owns file output and therefore must stay free of any
/// console, TigerCli, or tiger-sqlcmd dependency.
/// </summary>
public sealed class TigerQueryDependencyTests
{
    private static readonly Assembly TigerQueryAssembly = typeof(SqlCmdParser).Assembly;

    [Fact]
    public void TheEngineAssemblyReferencesNoApplicationLayer()
    {
        var forbidden = new[] { "ItTiger.Cli", "TigerCli", "ItTiger.TigerSqlCmd", "ItTiger.TigerQuery.CliCore" };

        var offenders = TigerQueryAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => forbidden.Any(item => name.Contains(item, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheEngineProjectFileDeclaresNoApplicationReference()
    {
        var projectFile = FindProjectFile("ItTiger.TigerQuery.csproj");
        var text = File.ReadAllText(projectFile);

        Assert.DoesNotContain("TigerCli", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TigerSqlCmd", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CliCore", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheEngineAssemblyReferencesNoConsoleFormattingPackage()
    {
        var referenced = TigerQueryAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

        Assert.Contains("Microsoft.Data.SqlClient", referenced);
        Assert.DoesNotContain(referenced, name => name.Contains("Spectre", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, name => name.Contains("System.CommandLine", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindProjectFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "ItTiger.TigerQuery", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName} above {AppContext.BaseDirectory}.");
    }
}
