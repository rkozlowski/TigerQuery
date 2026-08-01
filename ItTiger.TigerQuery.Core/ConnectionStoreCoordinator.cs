namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Serializes connection-store mutations by normalized file path, first within the
/// process and then, best effort, across processes through an exclusive sibling
/// lock file.
/// </summary>
/// <remarks>
/// <para>
/// The in-process gate is authoritative: two <see cref="SqlServerConnectionStore"/>
/// instances over the same path always serialize. The cross-process lock file adds
/// protection against a second tool or test process mutating the same user store,
/// but it cannot protect against a process that ignores it or against a file system
/// with no exclusive sharing semantics.
/// </para>
/// <para>
/// Scopes are reentrant on the acquiring thread so a composite operation such as
/// copy can nest load and save helpers inside one scope.
/// </para>
/// </remarks>
internal static class ConnectionStoreCoordinator
{
    private static readonly Dictionary<string, PathGate> Gates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static IDisposable Enter(string normalizedPath, TimeSpan timeout)
    {
        PathGate gate;
        lock (Gates)
        {
            if (!Gates.TryGetValue(normalizedPath, out var existing))
            {
                existing = new PathGate();
                Gates.Add(normalizedPath, existing);
            }

            gate = existing;
        }

        var acquired = false;
        Monitor.TryEnter(gate.SyncRoot, timeout, ref acquired);
        if (!acquired)
        {
            throw new TimeoutException(
                $"Another operation is mutating the connection store '{normalizedPath}'. " +
                $"The wait exceeded {timeout.TotalSeconds:0.###} seconds.");
        }

        try
        {
            if (gate.Depth == 0)
                gate.CrossProcessLock = AcquireCrossProcessLock(normalizedPath, timeout);

            gate.Depth++;
        }
        catch
        {
            Monitor.Exit(gate.SyncRoot);
            throw;
        }

        return new Scope(gate);
    }

    private static FileStream? AcquireCrossProcessLock(string normalizedPath, TimeSpan timeout)
    {
        var lockPath = normalizedPath + ".lock";
        var directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        var delayMilliseconds = 5;
        Exception? lastFailure = null;

        while (true)
        {
            try
            {
                // DeleteOnClose keeps the lock file from outliving the process that took it.
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException ex)
            {
                lastFailure = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastFailure = ex;
            }

            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException(
                    $"Another process is mutating the connection store '{normalizedPath}'. " +
                    $"The wait exceeded {timeout.TotalSeconds:0.###} seconds.",
                    lastFailure);
            }

            Thread.Sleep(delayMilliseconds);
            delayMilliseconds = Math.Min(delayMilliseconds * 2, 50);
        }
    }

    private sealed class PathGate
    {
        public object SyncRoot { get; } = new();

        public FileStream? CrossProcessLock { get; set; }

        public int Depth { get; set; }
    }

    private sealed class Scope(PathGate gate) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            gate.Depth--;
            if (gate.Depth == 0)
            {
                gate.CrossProcessLock?.Dispose();
                gate.CrossProcessLock = null;
            }

            Monitor.Exit(gate.SyncRoot);
        }
    }
}
