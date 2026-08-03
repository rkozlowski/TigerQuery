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
