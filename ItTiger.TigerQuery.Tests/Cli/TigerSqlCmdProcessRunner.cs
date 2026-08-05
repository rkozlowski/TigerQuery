using System.Diagnostics;

namespace ItTiger.TigerQuery.Tests.Cli;

internal static class TigerSqlCmdProcessRunner
{
    public static async Task<TigerSqlCmdProcessResult> RunAsync(
        IReadOnlyDictionary<string, string?> environment,
        string workingDirectory,
        params string[] arguments)
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "tiger-sqlcmd.dll");
        Assert.True(File.Exists(assemblyPath), $"tiger-sqlcmd was not found at '{assemblyPath}'.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        foreach (var (name, value) in environment)
        {
            if (value is null)
                startInfo.Environment.Remove(name);
            else
                startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start tiger-sqlcmd.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw new TimeoutException("tiger-sqlcmd did not exit within 30 seconds.");
        }

        return new TigerSqlCmdProcessResult(
            process.ExitCode,
            await stdout,
            await stderr);
    }
}

internal sealed record TigerSqlCmdProcessResult(
    int ExitCode,
    string StdOut,
    string StdErr);

/// <summary>
/// The deterministic child process the <c>exec</c> tests start: a real executable that
/// echoes its arguments and selected environment variables and exits with a requested code.
/// The build copies its whole runnable output next to the test host.
/// </summary>
internal static class TigerSqlCmdTestChild
{
    /// <summary>The test child's apphost.</summary>
    public static string ExecutablePath
    {
        get
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "testchild",
                OperatingSystem.IsWindows() ? "tq-test-child.exe" : "tq-test-child");
            Assert.True(File.Exists(path), $"The exec test child was not found at '{path}'.");
            return path;
        }
    }

    /// <summary>The values the child echoed for <c>--echo-env NAME</c>, keyed by name.</summary>
    public static IReadOnlyDictionary<string, string> EchoedEnvironment(string stdout)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Lines(stdout).Where(line => line.StartsWith("ENV[", StringComparison.Ordinal)))
        {
            var close = line.IndexOf("]=", StringComparison.Ordinal);
            if (close > 0)
                values[line[4..close]] = line[(close + 2)..];
        }

        return values;
    }

    /// <summary>The child's argument vector, in order, exactly as it received it.</summary>
    public static IReadOnlyList<string> Arguments(string stdout) =>
    [
        .. Lines(stdout)
            .Where(line => line.StartsWith("ARG[", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf('=') + 1)..])
    ];

    private static IEnumerable<string> Lines(string stdout) =>
        stdout.Split('\n').Select(line => line.TrimEnd('\r'));
}
