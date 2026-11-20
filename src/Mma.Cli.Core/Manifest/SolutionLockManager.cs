using System.Collections.Concurrent;
using System.Diagnostics;

namespace Mma.Cli.Core.Manifest;

public sealed class SolutionLockManager : ISolutionLockManager
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task<IAsyncDisposable> AcquireAsync(
        string solutionPath,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ct.ThrowIfCancellationRequested();

        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout < TimeSpan.Zero && effectiveTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be non-negative or InfiniteTimeSpan.");
        }

        var canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(solutionPath));
        var semaphore = _semaphores.GetOrAdd(canonicalPath, static _ => new SemaphoreSlim(1, 1));
        var startTimestamp = Stopwatch.GetTimestamp();

        var acquiredSemaphore = await semaphore.WaitAsync(effectiveTimeout, ct).ConfigureAwait(false);
        if (!acquiredSemaphore)
        {
            throw new TimeoutException(
                $"Timed out waiting to acquire in-process solution lock for '{canonicalPath}' after {effectiveTimeout.TotalSeconds:0.##}s.");
        }

        try
        {
            var mmaDir = Path.Combine(canonicalPath, ".mma");
            Directory.CreateDirectory(mmaDir);
            var lockFilePath = Path.Combine(mmaDir, ".lock");

            var backoffMs = 10;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var stream = new FileStream(
                        lockFilePath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    return new LockHandle(semaphore, stream);
                }
                catch (IOException ex) when (ex.GetType() == typeof(IOException))
                {
                    if (effectiveTimeout != Timeout.InfiniteTimeSpan)
                    {
                        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                        var remaining = effectiveTimeout - elapsed;
                        if (remaining <= TimeSpan.Zero)
                        {
                            throw new TimeoutException(
                                $"Timed out waiting to acquire cross-process lock '{lockFilePath}' after {effectiveTimeout.TotalSeconds:0.##}s.",
                                ex);
                        }

                        var jitterMs = Random.Shared.Next(0, 15);
                        var delay = TimeSpan.FromMilliseconds(Math.Min(backoffMs + jitterMs, Math.Max(1, remaining.TotalMilliseconds)));
                        backoffMs = Math.Min(backoffMs * 2, 100);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        var jitterMs = Random.Shared.Next(0, 15);
                        var delay = TimeSpan.FromMilliseconds(backoffMs + jitterMs);
                        backoffMs = Math.Min(backoffMs * 2, 100);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        catch
        {
            semaphore.Release();
            throw;
        }
    }

    private sealed class LockHandle(SemaphoreSlim semaphore, FileStream lockStream) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await lockStream.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Ensure the in-process semaphore is always released even if the stream throws on close.
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
