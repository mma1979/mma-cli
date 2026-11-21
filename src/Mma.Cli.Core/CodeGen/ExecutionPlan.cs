namespace Mma.Cli.Core.CodeGen;

public enum FileChangeKind
{
    Create = 0,
    Modify = 1,
    Delete = 2
}

public sealed record StagedFileChange(
    string FullPath,
    FileChangeKind Kind,
    string? NewContent = null);

public sealed record PlannedFileDiff(
    string FullPath,
    string RelativePath,
    FileChangeKind Kind,
    string UnifiedDiff,
    string? OriginalHash,
    string? NewHash,
    string? NewContent);

public sealed record ExecutionPlan(
    string SolutionPath,
    string PlanHash,
    IReadOnlyList<PlannedFileDiff> FileChanges,
    DateTimeOffset CreatedAtUtc);
