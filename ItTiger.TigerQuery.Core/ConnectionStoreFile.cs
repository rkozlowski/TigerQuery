using System.Text;

namespace ItTiger.TigerQuery.Core;

/// <summary>
/// Owns the on-disk representation of one connection-store JSON file: path
/// normalization, mutation coordination, and crash-safe replacement.
/// </summary>
/// <remarks>
/// One instance exists per <see cref="SqlServerConnectionStore"/>, but the
/// coordination it acquires is keyed by the normalized path, so two stores
/// constructed over the same file still serialize against each other.
/// </remarks>
internal sealed class ConnectionStoreFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeSpan mutationTimeout;

    internal ConnectionStoreFile(string filePath, TimeSpan mutationTimeout)
    {
        FilePath = System.IO.Path.GetFullPath(filePath);
        this.mutationTimeout = mutationTimeout > TimeSpan.Zero
            ? mutationTimeout
            : TimeSpan.Zero;
    }

    /// <summary>Gets the normalized absolute store path.</summary>
    internal string FilePath { get; }

    /// <summary>
    /// Set by tests to observe or fail a write after the temporary file exists and
    /// before the destination is replaced.
    /// </summary>
    internal Action<string>? WriteFaultHook { get; set; }

    /// <summary>
    /// Acquires exclusive mutation rights for this path. The scope is reentrant on
    /// the calling thread, so a composite operation may nest helper calls.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// The coordination could not be acquired within the configured timeout.
    /// </exception>
    internal IDisposable EnterMutationScope() =>
        ConnectionStoreCoordinator.Enter(FilePath, mutationTimeout);

    /// <summary>Reads the whole file, or returns null when it does not exist.</summary>
    internal string? ReadAllTextOrNull()
    {
        if (!File.Exists(FilePath))
            return null;

        return File.ReadAllText(FilePath);
    }

    /// <summary>
    /// Writes <paramref name="json"/> to a same-directory temporary file, flushes it
    /// to disk, and replaces the destination in one step.
    /// </summary>
    /// <remarks>
    /// A failure before the replacement leaves the previous file untouched and
    /// removes only this operation's temporary artifact. Callers hold the mutation
    /// scope; readers are never exposed to a partially written file.
    /// </remarks>
    internal void WriteAtomic(string json)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = $"{FilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                using (var writer = new StreamWriter(stream, Utf8NoBom, 4096, leaveOpen: true))
                {
                    writer.Write(json);
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            WriteFaultHook?.Invoke(temporaryPath);
            Replace(temporaryPath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    // A concurrent reader can hold a share mode that briefly denies the rename, so the
    // replacement is retried for the same budget the mutation scope already allows.
    private void Replace(string temporaryPath)
    {
        var deadline = Environment.TickCount64 + (long)mutationTimeout.TotalMilliseconds;
        var delayMilliseconds = 5;

        while (true)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(temporaryPath, FilePath, destinationBackupFileName: null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Move(temporaryPath, FilePath, overwrite: true);
                    }
                }
                else
                {
                    File.Move(temporaryPath, FilePath, overwrite: true);
                }

                return;
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
            }
            catch (UnauthorizedAccessException) when (Environment.TickCount64 < deadline)
            {
            }

            Thread.Sleep(delayMilliseconds);
            delayMilliseconds = Math.Min(delayMilliseconds * 2, 50);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
