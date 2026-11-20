using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Database.Entities;

namespace Mma.Cli.Database;

public interface IWorkspaceRegistry
{
    Task EnsureInitializedAsync(CancellationToken ct = default);

    Task<WorkspaceRecord> RegisterAsync(
        string name,
        string solutionPath,
        string mapper,
        CancellationToken ct = default);

    Task<IReadOnlyList<WorkspaceRecord>> ListAsync(CancellationToken ct = default);

    Task<WorkspaceRecord?> GetByIdAsync(int workspaceId, CancellationToken ct = default);

    Task<WorkspaceRecord?> GetByPathAsync(string solutionPath, CancellationToken ct = default);

    Task<OperationResult<WorkspaceRecord>> SyncFromManifestAsync(
        int workspaceId,
        ProjectManifest manifest,
        CancellationToken ct = default);

    Task<WorkspaceEntityRecord?> FindEntityAsync(
        int workspaceId,
        string entityName,
        CancellationToken ct = default);

    Task<bool> RemoveAsync(int workspaceId, CancellationToken ct = default);
}
