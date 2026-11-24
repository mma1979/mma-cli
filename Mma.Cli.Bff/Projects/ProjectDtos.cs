namespace Mma.Cli.Bff.Projects;

public sealed record BrowseFsResponse(
    string CurrentPath,
    string? ParentPath,
    IReadOnlyList<string> Directories,
    bool HasSolutionFile,
    bool IsMmaSolution);

public sealed record MkdirRequest(string ParentPath, string Name);

public sealed record MkdirResponse(string Path);

public sealed record ProjectSummaryDto(
    int Id,
    string Name,
    string Path,
    string Mapper,
    bool ExistsOnDisk,
    int EntityCount,
    int PendingCount,
    DateTime? UpdatedAtUtc);

public sealed record CreateProjectRequest(
    string Name,
    string ParentDirectory,
    string? Mapper = null,
    bool RunInitialMigration = false);

public sealed record ImportProjectRequest(string SolutionPath);
