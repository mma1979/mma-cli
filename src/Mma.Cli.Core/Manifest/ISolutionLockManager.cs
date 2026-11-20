namespace Mma.Cli.Core.Manifest;

public interface ISolutionLockManager
{
    Task<IAsyncDisposable> AcquireAsync(
        string solutionPath,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
