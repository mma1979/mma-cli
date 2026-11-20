using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Manifest;

public interface IManifestStore
{
    Task<OperationResult<ManifestDocument>> LoadAsync(string solutionPath, CancellationToken ct = default);

    Task<OperationResult<ManifestDocument>> SaveAtomicAsync(
        string solutionPath,
        ProjectManifest manifest,
        string? expectedHash = null,
        CancellationToken ct = default);

    Task<OperationResult<ManifestDocument>> UpdateAtomicAsync(
        string solutionPath,
        Func<ProjectManifest, OperationResult<ProjectManifest>> transform,
        CancellationToken ct = default);
}
