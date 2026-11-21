using System.Text.Json;
using System.Text.Json.Serialization;
using Mma.Cli.Bff.Jobs;
using Mma.Cli.Bff.Models;
using Mma.Cli.Bff.Security;
using Mma.Cli.Bff.Services;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Process;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Database;
using Mma.Cli.Database.Entities;

namespace Mma.Cli.Bff.Endpoints;

public static class ProjectEndpoints
{
    private static readonly HashSet<string> AllowedDeleteBehaviors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cascade",
        "Restrict",
        "SetNull",
        "SetDefault",
        "NoAction"
    };

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Project Modeling & Execution");

        group.MapGet("/projects/{id:int}", async (
            int id,
            BffProjectMutationService mutationService,
            WorkspacePathGuard pathGuard,
            IManifestStore manifestStore,
            IProjectValidator validator,
            CancellationToken ct) =>
        {
            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (workspace, info) = resolved.Value;
            var loaded = await manifestStore.LoadAsync(info.SolutionPath, ct).ConfigureAwait(false);
            if (!loaded.IsSuccess || loaded.Value is null)
            {
                return BffProblemDetails.ToResult(loaded.Error, "Project manifest could not be loaded.");
            }

            var validation = await validator.ValidateAsync(info.SolutionPath, ct).ConfigureAwait(false);
            var report = validation.IsSuccess && validation.Value is not null
                ? validation.Value
                : new ProjectValidationReport(false, info.SolutionName, []);

            var summary = await WorkspaceEndpoints.BuildSummaryAsync(workspace, pathGuard, manifestStore, ct).ConfigureAwait(false);

            return Results.Ok(new ProjectDetailResponse(
                summary,
                info,
                loaded.Value.Manifest,
                loaded.Value.ContentHash,
                report));
        })
        .WithName("GetProjectDetail")
        .Produces<ProjectDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/projects/{id:int}/entities", async (
            int id,
            AddDraftEntityRequest request,
            BffProjectMutationService mutationService,
            IManifestStore manifestStore,
            IWorkspaceRegistry registry,
            CancellationToken ct) =>
        {
            if (!NamingHelper.IsValidIdentifier(request.Name))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid entity name '{request.Name}'. Must be a valid non-keyword C# identifier.");
            }

            var normalizedPk = DataTypes.Normalize(request.PkType);
            if (normalizedPk is null)
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Unsupported primary key type '{request.PkType}'.");
            }

            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (_, info) = resolved.Value;
            var entityName = request.Name.Trim();

            var updated = await manifestStore.UpdateAtomicAsync(info.SolutionPath, manifest =>
            {
                if (manifest.Entities.Any(e => string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase)))
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.ENTITY_EXISTS,
                        $"Entity '{entityName}' already exists in manifest.");
                }

                var entities = manifest.Entities.Select(e => e with { Properties = [.. e.Properties] }).ToList();
                entities.Add(new ManifestEntity(
                    entityName,
                    normalizedPk,
                    request.HasService,
                    request.HasApi,
                    Applied: false,
                    Properties: []));

                return OperationResult<ProjectManifest>.Success(manifest with
                {
                    Entities = entities,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }, ct).ConfigureAwait(false);

            if (!updated.IsSuccess || updated.Value is null)
            {
                return BffProblemDetails.ToResult(updated.Error, "Failed adding draft entity.");
            }

            await registry.SyncFromManifestAsync(id, updated.Value.Manifest, ct).ConfigureAwait(false);
            return Results.Ok(new ManifestUpdateResponse(updated.Value.Manifest, updated.Value.ContentHash));
        })
        .WithName("AddDraftEntity")
        .Produces<ManifestUpdateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/projects/{id:int}/entities/{entityName}", async (
            int id,
            string entityName,
            BffProjectMutationService mutationService,
            IManifestStore manifestStore,
            IWorkspaceRegistry registry,
            CancellationToken ct) =>
        {
            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (_, info) = resolved.Value;
            var updated = await manifestStore.UpdateAtomicAsync(info.SolutionPath, manifest =>
            {
                var entities = manifest.Entities.Select(e => e with { Properties = [.. e.Properties] }).ToList();
                var removed = entities.RemoveAll(e => string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.ENTITY_NOT_FOUND,
                        $"Entity '{entityName}' was not found in manifest.");
                }

                var relations = manifest.Relations
                    .Where(r => !string.Equals(r.ParentEntity, entityName, StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(r.ChildEntity, entityName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return OperationResult<ProjectManifest>.Success(manifest with
                {
                    Entities = entities,
                    Relations = relations,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }, ct).ConfigureAwait(false);

            if (!updated.IsSuccess || updated.Value is null)
            {
                return BffProblemDetails.ToResult(updated.Error, "Failed deleting entity.");
            }

            await registry.SyncFromManifestAsync(id, updated.Value.Manifest, ct).ConfigureAwait(false);
            return Results.Ok(new ManifestUpdateResponse(updated.Value.Manifest, updated.Value.ContentHash));
        })
        .WithName("DeleteDraftEntity")
        .Produces<ManifestUpdateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/projects/{id:int}/entities/{entityName}/properties", async (
            int id,
            string entityName,
            AddDraftPropertyRequest request,
            BffProjectMutationService mutationService,
            IManifestStore manifestStore,
            IWorkspaceRegistry registry,
            CancellationToken ct) =>
        {
            if (!NamingHelper.IsValidIdentifier(request.Name))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid property name '{request.Name}'. Must be a valid non-keyword C# identifier.");
            }

            var normalizedType = DataTypes.Normalize(request.DataType);
            if (normalizedType is null)
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Unsupported property data type '{request.DataType}'.");
            }

            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (_, info) = resolved.Value;
            var propName = request.Name.Trim();

            var updated = await manifestStore.UpdateAtomicAsync(info.SolutionPath, manifest =>
            {
                var entities = manifest.Entities.Select(e => e with { Properties = [.. e.Properties] }).ToList();
                var idx = entities.FindIndex(e => string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase));
                if (idx < 0)
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.ENTITY_NOT_FOUND,
                        $"Entity '{entityName}' was not found in manifest.");
                }

                var entity = entities[idx];
                if (entity.Properties.Any(p => string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase)))
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.PROPERTY_EXISTS,
                        $"Property '{propName}' already exists on entity '{entity.Name}'.");
                }

                entity.Properties.Add(new ManifestProperty(
                    propName,
                    normalizedType,
                    request.Nullable,
                    request.IsForeignKey,
                    Applied: false,
                    request.MaxLength,
                    request.Required));

                return OperationResult<ProjectManifest>.Success(manifest with
                {
                    Entities = entities,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }, ct).ConfigureAwait(false);

            if (!updated.IsSuccess || updated.Value is null)
            {
                return BffProblemDetails.ToResult(updated.Error, "Failed adding draft property.");
            }

            await registry.SyncFromManifestAsync(id, updated.Value.Manifest, ct).ConfigureAwait(false);
            return Results.Ok(new ManifestUpdateResponse(updated.Value.Manifest, updated.Value.ContentHash));
        })
        .WithName("AddDraftProperty")
        .Produces<ManifestUpdateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/projects/{id:int}/entities/{entityName}/properties/{propertyName}", async (
            int id,
            string entityName,
            string propertyName,
            BffProjectMutationService mutationService,
            IManifestStore manifestStore,
            IWorkspaceRegistry registry,
            CancellationToken ct) =>
        {
            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (_, info) = resolved.Value;
            var updated = await manifestStore.UpdateAtomicAsync(info.SolutionPath, manifest =>
            {
                var entities = manifest.Entities.Select(e => e with { Properties = [.. e.Properties] }).ToList();
                var idx = entities.FindIndex(e => string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase));
                if (idx < 0)
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.ENTITY_NOT_FOUND,
                        $"Entity '{entityName}' was not found in manifest.");
                }

                var entity = entities[idx];
                var removed = entity.Properties.RemoveAll(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.PROPERTY_NOT_FOUND,
                        $"Property '{propertyName}' was not found on entity '{entity.Name}'.");
                }

                return OperationResult<ProjectManifest>.Success(manifest with
                {
                    Entities = entities,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }, ct).ConfigureAwait(false);

            if (!updated.IsSuccess || updated.Value is null)
            {
                return BffProblemDetails.ToResult(updated.Error, "Failed deleting property.");
            }

            await registry.SyncFromManifestAsync(id, updated.Value.Manifest, ct).ConfigureAwait(false);
            return Results.Ok(new ManifestUpdateResponse(updated.Value.Manifest, updated.Value.ContentHash));
        })
        .WithName("DeleteDraftProperty")
        .Produces<ManifestUpdateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/projects/{id:int}/relations", async (
            int id,
            AddDraftRelationRequest request,
            BffProjectMutationService mutationService,
            IManifestStore manifestStore,
            IWorkspaceRegistry registry,
            CancellationToken ct) =>
        {
            if (!NamingHelper.IsValidIdentifier(request.ParentEntity)
                || !NamingHelper.IsValidIdentifier(request.ChildEntity)
                || !NamingHelper.IsValidIdentifier(request.ForeignKey))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    "ParentEntity, ChildEntity, and ForeignKey must be valid C# identifiers.");
            }

            var normalizedFkType = DataTypes.Normalize(request.ForeignKeyType);
            if (normalizedFkType is null)
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Unsupported foreign key data type '{request.ForeignKeyType}'.");
            }

            var deleteBehavior = string.IsNullOrWhiteSpace(request.DeleteBehavior)
                ? "Cascade"
                : request.DeleteBehavior.Trim();
            if (!AllowedDeleteBehaviors.Contains(deleteBehavior))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Unsupported DeleteBehavior '{request.DeleteBehavior}'.");
            }

            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (_, info) = resolved.Value;
            var fkName = request.ForeignKey.Trim();

            var updated = await manifestStore.UpdateAtomicAsync(info.SolutionPath, manifest =>
            {
                var parent = manifest.Entities.FirstOrDefault(e => string.Equals(e.Name, request.ParentEntity, StringComparison.OrdinalIgnoreCase));
                if (parent is null)
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.ENTITY_NOT_FOUND,
                        $"Parent entity '{request.ParentEntity}' was not found in manifest.");
                }

                var child = manifest.Entities.FirstOrDefault(e => string.Equals(e.Name, request.ChildEntity, StringComparison.OrdinalIgnoreCase));
                if (child is null)
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.ENTITY_NOT_FOUND,
                        $"Child entity '{request.ChildEntity}' was not found in manifest.");
                }

                var relationName = $"{parent.Name}_{child.Name}_{fkName}";
                var duplicate = manifest.Relations.Any(r =>
                    string.Equals(r.Name, relationName, StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(r.ParentEntity, parent.Name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.ChildEntity, child.Name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.ForeignKey, fkName, StringComparison.OrdinalIgnoreCase)));

                if (duplicate)
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.RELATION_EXISTS,
                        $"Relation '{relationName}' already exists in manifest.");
                }

                var relations = manifest.Relations.ToList();
                relations.Add(new ManifestRelation(
                    relationName,
                    parent.Name,
                    child.Name,
                    fkName,
                    normalizedFkType,
                    string.IsNullOrWhiteSpace(request.RelationType) ? "1:N" : request.RelationType.Trim(),
                    deleteBehavior,
                    Applied: false));

                return OperationResult<ProjectManifest>.Success(manifest with
                {
                    Relations = relations,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }, ct).ConfigureAwait(false);

            if (!updated.IsSuccess || updated.Value is null)
            {
                return BffProblemDetails.ToResult(updated.Error, "Failed adding draft relation.");
            }

            await registry.SyncFromManifestAsync(id, updated.Value.Manifest, ct).ConfigureAwait(false);
            return Results.Ok(new ManifestUpdateResponse(updated.Value.Manifest, updated.Value.ContentHash));
        })
        .WithName("AddDraftRelation")
        .Produces<ManifestUpdateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/projects/{id:int}/relations/{relationName}", async (
            int id,
            string relationName,
            BffProjectMutationService mutationService,
            IManifestStore manifestStore,
            IWorkspaceRegistry registry,
            CancellationToken ct) =>
        {
            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (_, info) = resolved.Value;
            var updated = await manifestStore.UpdateAtomicAsync(info.SolutionPath, manifest =>
            {
                var relations = manifest.Relations.ToList();
                var removed = relations.RemoveAll(r => string.Equals(r.Name, relationName, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    return OperationResult<ProjectManifest>.Failure(
                        ErrorCodes.RELATION_NOT_FOUND,
                        $"Relation '{relationName}' was not found in manifest.");
                }

                return OperationResult<ProjectManifest>.Success(manifest with
                {
                    Relations = relations,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }, ct).ConfigureAwait(false);

            if (!updated.IsSuccess || updated.Value is null)
            {
                return BffProblemDetails.ToResult(updated.Error, "Failed deleting relation.");
            }

            await registry.SyncFromManifestAsync(id, updated.Value.Manifest, ct).ConfigureAwait(false);
            return Results.Ok(new ManifestUpdateResponse(updated.Value.Manifest, updated.Value.ContentHash));
        })
        .WithName("DeleteDraftRelation")
        .Produces<ManifestUpdateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/projects/{id:int}/plan-diff", async (
            int id,
            BffProjectMutationService mutationService,
            CancellationToken ct) =>
        {
            var plan = await mutationService.ComputePlanAsync(id, ct).ConfigureAwait(false);
            if (!plan.IsSuccess || plan.Value is null)
            {
                return BffProblemDetails.ToResult(plan.Error, "Failed computing dry-run plan.");
            }

            return Results.Ok(plan.Value);
        })
        .WithName("PlanProjectDiff")
        .Produces<PlanDiffResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/projects/{id:int}/apply", async (
            int id,
            ApplyProjectRequest request,
            BffProjectMutationService mutationService,
            CancellationToken ct) =>
        {
            var applied = await mutationService.ApplyAsync(id, request, ct).ConfigureAwait(false);
            if (!applied.IsSuccess || applied.Value is null)
            {
                return BffProblemDetails.ToResult(applied.Error, "Failed applying pending changes.");
            }

            return Results.Ok(applied.Value);
        })
        .WithName("ApplyProjectChanges")
        .Produces<ApplyProjectResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/projects/{id:int}/build", async (
            int id,
            HttpRequest httpRequest,
            BffProjectMutationService mutationService,
            IDotnetRunner dotnetRunner,
            IBackgroundJobCoordinator jobs,
            IHostApplicationLifetime lifetime,
            CancellationToken ct) =>
        {
            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var reqBody = await ReadOptionalJsonAsync<BuildProjectRequest>(httpRequest, ct).ConfigureAwait(false);
            var configuration = string.IsNullOrWhiteSpace(reqBody?.Configuration) ? "Debug" : reqBody.Configuration.Trim();
            if (!string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid build configuration '{configuration}'. Allowed values: Debug, Release.");
            }

            var (_, info) = resolved.Value;
            var jobId = jobs.CreateJob("build", id);
            var appStopping = lifetime.ApplicationStopping;

            _ = Task.Run(async () =>
            {
                jobs.Publish(jobId, "progress", new { stage = "Starting", message = $"Building {info.SolutionName} ({configuration})..." });
                var progress = new JobEventProgress(jobs, jobId);
                var buildResult = await dotnetRunner.BuildAsync(info.SolutionPath, configuration, progress, appStopping)
                    .ConfigureAwait(false);

                if (buildResult.IsSuccess)
                {
                    jobs.Publish(jobId, "done", new
                    {
                        jobId,
                        exitCode = buildResult.Value?.ExitCode ?? 0
                    });
                }
                else
                {
                    jobs.Publish(jobId, "error", new
                    {
                        errorCode = buildResult.Error?.Code ?? ErrorCodes.PROCESS_FAILED,
                        message = buildResult.Error?.Message ?? "Build failed."
                    });
                }
            }, appStopping);

            return Results.Accepted($"/api/v1/jobs/{jobId}/events", new JobAcceptedResponse(jobId));
        })
        .WithName("BuildProject")
        .Produces<JobAcceptedResponse>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/projects/{id:int}/migrations", async (
            int id,
            AddMigrationRequest request,
            BffProjectMutationService mutationService,
            IDotnetRunner dotnetRunner,
            IBackgroundJobCoordinator jobs,
            IHostApplicationLifetime lifetime,
            CancellationToken ct) =>
        {
            if (!BffProjectMutationService.IsValidMigrationName(request.Name))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    "Invalid migration name. Use letters, digits, underscores, or hyphens starting with a letter or underscore.");
            }

            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (_, info) = resolved.Value;
            if (!BffProjectMutationService.TryResolveProjectSubpath(info.SolutionPath, request.EfProject, out var efErr))
            {
                return BffProblemDetails.ToResult(ErrorCodes.VALIDATION_ERROR, efErr!);
            }

            if (!BffProjectMutationService.TryResolveProjectSubpath(info.SolutionPath, request.StartupProject, out var startupErr))
            {
                return BffProblemDetails.ToResult(ErrorCodes.VALIDATION_ERROR, startupErr!);
            }

            var args = BffProjectMutationService.BuildEfMigrationArguments(
                info,
                request.Name.Trim(),
                request.EfProject,
                request.StartupProject);

            var jobId = jobs.CreateJob("migration", id);
            var appStopping = lifetime.ApplicationStopping;

            _ = Task.Run(async () =>
            {
                jobs.Publish(jobId, "progress", new { stage = "Starting", message = $"Creating migration '{request.Name}'..." });
                var progress = new JobEventProgress(jobs, jobId);
                var result = await dotnetRunner.RunAsync(info.SolutionPath, args, progress, appStopping)
                    .ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    jobs.Publish(jobId, "done", new
                    {
                        jobId,
                        migrationName = request.Name.Trim(),
                        exitCode = result.Value?.ExitCode ?? 0
                    });
                }
                else
                {
                    jobs.Publish(jobId, "error", new
                    {
                        errorCode = result.Error?.Code ?? ErrorCodes.PROCESS_FAILED,
                        message = result.Error?.Message ?? "Migration failed."
                    });
                }
            }, appStopping);

            return Results.Accepted($"/api/v1/jobs/{jobId}/events", new JobAcceptedResponse(jobId));
        })
        .WithName("CreateProjectMigration")
        .Produces<JobAcceptedResponse>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/jobs/{jobId}/events", async (
            string jobId,
            IBackgroundJobCoordinator jobs,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (!jobs.TryGetJobStream(jobId, out _))
            {
                await BffProblemDetails.WriteAsync(
                    httpContext,
                    BffErrorCodes.JOB_NOT_FOUND,
                    $"Job '{jobId}' was not found.")
                    .ConfigureAwait(false);
                return;
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            try
            {
                await foreach (var evt in jobs.StreamEventsAsync(jobId, ct).ConfigureAwait(false))
                {
                    var payloadJson = JsonSerializer.Serialize(evt, SseJsonOptions);
                    var frame = $"id: {evt.EventId}\nevent: {evt.Type}\ndata: {payloadJson}\n\n";
                    await httpContext.Response.WriteAsync(frame, ct).ConfigureAwait(false);
                    await httpContext.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client disconnected SSE stream cleanly.
            }
        })
        .WithName("StreamJobEvents")
        .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<T?> ReadOptionalJsonAsync<T>(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength == 0 || string.IsNullOrWhiteSpace(request.ContentType))
        {
            return default;
        }

        try
        {
            return await request.ReadFromJsonAsync<T>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed class JobEventProgress(IBackgroundJobCoordinator jobs, string jobId) : IProgress<GenerationEvent>
    {
        public void Report(GenerationEvent value)
        {
            var eventType = value.Stage == GenerationStage.ProcessOutput ? "log" : "progress";
            jobs.Publish(jobId, eventType, new
            {
                stage = value.Stage.ToString(),
                message = value.Message,
                filePath = value.FilePath
            });
        }
    }
}
