using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Database.Entities;

namespace Mma.Cli.Database;

public sealed class SqliteWorkspaceRegistry : IWorkspaceRegistry
{
    private readonly WorkspaceDbContext _db;

    public SqliteWorkspaceRegistry(WorkspaceDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        var connectionString = _db.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (!string.IsNullOrWhiteSpace(builder.DataSource)
                && !string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                var fullDataSource = Path.GetFullPath(builder.DataSource);
                var parentDir = Path.GetDirectoryName(fullDataSource);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }
            }
        }

        await _db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
    }

    public async Task<WorkspaceRecord> RegisterAsync(
        string name,
        string solutionPath,
        string mapper,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapper);

        var canonicalPath = CanonicalizePath(solutionPath);
        var now = DateTimeOffset.UtcNow.ToUniversalTime();

        var existing = await FindTrackedByPathAsync(canonicalPath, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Name = name;
            existing.Mapper = mapper;
            existing.CreatedAtUtc = existing.CreatedAtUtc.ToUniversalTime();
            existing.LastOpenedAtUtc = now;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return existing;
        }

        var record = new WorkspaceRecord
        {
            Name = name,
            SolutionPath = canonicalPath,
            Mapper = mapper,
            CreatedAtUtc = now,
            LastOpenedAtUtc = now
        };

        _db.Workspaces.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return record;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var conflicted = await FindTrackedByPathAsync(canonicalPath, ct).ConfigureAwait(false);
            if (conflicted is null)
            {
                throw;
            }

            conflicted.Name = name;
            conflicted.Mapper = mapper;
            conflicted.CreatedAtUtc = conflicted.CreatedAtUtc.ToUniversalTime();
            conflicted.LastOpenedAtUtc = DateTimeOffset.UtcNow.ToUniversalTime();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return conflicted;
        }
    }

    public async Task<IReadOnlyList<WorkspaceRecord>> ListAsync(CancellationToken ct = default)
    {
        var all = await _db.Workspaces
            .AsNoTracking()
            .Include(w => w.Entities)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return all
            .OrderByDescending(w => w.LastOpenedAtUtc)
            .ToList();
    }

    public Task<WorkspaceRecord?> GetByIdAsync(int workspaceId, CancellationToken ct = default) =>
        _db.Workspaces
            .AsNoTracking()
            .Include(w => w.Entities)
            .FirstOrDefaultAsync(w => w.Id == workspaceId, ct);

    public async Task<WorkspaceRecord?> GetByPathAsync(string solutionPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        var canonicalPath = CanonicalizePath(solutionPath);

        var exact = await _db.Workspaces
            .AsNoTracking()
            .Include(w => w.Entities)
            .FirstOrDefaultAsync(w => w.SolutionPath == canonicalPath, ct)
            .ConfigureAwait(false);

        if (exact is not null || !OperatingSystem.IsWindows())
        {
            return exact;
        }

        var candidates = await _db.Workspaces
            .AsNoTracking()
            .Include(w => w.Entities)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return candidates.FirstOrDefault(
            w => string.Equals(w.SolutionPath, canonicalPath, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OperationResult<WorkspaceRecord>> SyncFromManifestAsync(
        int workspaceId,
        ProjectManifest manifest,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var workspace = await _db.Workspaces
            .Include(w => w.Entities)
            .FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
            .ConfigureAwait(false);

        if (workspace is null)
        {
            return OperationResult<WorkspaceRecord>.Failure(
                ErrorCodes.SOLUTION_NOT_FOUND,
                $"Workspace {workspaceId} was not found.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.SolutionName))
        {
            workspace.Name = manifest.SolutionName;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Mapper))
        {
            workspace.Mapper = manifest.Mapper;
        }

        workspace.CreatedAtUtc = workspace.CreatedAtUtc.ToUniversalTime();
        workspace.LastOpenedAtUtc = DateTimeOffset.UtcNow.ToUniversalTime();

        var incomingByName = new Dictionary<string, ManifestEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in manifest.Entities)
        {
            if (!string.IsNullOrWhiteSpace(entity.Name))
            {
                incomingByName.TryAdd(entity.Name, entity);
            }
        }

        var toRemove = workspace.Entities
            .Where(e => !incomingByName.ContainsKey(e.EntityName))
            .ToList();

        foreach (var obsolete in toRemove)
        {
            workspace.Entities.Remove(obsolete);
            _db.Entities.Remove(obsolete);
        }

        var existingByName = new Dictionary<string, WorkspaceEntityRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in workspace.Entities)
        {
            existingByName.TryAdd(existing.EntityName, existing);
        }

        foreach (var (name, manifestEntity) in incomingByName)
        {
            if (existingByName.TryGetValue(name, out var existingRecord))
            {
                existingRecord.EntityName = manifestEntity.Name;
                existingRecord.PkType = manifestEntity.PkType;
                existingRecord.HasApi = manifestEntity.HasApi;
                existingRecord.Applied = manifestEntity.Applied;
            }
            else
            {
                workspace.Entities.Add(new WorkspaceEntityRecord
                {
                    WorkspaceId = workspaceId,
                    EntityName = manifestEntity.Name,
                    PkType = manifestEntity.PkType,
                    HasApi = manifestEntity.HasApi,
                    Applied = manifestEntity.Applied
                });
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return OperationResult<WorkspaceRecord>.Success(workspace);
    }

    public Task<WorkspaceEntityRecord?> FindEntityAsync(
        int workspaceId,
        string entityName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        return _db.Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.WorkspaceId == workspaceId && e.EntityName == entityName,
                ct);
    }

    public async Task<bool> RemoveAsync(int workspaceId, CancellationToken ct = default)
    {
        var workspace = await _db.Workspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
            .ConfigureAwait(false);

        if (workspace is null)
        {
            return false;
        }

        _db.Workspaces.Remove(workspace);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<WorkspaceRecord?> FindTrackedByPathAsync(string canonicalPath, CancellationToken ct)
    {
        var exact = await _db.Workspaces
            .Include(w => w.Entities)
            .FirstOrDefaultAsync(w => w.SolutionPath == canonicalPath, ct)
            .ConfigureAwait(false);

        if (exact is not null || !OperatingSystem.IsWindows())
        {
            return exact;
        }

        var all = await _db.Workspaces
            .Include(w => w.Entities)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return all.FirstOrDefault(
            w => string.Equals(w.SolutionPath, canonicalPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string CanonicalizePath(string solutionPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(solutionPath));
}
