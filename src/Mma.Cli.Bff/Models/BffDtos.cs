using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Database.Introspection;

namespace Mma.Cli.Bff.Models;

public sealed record HealthResponse(
    string Status,
    string Version,
    string Environment,
    string? DotnetVersion,
    bool DotnetEfAvailable,
    DateTimeOffset TimestampUtc);

public sealed record BootstrapAuthRequest(string? Token);

public sealed record BootstrapAuthResponse(
    bool Authenticated,
    string CsrfToken);

public sealed record AuthSessionResponse(
    bool Authenticated,
    string? CsrfToken,
    string UserId,
    string DisplayName);

public sealed record DirectoryEntryDto(
    string Name,
    string FullPath,
    bool IsDirectory,
    bool IsSolution);

public sealed record BrowseDirectoryResponse(
    string CurrentPath,
    string? ParentPath,
    IReadOnlyList<string> AllowedRoots,
    IReadOnlyList<DirectoryEntryDto> Entries);

public sealed record WorkspaceSummaryDto(
    int Id,
    string Name,
    string SolutionPath,
    string Mapper,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastOpenedAtUtc,
    int EntityCount,
    int RelationCount,
    int PendingChangesCount,
    bool ExistsOnDisk,
    bool HasDrift);

public sealed record RegisterWorkspaceRequest(string SolutionPath);

public sealed record RegisterWorkspaceResponse(
    WorkspaceSummaryDto Workspace,
    SolutionInfo SolutionInfo,
    ProjectManifest? Manifest,
    string? ManifestHash);

public sealed record CreateWorkspaceRequest(
    string ParentDirectory,
    string SolutionName,
    string Mapper = "AutoMapper",
    bool DryRun = false);

public sealed record CreateWorkspaceResponse(
    WorkspaceSummaryDto? Workspace,
    GenerationSummary Summary);

public sealed record ExportWorkspaceResponse(
    bool Exported,
    string SolutionPath);

public sealed record ProjectDetailResponse(
    WorkspaceSummaryDto Workspace,
    SolutionInfo SolutionInfo,
    ProjectManifest Manifest,
    string ManifestHash,
    ProjectValidationReport Validation);

public sealed record ManifestUpdateResponse(
    ProjectManifest Manifest,
    string ContentHash);

public sealed record AddDraftEntityRequest(
    string Name,
    string PkType = "Guid",
    bool HasService = true,
    bool HasApi = true);

public sealed record AddDraftPropertyRequest(
    string Name,
    string DataType,
    bool Nullable = false,
    bool IsForeignKey = false,
    int? MaxLength = null,
    bool? Required = null);

public sealed record AddDraftRelationRequest(
    string ParentEntity,
    string ChildEntity,
    string ForeignKey,
    string ForeignKeyType = "Guid",
    string RelationType = "1:N",
    string DeleteBehavior = "Cascade");

public sealed record PlanFileDiffDto(
    string RelativePath,
    string FullPath,
    string Kind,
    string UnifiedDiff,
    string? OriginalHash,
    string? NewHash);

public sealed record PlanDiffResponse(
    string PlanHash,
    string ManifestHash,
    bool HasChanges,
    IReadOnlyList<PlanFileDiffDto> FileChanges);

public sealed record ApplyProjectRequest(
    string? PlanHash = null,
    bool RunBuild = false,
    bool CreateMigration = false,
    string? MigrationName = null);

public sealed record ApplyProjectResponse(
    string JobId,
    string PlanHash,
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> ModifiedFiles,
    ProjectManifest Manifest);

public sealed record BuildProjectRequest(string Configuration = "Debug");

public sealed record AddMigrationRequest(
    string Name,
    string? StartupProject = null,
    string? EfProject = null);

public sealed record JobAcceptedResponse(string JobId);

public sealed record DbImportConnectionRequest(
    DatabaseProviderKind Provider,
    string? ConnectionString = null,
    DatabaseConnectionParameters? Parameters = null);

public sealed record DbImportTestResponse(
    bool Connected,
    string Provider);

public sealed record DbIntrospectApiRequest(
    DatabaseProviderKind Provider,
    string? ConnectionString = null,
    DatabaseConnectionParameters? Parameters = null,
    SchemaFilterOptions? Filters = null);

public sealed record DbImportApplyRequest(
    DatabaseSchemaSnapshot? Snapshot = null,
    DatabaseProviderKind? Provider = null,
    string? ConnectionString = null,
    DatabaseConnectionParameters? Parameters = null,
    SchemaFilterOptions? Filters = null,
    IReadOnlyList<string>? SelectedTables = null,
    IReadOnlyDictionary<string, string>? TableEntityOverrides = null,
    bool ApplyImmediately = false);

public sealed record DbImportApplyResponse(
    ProjectManifest Manifest,
    string ContentHash,
    string? JobId,
    PlanDiffResponse? Plan);

public sealed record TemplateItemDto(
    string Mapper,
    string Name,
    bool IsCustomOverride,
    string? OverridePath,
    string Content);

public sealed record TemplateListResponse(
    string ActiveMapper,
    IReadOnlyList<TemplateItemDto> Templates);

public sealed record UpdateTemplateRequest(string Content);

public sealed record UpdateTemplateResponse(
    string Mapper,
    string Name,
    bool IsCustomOverride,
    string Content,
    string RenderedPreview);
