using Mma.Cli.Bff.Models;
using Mma.Cli.Bff.Security;
using Mma.Cli.Bff.Services;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Export;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Database;
using Mma.Cli.Database.Entities;

namespace Mma.Cli.Bff.Endpoints;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Workspaces & Filesystem");

        group.MapGet("/fs/browse", (
            string? path,
            WorkspacePathGuard pathGuard) =>
        {
            var requestedPath = string.IsNullOrWhiteSpace(path)
                ? pathGuard.AllowedRoots[0]
                : path;

            var pathCheck = pathGuard.ValidateDirectoryPath(requestedPath);
            if (!pathCheck.IsSuccess || pathCheck.Value is null)
            {
                return BffProblemDetails.ToResult(pathCheck.Error, "Path is not accessible.");
            }

            var currentPath = pathCheck.Value;
            if (!Directory.Exists(currentPath))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.SOLUTION_NOT_FOUND,
                    $"Directory '{currentPath}' does not exist.");
            }

            string? parentPath = null;
            var parent = Directory.GetParent(currentPath);
            if (parent is not null && pathGuard.ValidatePathWithinRoots(parent.FullName, out _).IsSuccess)
            {
                parentPath = Path.TrimEndingDirectorySeparator(parent.FullName);
            }

            var entries = new List<DirectoryEntryDto>();
            try
            {
                var dirInfo = new DirectoryInfo(currentPath);
                foreach (var subDir in dirInfo.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (!pathGuard.ValidatePathWithinRoots(subDir.FullName, out _).IsSuccess)
                    {
                        continue;
                    }

                    entries.Add(new DirectoryEntryDto(
                        subDir.Name,
                        Path.TrimEndingDirectorySeparator(subDir.FullName),
                        IsDirectory: true,
                        IsSolution: ContainsSolutionFile(subDir.FullName)));
                }

                foreach (var file in dirInfo.EnumerateFiles()
                             .Where(f => f.Name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                                         || f.Name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (!pathGuard.ValidatePathWithinRoots(file.FullName, out _).IsSuccess)
                    {
                        continue;
                    }

                    entries.Add(new DirectoryEntryDto(
                        file.Name,
                        file.FullName,
                        IsDirectory: false,
                        IsSolution: true));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return BffProblemDetails.ToResult(ErrorCodes.IO_ERROR, $"Unable to browse directory '{currentPath}': {ex.Message}");
            }

            return Results.Ok(new BrowseDirectoryResponse(
                currentPath,
                parentPath,
                pathGuard.AllowedRoots,
                entries));
        })
        .WithName("BrowseFilesystem")
        .Produces<BrowseDirectoryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/workspaces", async (
            IWorkspaceRegistry registry,
            WorkspacePathGuard pathGuard,
            IManifestStore manifestStore,
            CancellationToken ct) =>
        {
            await registry.EnsureInitializedAsync(ct).ConfigureAwait(false);
            var records = await registry.ListAsync(ct).ConfigureAwait(false);
            var summaries = new List<WorkspaceSummaryDto>(records.Count);

            foreach (var record in records)
            {
                summaries.Add(await BuildSummaryAsync(record, pathGuard, manifestStore, ct).ConfigureAwait(false));
            }

            return Results.Ok(summaries);
        })
        .WithName("ListWorkspaces")
        .Produces<IReadOnlyList<WorkspaceSummaryDto>>(StatusCodes.Status200OK);

        group.MapPost("/workspaces/register", async (
            RegisterWorkspaceRequest request,
            WorkspacePathGuard pathGuard,
            ISolutionLocator locator,
            IManifestStore manifestStore,
            IWorkspaceRegistry registry,
            CancellationToken ct) =>
        {
            var pathCheck = pathGuard.ValidateDirectoryPath(request.SolutionPath);
            if (!pathCheck.IsSuccess || pathCheck.Value is null)
            {
                return BffProblemDetails.ToResult(pathCheck.Error, "Solution path is outside allowed roots.");
            }

            var candidatePath = pathCheck.Value;
            if (File.Exists(candidatePath)
                && (candidatePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                    || candidatePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
            {
                candidatePath = Path.GetDirectoryName(candidatePath) ?? candidatePath;
            }

            var located = locator.Locate(candidatePath);
            if (!located.IsSuccess || located.Value is null)
            {
                return BffProblemDetails.ToResult(located.Error, "Solution could not be located.");
            }

            var info = located.Value;
            var manifestLoad = await manifestStore.LoadAsync(info.SolutionPath, ct).ConfigureAwait(false);
            var manifestDoc = manifestLoad.IsSuccess ? manifestLoad.Value : null;
            var effectiveMapper = manifestDoc?.Manifest.Mapper ?? info.DetectedMapper.ToFolderName();

            await registry.EnsureInitializedAsync(ct).ConfigureAwait(false);
            var registered = await registry.RegisterAsync(
                info.SolutionName,
                info.SolutionPath,
                effectiveMapper,
                ct).ConfigureAwait(false);

            if (manifestDoc is not null)
            {
                var synced = await registry.SyncFromManifestAsync(registered.Id, manifestDoc.Manifest, ct).ConfigureAwait(false);
                if (synced.IsSuccess && synced.Value is not null)
                {
                    registered = synced.Value;
                }
            }

            var summary = await BuildSummaryAsync(registered, pathGuard, manifestStore, ct).ConfigureAwait(false);
            return Results.Ok(new RegisterWorkspaceResponse(
                summary,
                info,
                manifestDoc?.Manifest,
                manifestDoc?.ContentHash));
        })
        .WithName("RegisterWorkspace")
        .Produces<RegisterWorkspaceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/workspaces", async (
            CreateWorkspaceRequest request,
            WorkspacePathGuard pathGuard,
            ISolutionService solutionService,
            IManifestStore manifestStore,
            IWorkspaceRegistry registry,
            CancellationToken ct) =>
        {
            var parentCheck = pathGuard.ValidateDirectoryPath(request.ParentDirectory);
            if (!parentCheck.IsSuccess || parentCheck.Value is null)
            {
                return BffProblemDetails.ToResult(parentCheck.Error, "Parent directory is outside allowed roots.");
            }

            if (!NamingHelper.IsValidSolutionName(request.SolutionName))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid solution name '{request.SolutionName}'. Use dot-separated valid C# identifiers.");
            }

            if (!MapperKindParser.TryParse(request.Mapper, out var mapperKind))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid mapper '{request.Mapper}'. Supported values: AutoMapper, Mapster.");
            }

            var targetSolutionPath = Path.Combine(parentCheck.Value, request.SolutionName);
            var targetCheck = pathGuard.ValidateDirectoryPath(targetSolutionPath);
            if (!targetCheck.IsSuccess)
            {
                return BffProblemDetails.ToResult(targetCheck.Error, "Target solution path is outside allowed roots.");
            }

            var createResult = await solutionService.CreateAsync(
                new CreateSolutionRequest(parentCheck.Value, request.SolutionName, mapperKind, request.DryRun),
                ct: ct).ConfigureAwait(false);

            if (!createResult.IsSuccess || createResult.Value is null)
            {
                return BffProblemDetails.ToResult(createResult.Error, "Failed to create solution.");
            }

            if (request.DryRun)
            {
                return Results.Ok(new CreateWorkspaceResponse(null, createResult.Value));
            }

            await registry.EnsureInitializedAsync(ct).ConfigureAwait(false);
            var record = await registry.RegisterAsync(
                request.SolutionName,
                targetSolutionPath,
                mapperKind.ToFolderName(),
                ct).ConfigureAwait(false);

            var manifestLoad = await manifestStore.LoadAsync(targetSolutionPath, ct).ConfigureAwait(false);
            if (manifestLoad.IsSuccess && manifestLoad.Value is not null)
            {
                var synced = await registry.SyncFromManifestAsync(record.Id, manifestLoad.Value.Manifest, ct).ConfigureAwait(false);
                if (synced.IsSuccess && synced.Value is not null)
                {
                    record = synced.Value;
                }
            }

            var summary = await BuildSummaryAsync(record, pathGuard, manifestStore, ct).ConfigureAwait(false);
            return Results.Created(
                $"/api/v1/projects/{record.Id}",
                new CreateWorkspaceResponse(summary, createResult.Value));
        })
        .WithName("CreateWorkspace")
        .Produces<CreateWorkspaceResponse>(StatusCodes.Status201Created)
        .Produces<CreateWorkspaceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/workspaces/{id:int}/export", async (
            int id,
            IWorkspaceRegistry registry,
            WorkspacePathGuard pathGuard,
            ISolutionExporter exporter,
            CancellationToken ct) =>
        {
            await registry.EnsureInitializedAsync(ct).ConfigureAwait(false);
            var workspace = await registry.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (workspace is null)
            {
                return BffProblemDetails.ToResult(ErrorCodes.SOLUTION_NOT_FOUND, $"Workspace {id} was not found.");
            }

            var pathCheck = pathGuard.ValidateDirectoryPath(workspace.SolutionPath);
            if (!pathCheck.IsSuccess || pathCheck.Value is null)
            {
                return BffProblemDetails.ToResult(pathCheck.Error, "Workspace path is outside allowed roots.");
            }

            var exportResult = await exporter.ExportAsync(pathCheck.Value, ct).ConfigureAwait(false);
            if (!exportResult.IsSuccess)
            {
                return BffProblemDetails.ToResult(exportResult.Error, "Solution export failed.");
            }

            return Results.Ok(new ExportWorkspaceResponse(true, pathCheck.Value));
        })
        .WithName("ExportWorkspace")
        .Produces<ExportWorkspaceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    internal static async Task<WorkspaceSummaryDto> BuildSummaryAsync(
        WorkspaceRecord record,
        WorkspacePathGuard pathGuard,
        IManifestStore manifestStore,
        CancellationToken ct)
    {
        var exists = Directory.Exists(record.SolutionPath);
        var entityCount = record.Entities.Count;
        var relationCount = 0;
        var pendingCount = 0;
        var hasDrift = !exists;

        if (exists && pathGuard.ValidatePathWithinRoots(record.SolutionPath, out _).IsSuccess)
        {
            var loaded = await manifestStore.LoadAsync(record.SolutionPath, ct).ConfigureAwait(false);
            if (loaded.IsSuccess && loaded.Value is not null)
            {
                var manifest = loaded.Value.Manifest;
                entityCount = manifest.Entities.Count;
                relationCount = manifest.Relations.Count;
                pendingCount = manifest.Entities.Count(e => !e.Applied)
                    + manifest.Entities.Sum(e => e.Properties.Count(p => !p.Applied))
                    + manifest.Relations.Count(r => !r.Applied);
            }
            else
            {
                hasDrift = true;
            }
        }

        return new WorkspaceSummaryDto(
            record.Id,
            record.Name,
            record.SolutionPath,
            record.Mapper,
            record.CreatedAtUtc,
            record.LastOpenedAtUtc,
            entityCount,
            relationCount,
            pendingCount,
            exists,
            hasDrift);
    }

    private static bool ContainsSolutionFile(string dirPath)
    {
        try
        {
            return Directory.EnumerateFiles(dirPath, "*.slnx", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateFiles(dirPath, "*.sln", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
