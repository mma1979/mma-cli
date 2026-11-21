using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mma.Cli.Bff.Jobs;
using Mma.Cli.Bff.Models;
using Mma.Cli.Bff.Security;
using Mma.Cli.Core.CodeGen;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Process;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Roslyn;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Database;
using Mma.Cli.Database.Entities;
using Mma.Cli.Database.Introspection;

namespace Mma.Cli.Bff.Services;

public sealed record ResolvedWorkspace(WorkspaceRecord Workspace, SolutionInfo SolutionInfo);

public sealed partial class BffProjectMutationService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IWorkspaceRegistry _registry;
    private readonly WorkspacePathGuard _pathGuard;
    private readonly ISolutionLocator _solutionLocator;
    private readonly IManifestStore _manifestStore;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly IDryRunDiffEngine _diffEngine;
    private readonly IEntityService _entityService;
    private readonly IPropertyService _propertyService;
    private readonly IRelationService _relationService;
    private readonly IDotnetRunner _dotnetRunner;
    private readonly IDatabaseIntrospectorFactory _introspectorFactory;
    private readonly IBackgroundJobCoordinator _jobs;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _applyGates = new();

    public BffProjectMutationService(
        IWorkspaceRegistry registry,
        WorkspacePathGuard pathGuard,
        ISolutionLocator solutionLocator,
        IManifestStore manifestStore,
        ITemplateRenderer templateRenderer,
        IDryRunDiffEngine diffEngine,
        IEntityService entityService,
        IPropertyService propertyService,
        IRelationService relationService,
        IDotnetRunner dotnetRunner,
        IDatabaseIntrospectorFactory introspectorFactory,
        IBackgroundJobCoordinator jobs)
    {
        _registry = registry;
        _pathGuard = pathGuard;
        _solutionLocator = solutionLocator;
        _manifestStore = manifestStore;
        _templateRenderer = templateRenderer;
        _diffEngine = diffEngine;
        _entityService = entityService;
        _propertyService = propertyService;
        _relationService = relationService;
        _dotnetRunner = dotnetRunner;
        _introspectorFactory = introspectorFactory;
        _jobs = jobs;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeMigrationNameRegex();

    public static bool IsValidMigrationName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && SafeMigrationNameRegex().IsMatch(name.Trim());

    public static bool TryResolveProjectSubpath(string solutionPath, string? projectPath, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return true;
        }

        var trimmed = projectPath.Trim();
        if (trimmed.StartsWith('-') || trimmed.IndexOf('\0') >= 0)
        {
            errorMessage = $"Invalid project path '{projectPath}'.";
            return false;
        }

        try
        {
            var fullSolution = Path.TrimEndingDirectorySeparator(Path.GetFullPath(solutionPath));
            var fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed, fullSolution));
            var prefix = fullSolution.EndsWith(Path.DirectorySeparatorChar)
                ? fullSolution
                : fullSolution + Path.DirectorySeparatorChar;
            if (!string.Equals(fullCandidate, fullSolution, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && !fullCandidate.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                errorMessage = $"Project path '{projectPath}' must stay within solution directory.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errorMessage = $"Invalid project path '{projectPath}': {ex.Message}";
            return false;
        }
    }

    public async Task<OperationResult<ResolvedWorkspace>> ResolveWorkspaceAsync(
        int projectId,
        CancellationToken ct)
    {
        await _registry.EnsureInitializedAsync(ct).ConfigureAwait(false);
        var workspace = await _registry.GetByIdAsync(projectId, ct).ConfigureAwait(false);
        if (workspace is null)
        {
            return OperationResult<ResolvedWorkspace>.Failure(
                ErrorCodes.SOLUTION_NOT_FOUND,
                $"Project workspace {projectId} was not found.");
        }

        var pathCheck = _pathGuard.ValidateDirectoryPath(workspace.SolutionPath);
        if (!pathCheck.IsSuccess || pathCheck.Value is null)
        {
            return OperationResult<ResolvedWorkspace>.Failure(
                pathCheck.Error ?? new GenerationError(BffErrorCodes.PATH_OUTSIDE_ALLOWLIST, "Workspace path is outside allowed roots."));
        }

        var located = _solutionLocator.Locate(pathCheck.Value);
        if (!located.IsSuccess || located.Value is null)
        {
            return OperationResult<ResolvedWorkspace>.Failure(
                located.Error ?? new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Solution could not be located."));
        }

        return OperationResult<ResolvedWorkspace>.Success(new ResolvedWorkspace(workspace, located.Value));
    }

    public async Task<OperationResult<PlanDiffResponse>> ComputePlanAsync(int projectId, CancellationToken ct)
    {
        var computed = await ComputeInternalPlanAsync(projectId, ct).ConfigureAwait(false);
        if (!computed.IsSuccess || computed.Value is null)
        {
            return OperationResult<PlanDiffResponse>.Failure(
                computed.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to compute plan."));
        }

        return OperationResult<PlanDiffResponse>.Success(computed.Value.Response);
    }

    public async Task<OperationResult<ApplyProjectResponse>> ApplyAsync(
        int projectId,
        ApplyProjectRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CreateMigration && !IsValidMigrationName(request.MigrationName))
        {
            return OperationResult<ApplyProjectResponse>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "Invalid EF migration name. Use letters, digits, underscores, or hyphens starting with a letter or underscore.");
        }

        var gate = _applyGates.GetOrAdd(projectId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var computed = await ComputeInternalPlanAsync(projectId, ct).ConfigureAwait(false);
            if (!computed.IsSuccess || computed.Value is null)
            {
                return OperationResult<ApplyProjectResponse>.Failure(
                    computed.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to compute apply plan."));
            }

            var planContext = computed.Value;
            if (!string.IsNullOrWhiteSpace(request.PlanHash)
                && !string.Equals(request.PlanHash.Trim(), planContext.Response.PlanHash, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<ApplyProjectResponse>.Failure(
                    ErrorCodes.MANIFEST_CONFLICT,
                    $"Manifest or files changed since plan computation. Expected '{planContext.Response.PlanHash}', got '{request.PlanHash}'.");
            }

            var jobId = _jobs.CreateJob("apply", projectId);
            var createdSet = new HashSet<string>(PathComparer);
            var modifiedSet = new HashSet<string>(PathComparer);
            var progress = new InlineProgress(evt =>
            {
                var eventType = evt.Stage == GenerationStage.ProcessOutput ? "log" : "progress";
                _jobs.Publish(jobId, eventType, new
                {
                    stage = evt.Stage.ToString(),
                    message = evt.Message,
                    filePath = evt.FilePath
                });
            });

            _jobs.Publish(jobId, "progress", new
            {
                stage = "Starting",
                message = "Applying pending manifest changes..."
            });

            var info = planContext.SolutionInfo;
            var mapper = planContext.Mapper;

            foreach (var entity in planContext.PendingEntities)
            {
                var res = await _entityService.GenerateAsync(
                    new AddEntityRequest(info.SolutionPath, entity.Name, entity.PkType, mapper, entity.HasApi, DryRun: false),
                    progress,
                    ct).ConfigureAwait(false);

                if (!res.IsSuccess || res.Value is null)
                {
                    var err = res.Error ?? new GenerationError(ErrorCodes.IO_ERROR, $"Failed generating entity '{entity.Name}'.");
                    _jobs.Publish(jobId, "error", new { errorCode = err.Code, message = err.Message });
                    return OperationResult<ApplyProjectResponse>.Failure(err);
                }

                RecordPaths(res.Value, createdSet, modifiedSet);
            }

            foreach (var (entityName, prop) in planContext.PendingProperties)
            {
                var res = await _propertyService.ApplyAsync(
                    new AddPropertyRequest(info.SolutionPath, entityName, prop.Name, prop.DataType, prop.Nullable, Remove: false, DryRun: false),
                    progress,
                    ct).ConfigureAwait(false);

                if (!res.IsSuccess || res.Value is null)
                {
                    var err = res.Error ?? new GenerationError(ErrorCodes.IO_ERROR, $"Failed applying property '{entityName}.{prop.Name}'.");
                    _jobs.Publish(jobId, "error", new { errorCode = err.Code, message = err.Message });
                    return OperationResult<ApplyProjectResponse>.Failure(err);
                }

                RecordPaths(res.Value, createdSet, modifiedSet);
            }

            foreach (var rel in planContext.PendingRelations)
            {
                var res = await _relationService.ApplyAsync(
                    new AddRelationRequest(info.SolutionPath, rel.ParentEntity, rel.ChildEntity, rel.ForeignKey, rel.ForeignKeyType, Remove: false, DryRun: false),
                    progress,
                    ct).ConfigureAwait(false);

                if (!res.IsSuccess || res.Value is null)
                {
                    var err = res.Error ?? new GenerationError(ErrorCodes.IO_ERROR, $"Failed applying relation '{rel.Name}'.");
                    _jobs.Publish(jobId, "error", new { errorCode = err.Code, message = err.Message });
                    return OperationResult<ApplyProjectResponse>.Failure(err);
                }

                RecordPaths(res.Value, createdSet, modifiedSet);
            }

            var reloaded = await _manifestStore.LoadAsync(info.SolutionPath, ct).ConfigureAwait(false);
            if (!reloaded.IsSuccess || reloaded.Value is null)
            {
                var err = reloaded.Error ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Failed to reload manifest after apply.");
                _jobs.Publish(jobId, "error", new { errorCode = err.Code, message = err.Message });
                return OperationResult<ApplyProjectResponse>.Failure(err);
            }

            await _registry.SyncFromManifestAsync(projectId, reloaded.Value.Manifest, ct).ConfigureAwait(false);

            if (request.RunBuild)
            {
                var buildRes = await _dotnetRunner.BuildAsync(info.SolutionPath, "Debug", progress, ct).ConfigureAwait(false);
                if (!buildRes.IsSuccess)
                {
                    var err = buildRes.Error ?? new GenerationError(ErrorCodes.PROCESS_FAILED, "dotnet build failed.");
                    _jobs.Publish(jobId, "error", new { errorCode = err.Code, message = err.Message });
                    return OperationResult<ApplyProjectResponse>.Failure(err);
                }
            }

            if (request.CreateMigration)
            {
                var args = BuildEfMigrationArguments(info, request.MigrationName!.Trim(), null, null);
                var migRes = await _dotnetRunner.RunAsync(info.SolutionPath, args, progress, ct).ConfigureAwait(false);
                if (!migRes.IsSuccess)
                {
                    var err = migRes.Error ?? new GenerationError(ErrorCodes.PROCESS_FAILED, "dotnet ef migrations add failed.");
                    _jobs.Publish(jobId, "error", new { errorCode = err.Code, message = err.Message });
                    return OperationResult<ApplyProjectResponse>.Failure(err);
                }
            }

            var createdFiles = createdSet.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var modifiedFiles = modifiedSet.OrderBy(p => p, StringComparer.Ordinal).ToArray();

            _jobs.Publish(jobId, "done", new
            {
                jobId,
                planHash = planContext.Response.PlanHash,
                createdCount = createdFiles.Length,
                modifiedCount = modifiedFiles.Length
            });

            return OperationResult<ApplyProjectResponse>.Success(new ApplyProjectResponse(
                jobId,
                planContext.Response.PlanHash,
                createdFiles,
                modifiedFiles,
                reloaded.Value.Manifest));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OperationResult<DbImportApplyResponse>> ApplyDbImportAsync(
        int projectId,
        DbImportApplyRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = await ResolveWorkspaceAsync(projectId, ct).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResult<DbImportApplyResponse>.Failure(
                resolved.Error ?? new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Project not found."));
        }

        var (_, info) = resolved.Value;
        var manifestResult = await _manifestStore.LoadAsync(info.SolutionPath, ct).ConfigureAwait(false);
        if (!manifestResult.IsSuccess || manifestResult.Value is null)
        {
            return OperationResult<DbImportApplyResponse>.Failure(
                manifestResult.Error ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Manifest could not be loaded."));
        }

        DatabaseSchemaSnapshot snapshot;
        if (request.Snapshot is not null)
        {
            snapshot = request.Snapshot;
        }
        else if (request.Provider.HasValue)
        {
            if (request.Provider.Value == DatabaseProviderKind.Sqlite)
            {
                var sqliteCheck = _pathGuard.ValidateSqlitePathIfLocal(request.Parameters?.ServerOrPath, request.ConnectionString);
                if (!sqliteCheck.IsSuccess)
                {
                    return OperationResult<DbImportApplyResponse>.Failure(sqliteCheck.Error!);
                }
            }

            var introspector = _introspectorFactory.GetIntrospector(request.Provider.Value);
            var introResult = await introspector.IntrospectAsync(
                new DatabaseIntrospectRequest(request.Provider.Value, request.ConnectionString, request.Parameters, request.Filters),
                ct).ConfigureAwait(false);
            if (!introResult.IsSuccess || introResult.Value is null)
            {
                return OperationResult<DbImportApplyResponse>.Failure(
                    introResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Database introspection failed."));
            }

            snapshot = introResult.Value;
        }
        else
        {
            return OperationResult<DbImportApplyResponse>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "Either snapshot or provider connection details must be provided.");
        }

        var filteredResult = FilterAndNormalizeSnapshot(snapshot, request.SelectedTables, request.TableEntityOverrides);
        if (!filteredResult.IsSuccess || filteredResult.Value is null)
        {
            return OperationResult<DbImportApplyResponse>.Failure(filteredResult.Error!);
        }

        var mapper = MapperKindParser.TryParse(manifestResult.Value.Manifest.Mapper, out var parsedMapper)
            ? parsedMapper
            : info.DetectedMapper;

        var importedManifest = filteredResult.Value.ToProjectManifest(
            info.SolutionName,
            mapper,
            info.SolutionFormat);

        var updatedDocResult = await _manifestStore.UpdateAtomicAsync(
            info.SolutionPath,
            current => MergeImportedManifest(current, importedManifest),
            ct).ConfigureAwait(false);

        if (!updatedDocResult.IsSuccess || updatedDocResult.Value is null)
        {
            return OperationResult<DbImportApplyResponse>.Failure(
                updatedDocResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to update manifest with imported schema."));
        }

        await _registry.SyncFromManifestAsync(projectId, updatedDocResult.Value.Manifest, ct).ConfigureAwait(false);

        if (request.ApplyImmediately)
        {
            var applyRes = await ApplyAsync(projectId, new ApplyProjectRequest(), ct).ConfigureAwait(false);
            if (!applyRes.IsSuccess || applyRes.Value is null)
            {
                return OperationResult<DbImportApplyResponse>.Failure(
                    applyRes.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed applying imported schema."));
            }

            var latestDoc = await _manifestStore.LoadAsync(info.SolutionPath, ct).ConfigureAwait(false);
            var finalManifest = latestDoc.IsSuccess && latestDoc.Value is not null
                ? latestDoc.Value.Manifest
                : applyRes.Value.Manifest;
            var finalHash = latestDoc.IsSuccess && latestDoc.Value is not null
                ? latestDoc.Value.ContentHash
                : updatedDocResult.Value.ContentHash;

            return OperationResult<DbImportApplyResponse>.Success(new DbImportApplyResponse(
                finalManifest,
                finalHash,
                applyRes.Value.JobId,
                null));
        }

        var planRes = await ComputePlanAsync(projectId, ct).ConfigureAwait(false);
        return OperationResult<DbImportApplyResponse>.Success(new DbImportApplyResponse(
            updatedDocResult.Value.Manifest,
            updatedDocResult.Value.ContentHash,
            null,
            planRes.IsSuccess ? planRes.Value : null));
    }

    public static IReadOnlyList<string> BuildEfMigrationArguments(
        SolutionInfo info,
        string migrationName,
        string? efProject,
        string? startupProject)
    {
        var defaultEf = Path.Combine(info.SolutionName, $"{info.SolutionName}.EntityFramework");
        var defaultStartup = Path.Combine(info.SolutionName, $"{info.SolutionName}.AppApi");

        return
        [
            "ef",
            "migrations",
            "add",
            migrationName,
            "--project",
            string.IsNullOrWhiteSpace(efProject) ? defaultEf : efProject.Trim(),
            "--startup-project",
            string.IsNullOrWhiteSpace(startupProject) ? defaultStartup : startupProject.Trim()
        ];
    }

    private async Task<OperationResult<ComputedPlanContext>> ComputeInternalPlanAsync(int projectId, CancellationToken ct)
    {
        var resolved = await ResolveWorkspaceAsync(projectId, ct).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResult<ComputedPlanContext>.Failure(
                resolved.Error ?? new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Project not found."));
        }

        var (_, info) = resolved.Value;
        var loaded = await _manifestStore.LoadAsync(info.SolutionPath, ct).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return OperationResult<ComputedPlanContext>.Failure(
                loaded.Error ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Manifest could not be loaded."));
        }

        var manifestDoc = loaded.Value;
        var manifest = manifestDoc.Manifest;
        var mapper = MapperKindParser.TryParse(manifest.Mapper, out var parsedMapper)
            ? parsedMapper
            : info.DetectedMapper;
        var modelSuffix = mapper == MapperKind.Mapster ? ".g.cs" : ".cs";

        var pendingEntities = manifest.Entities.Where(e => !e.Applied).ToList();
        var pendingRelations = manifest.Relations.Where(r => !r.Applied).ToList();

        var pendingRelationFkTargets = new HashSet<string>(
            pendingRelations.Select(r => $"{r.ChildEntity}.{r.ForeignKey}"),
            StringComparer.OrdinalIgnoreCase);

        var pendingProperties = new List<(string EntityName, ManifestProperty Property)>();
        foreach (var entity in manifest.Entities)
        {
            foreach (var prop in entity.Properties)
            {
                if (prop.Applied)
                {
                    continue;
                }

                if (pendingRelationFkTargets.Contains($"{entity.Name}.{prop.Name}"))
                {
                    continue;
                }

                pendingProperties.Add((entity.Name, prop));
            }
        }

        var virtualFiles = new Dictionary<string, VirtualFileState>(PathComparer);
        var dbContextRewriter = new DbContextSyntaxRewriter();
        var modelRewriter = new ModelSyntaxRewriter();
        var entityRewriter = new EntitySyntaxRewriter();
        var configRewriter = new EntityConfigSyntaxRewriter();

        var dbContextPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.EntityFramework", "ApplicationDbContext.cs");
        var mappingProfilePath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "MappingProfile.cs");

        // Phase 1: Stage pending entities in memory
        foreach (var entity in pendingEntities)
        {
            if (!NamingHelper.IsValidIdentifier(entity.Name))
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid entity name '{entity.Name}'.");
            }

            var normalizedPk = DataTypes.Normalize(entity.PkType);
            if (normalizedPk is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Unsupported primary key type '{entity.PkType}'.");
            }

            var paths = GetEntityPaths(info, entity.Name, modelSuffix);
            if (File.Exists(paths.EntityPath) || virtualFiles.ContainsKey(paths.EntityPath))
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.ENTITY_EXISTS,
                    $"Entity '{entity.Name}' already exists at '{paths.EntityPath}'.");
            }

            var entitySetName = NamingHelper.Pluralize(entity.Name);
            var templateModel = new TemplateModel(
                info.SolutionName,
                entity.Name,
                NamingHelper.ToCamelCase(entity.Name),
                entitySetName,
                normalizedPk,
                mapper,
                Properties: [],
                Relations: []);

            var templatesToRender = new List<(string Path, string TemplateName)>
            {
                (paths.ModifyModelPath, "ModifyModel"),
                (paths.ReadModelPath, "ReadModel"),
                (paths.ValidatorPath, "Validator"),
                (paths.EntityPath, "Entity"),
                (paths.EntityConfigPath, "EntityConfig"),
                (paths.ServicePath, "Service")
            };
            if (entity.HasApi)
            {
                templatesToRender.Add((paths.ControllerPath, "Controller"));
            }

            foreach (var (targetPath, templateName) in templatesToRender)
            {
                var rendered = _templateRenderer.RenderTemplate(info.SolutionPath, mapper, templateName, templateModel);
                if (!rendered.IsSuccess || rendered.Value is null)
                {
                    return OperationResult<ComputedPlanContext>.Failure(
                        rendered.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, $"Failed rendering '{templateName}'."));
                }

                virtualFiles[targetPath] = new VirtualFileState(FileChangeKind.Create, rendered.Value);
            }

            var dbContextText = await ReadVirtualOrDiskTextAsync(dbContextPath, virtualFiles, ct).ConfigureAwait(false);
            if (dbContextText is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.IO_ERROR,
                    $"ApplicationDbContext not found at '{dbContextPath}'.");
            }

            var dbSetRendered = _templateRenderer.RenderTemplate(info.SolutionPath, mapper, "DbSetEntry", templateModel);
            if (!dbSetRendered.IsSuccess || dbSetRendered.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    dbSetRendered.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, "Failed rendering 'DbSetEntry'."));
            }

            var configEntryRendered = _templateRenderer.RenderTemplate(info.SolutionPath, mapper, "ConfigEntry", templateModel);
            if (!configEntryRendered.IsSuccess || configEntryRendered.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    configEntryRendered.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, "Failed rendering 'ConfigEntry'."));
            }

            var updatedDbContext = dbContextRewriter.RegisterEntityInDbContext(
                dbContextText,
                entity.Name,
                entitySetName,
                dbSetRendered.Value,
                configEntryRendered.Value);
            if (!updatedDbContext.IsSuccess || updatedDbContext.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    updatedDbContext.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, "Failed rewriting ApplicationDbContext."));
            }

            SetVirtualModifiedOrCreated(virtualFiles, dbContextPath, updatedDbContext.Value);

            if (mapper == MapperKind.AutoMapper)
            {
                var profileText = await ReadVirtualOrDiskTextAsync(mappingProfilePath, virtualFiles, ct).ConfigureAwait(false);
                if (profileText is null)
                {
                    return OperationResult<ComputedPlanContext>.Failure(
                        ErrorCodes.IO_ERROR,
                        $"MappingProfile not found at '{mappingProfilePath}'.");
                }

                var configRendered = _templateRenderer.RenderTemplate(info.SolutionPath, mapper, "Config", templateModel);
                if (!configRendered.IsSuccess || configRendered.Value is null)
                {
                    return OperationResult<ComputedPlanContext>.Failure(
                        configRendered.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, "Failed rendering 'Config'."));
                }

                var updatedProfile = dbContextRewriter.RegisterEntityInMappingProfile(
                    profileText,
                    entity.Name,
                    configRendered.Value);
                if (!updatedProfile.IsSuccess || updatedProfile.Value is null)
                {
                    return OperationResult<ComputedPlanContext>.Failure(
                        updatedProfile.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, "Failed rewriting MappingProfile."));
                }

                SetVirtualModifiedOrCreated(virtualFiles, mappingProfilePath, updatedProfile.Value);
            }
        }

        // Phase 2: Stage pending non-FK properties on virtual/disk files
        foreach (var (entityName, prop) in pendingProperties)
        {
            if (!NamingHelper.IsValidIdentifier(entityName) || !NamingHelper.IsValidIdentifier(prop.Name))
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid identifier for '{entityName}.{prop.Name}'.");
            }

            var normalizedType = DataTypes.Normalize(prop.DataType);
            if (normalizedType is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Unsupported data type '{prop.DataType}'.");
            }

            var paths = GetEntityPaths(info, entityName, modelSuffix);
            var modifyText = await ReadVirtualOrDiskTextAsync(paths.ModifyModelPath, virtualFiles, ct).ConfigureAwait(false);
            var readText = await ReadVirtualOrDiskTextAsync(paths.ReadModelPath, virtualFiles, ct).ConfigureAwait(false);
            var entityText = await ReadVirtualOrDiskTextAsync(paths.EntityPath, virtualFiles, ct).ConfigureAwait(false);

            if (modifyText is null || readText is null || entityText is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.ENTITY_NOT_FOUND,
                    $"Entity '{entityName}' or its models could not be found.");
            }

            if (entityRewriter.HasProperty(entityText, prop.Name) || modelRewriter.HasProperty(modifyText, prop.Name))
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.PROPERTY_EXISTS,
                    $"Property '{prop.Name}' already exists on entity '{entityName}'.");
            }

            var updatedModify = modelRewriter.AddProperty(modifyText, prop.Name, normalizedType, isNullable: prop.Nullable);
            if (!updatedModify.IsSuccess || updatedModify.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(updatedModify.Error!);
            }

            var updatedRead = modelRewriter.AddProperty(readText, prop.Name, normalizedType, isNullable: prop.Nullable);
            if (!updatedRead.IsSuccess || updatedRead.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(updatedRead.Error!);
            }

            var entityWithProp = entityRewriter.AddProperty(
                entityText,
                entityName,
                prop.Name,
                normalizedType,
                isNullable: prop.Nullable,
                isVirtual: false,
                isPrivateSetter: true);
            if (!entityWithProp.IsSuccess || entityWithProp.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(entityWithProp.Error!);
            }

            var entityWithAssign = entityRewriter.AddConstructorAndUpdateAssignment(
                entityWithProp.Value,
                entityName,
                $"{prop.Name} = dto.{prop.Name};");
            if (!entityWithAssign.IsSuccess || entityWithAssign.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(entityWithAssign.Error!);
            }

            SetVirtualModifiedOrCreated(virtualFiles, paths.ModifyModelPath, updatedModify.Value);
            SetVirtualModifiedOrCreated(virtualFiles, paths.ReadModelPath, updatedRead.Value);
            SetVirtualModifiedOrCreated(virtualFiles, paths.EntityPath, entityWithAssign.Value);

            var configLine = BuildPropertyConfigLine(normalizedType, prop.Name);
            if (!string.IsNullOrEmpty(configLine))
            {
                var configText = await ReadVirtualOrDiskTextAsync(paths.EntityConfigPath, virtualFiles, ct).ConfigureAwait(false);
                if (configText is not null)
                {
                    var updatedConfig = configRewriter.AddConfigurationStatement(configText, configLine);
                    if (!updatedConfig.IsSuccess || updatedConfig.Value is null)
                    {
                        return OperationResult<ComputedPlanContext>.Failure(updatedConfig.Error!);
                    }

                    SetVirtualModifiedOrCreated(virtualFiles, paths.EntityConfigPath, updatedConfig.Value);
                }
            }
        }

        // Phase 3: Stage pending relations on virtual/disk files
        foreach (var rel in pendingRelations)
        {
            if (!NamingHelper.IsValidIdentifier(rel.ParentEntity)
                || !NamingHelper.IsValidIdentifier(rel.ChildEntity)
                || !NamingHelper.IsValidIdentifier(rel.ForeignKey))
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid relation identifiers on '{rel.Name}'.");
            }

            var normalizedFkType = DataTypes.Normalize(rel.ForeignKeyType);
            if (normalizedFkType is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Unsupported foreign key data type '{rel.ForeignKeyType}'.");
            }

            var parentPaths = GetEntityPaths(info, rel.ParentEntity, modelSuffix);
            var childPaths = GetEntityPaths(info, rel.ChildEntity, modelSuffix);
            var childrenSetName = NamingHelper.Pluralize(rel.ChildEntity);

            var parentModifyText = await ReadVirtualOrDiskTextAsync(parentPaths.ModifyModelPath, virtualFiles, ct).ConfigureAwait(false);
            var childModifyText = await ReadVirtualOrDiskTextAsync(childPaths.ModifyModelPath, virtualFiles, ct).ConfigureAwait(false);
            var childReadText = await ReadVirtualOrDiskTextAsync(childPaths.ReadModelPath, virtualFiles, ct).ConfigureAwait(false);
            var parentEntityText = await ReadVirtualOrDiskTextAsync(parentPaths.EntityPath, virtualFiles, ct).ConfigureAwait(false);
            var childEntityText = await ReadVirtualOrDiskTextAsync(childPaths.EntityPath, virtualFiles, ct).ConfigureAwait(false);
            var parentConfigText = await ReadVirtualOrDiskTextAsync(parentPaths.EntityConfigPath, virtualFiles, ct).ConfigureAwait(false);

            if (parentModifyText is null || childModifyText is null || childReadText is null
                || parentEntityText is null || childEntityText is null || parentConfigText is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.ENTITY_NOT_FOUND,
                    $"Parent entity '{rel.ParentEntity}' or child entity '{rel.ChildEntity}' files could not be found.");
            }

            if (configRewriter.HasRelationConfiguration(parentConfigText, childrenSetName, rel.ParentEntity, rel.ForeignKey))
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.RELATION_EXISTS,
                    $"Relation '{rel.ParentEntity} -> {rel.ChildEntity}' via '{rel.ForeignKey}' already exists.");
            }

            if (entityRewriter.HasProperty(childEntityText, rel.ForeignKey)
                || modelRewriter.HasProperty(childModifyText, rel.ForeignKey)
                || modelRewriter.HasProperty(childReadText, rel.ForeignKey))
            {
                return OperationResult<ComputedPlanContext>.Failure(
                    ErrorCodes.PROPERTY_EXISTS,
                    $"Property '{rel.ForeignKey}' already exists on entity '{rel.ChildEntity}'.");
            }

            var parentModifyResult = modelRewriter.AddProperty(parentModifyText, childrenSetName, $"List<{rel.ChildEntity}>", isNullable: true, isVirtual: true);
            if (!parentModifyResult.IsSuccess || parentModifyResult.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(parentModifyResult.Error!);
            }

            var childModifyFk = modelRewriter.AddProperty(childModifyText, rel.ForeignKey, normalizedFkType, isNullable: true, isVirtual: false);
            if (!childModifyFk.IsSuccess || childModifyFk.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(childModifyFk.Error!);
            }

            var childModifyNav = modelRewriter.AddProperty(childModifyFk.Value, rel.ParentEntity, rel.ParentEntity, isNullable: true, isVirtual: true);
            if (!childModifyNav.IsSuccess || childModifyNav.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(childModifyNav.Error!);
            }

            var childReadResult = modelRewriter.AddProperty(childReadText, rel.ForeignKey, normalizedFkType, isNullable: true, isVirtual: false);
            if (!childReadResult.IsSuccess || childReadResult.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(childReadResult.Error!);
            }

            var parentEntityProp = entityRewriter.AddProperty(
                parentEntityText,
                rel.ParentEntity,
                childrenSetName,
                $"ICollection<{rel.ChildEntity}>",
                isNullable: true,
                isVirtual: true,
                isPrivateSetter: false);
            if (!parentEntityProp.IsSuccess || parentEntityProp.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(parentEntityProp.Error!);
            }

            var parentEntityAssign = entityRewriter.AddConstructorAndUpdateAssignment(
                parentEntityProp.Value,
                rel.ParentEntity,
                $"{childrenSetName} ??= new List<{rel.ChildEntity}>();");
            if (!parentEntityAssign.IsSuccess || parentEntityAssign.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(parentEntityAssign.Error!);
            }

            var childEntityFk = entityRewriter.AddProperty(
                childEntityText,
                rel.ChildEntity,
                rel.ForeignKey,
                normalizedFkType,
                isNullable: true,
                isVirtual: false,
                isPrivateSetter: true);
            if (!childEntityFk.IsSuccess || childEntityFk.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(childEntityFk.Error!);
            }

            var childEntityNav = entityRewriter.AddProperty(
                childEntityFk.Value,
                rel.ChildEntity,
                rel.ParentEntity,
                rel.ParentEntity,
                isNullable: true,
                isVirtual: true,
                isPrivateSetter: true);
            if (!childEntityNav.IsSuccess || childEntityNav.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(childEntityNav.Error!);
            }

            var childEntityAssign = entityRewriter.AddConstructorAndUpdateAssignment(
                childEntityNav.Value,
                rel.ChildEntity,
                $"{rel.ForeignKey} = dto.{rel.ForeignKey};");
            if (!childEntityAssign.IsSuccess || childEntityAssign.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(childEntityAssign.Error!);
            }

            var parentConfigLine =
                $"builder.HasMany(e => e.{childrenSetName}).WithOne(e => e.{rel.ParentEntity}).HasForeignKey(e => e.{rel.ForeignKey}).OnDelete(DeleteBehavior.Cascade);";
            var parentConfigResult = configRewriter.AddConfigurationStatement(parentConfigText, parentConfigLine);
            if (!parentConfigResult.IsSuccess || parentConfigResult.Value is null)
            {
                return OperationResult<ComputedPlanContext>.Failure(parentConfigResult.Error!);
            }

            SetVirtualModifiedOrCreated(virtualFiles, parentPaths.ModifyModelPath, parentModifyResult.Value);
            SetVirtualModifiedOrCreated(virtualFiles, childPaths.ModifyModelPath, childModifyNav.Value);
            SetVirtualModifiedOrCreated(virtualFiles, childPaths.ReadModelPath, childReadResult.Value);
            SetVirtualModifiedOrCreated(virtualFiles, parentPaths.EntityPath, parentEntityAssign.Value);
            SetVirtualModifiedOrCreated(virtualFiles, childPaths.EntityPath, childEntityAssign.Value);
            SetVirtualModifiedOrCreated(virtualFiles, parentPaths.EntityConfigPath, parentConfigResult.Value);
        }

        var stagedChanges = virtualFiles
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => new StagedFileChange(kvp.Key, kvp.Value.Kind, kvp.Value.Content))
            .ToArray();

        var planResult = await _diffEngine.ComputePlanAsync(info.SolutionPath, stagedChanges, ct).ConfigureAwait(false);
        if (!planResult.IsSuccess || planResult.Value is null)
        {
            return OperationResult<ComputedPlanContext>.Failure(
                planResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed computing dry-run execution plan."));
        }

        var executionPlan = planResult.Value;
        var combinedHash = ComputeCombinedPlanHash(manifestDoc.ContentHash, executionPlan.PlanHash);
        var fileDtos = executionPlan.FileChanges
            .Select(d => new PlanFileDiffDto(
                d.RelativePath,
                d.FullPath,
                d.Kind.ToString(),
                d.UnifiedDiff,
                d.OriginalHash,
                d.NewHash))
            .ToArray();

        var response = new PlanDiffResponse(
            combinedHash,
            manifestDoc.ContentHash,
            fileDtos.Length > 0,
            fileDtos);

        return OperationResult<ComputedPlanContext>.Success(new ComputedPlanContext(
            info,
            mapper,
            manifest,
            pendingEntities,
            pendingProperties,
            pendingRelations,
            response));
    }

    private static OperationResult<DatabaseSchemaSnapshot> FilterAndNormalizeSnapshot(
        DatabaseSchemaSnapshot snapshot,
        IReadOnlyList<string>? selectedTables,
        IReadOnlyDictionary<string, string>? tableEntityOverrides)
    {
        HashSet<string>? selectedSet = selectedTables is { Count: > 0 }
            ? new HashSet<string>(selectedTables.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase)
            : null;

        var entityNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var filteredTables = new List<IntrospectedTable>();

        foreach (var table in snapshot.Tables)
        {
            if (selectedSet is not null
                && !selectedSet.Contains(table.TableName)
                && !selectedSet.Contains(table.EntityName))
            {
                continue;
            }

            var targetEntityName = table.EntityName;
            if (tableEntityOverrides is not null)
            {
                foreach (var kvp in tableEntityOverrides)
                {
                    if ((string.Equals(kvp.Key, table.TableName, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(kvp.Key, table.EntityName, StringComparison.OrdinalIgnoreCase))
                        && !string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        targetEntityName = kvp.Value.Trim();
                        break;
                    }
                }
            }

            if (!NamingHelper.IsValidIdentifier(targetEntityName))
            {
                return OperationResult<DatabaseSchemaSnapshot>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid target entity name '{targetEntityName}' for table '{table.TableName}'.");
            }

            entityNameMap[table.EntityName] = targetEntityName;
            filteredTables.Add(table with { EntityName = targetEntityName });
        }

        var keptEntities = new HashSet<string>(filteredTables.Select(t => t.EntityName), StringComparer.OrdinalIgnoreCase);
        var filteredRelations = new List<IntrospectedRelation>();

        foreach (var rel in snapshot.Relations)
        {
            var mappedParent = entityNameMap.GetValueOrDefault(rel.ParentEntity, rel.ParentEntity);
            var mappedChild = entityNameMap.GetValueOrDefault(rel.ChildEntity, rel.ChildEntity);
            if (!keptEntities.Contains(mappedParent) || !keptEntities.Contains(mappedChild))
            {
                continue;
            }

            var relName = $"{mappedParent}_{mappedChild}_{rel.ForeignKey}";
            filteredRelations.Add(rel with
            {
                Name = relName,
                ParentEntity = mappedParent,
                ChildEntity = mappedChild
            });
        }

        return OperationResult<DatabaseSchemaSnapshot>.Success(
            snapshot with { Tables = filteredTables, Relations = filteredRelations });
    }

    private static OperationResult<ProjectManifest> MergeImportedManifest(
        ProjectManifest current,
        ProjectManifest imported)
    {
        var entities = current.Entities
            .Select(e => e with { Properties = [.. e.Properties] })
            .ToList();

        foreach (var importedEntity in imported.Entities)
        {
            var existingIdx = entities.FindIndex(e => string.Equals(e.Name, importedEntity.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIdx < 0)
            {
                entities.Add(importedEntity with { Applied = false, Properties = [.. importedEntity.Properties] });
                continue;
            }

            var existing = entities[existingIdx];
            var mergedProps = new List<ManifestProperty>(existing.Properties);
            foreach (var importedProp in importedEntity.Properties)
            {
                if (!mergedProps.Any(p => string.Equals(p.Name, importedProp.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    mergedProps.Add(importedProp with { Applied = false });
                }
            }

            entities[existingIdx] = existing with { Properties = mergedProps };
        }

        var relations = current.Relations.ToList();
        foreach (var importedRel in imported.Relations)
        {
            var exists = relations.Any(r =>
                string.Equals(r.Name, importedRel.Name, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(r.ParentEntity, importedRel.ParentEntity, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ChildEntity, importedRel.ChildEntity, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ForeignKey, importedRel.ForeignKey, StringComparison.OrdinalIgnoreCase)));

            if (!exists)
            {
                relations.Add(importedRel with { Applied = false });
            }
        }

        return OperationResult<ProjectManifest>.Success(current with
        {
            Entities = entities,
            Relations = relations,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static async Task<string?> ReadVirtualOrDiskTextAsync(
        string path,
        Dictionary<string, VirtualFileState> virtualFiles,
        CancellationToken ct)
    {
        if (virtualFiles.TryGetValue(path, out var virtualFile))
        {
            return virtualFile.Content;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var preamble = Encoding.UTF8.Preamble;
        var span = bytes.AsSpan();
        if (span.StartsWith(preamble))
        {
            span = span[preamble.Length..];
        }

        return Encoding.UTF8.GetString(span);
    }

    private static void SetVirtualModifiedOrCreated(
        Dictionary<string, VirtualFileState> virtualFiles,
        string path,
        string content)
    {
        if (virtualFiles.TryGetValue(path, out var existing))
        {
            virtualFiles[path] = existing with { Content = content };
        }
        else
        {
            virtualFiles[path] = new VirtualFileState(
                File.Exists(path) ? FileChangeKind.Modify : FileChangeKind.Create,
                content);
        }
    }

    private static EntityPaths GetEntityPaths(SolutionInfo info, string entityName, string modelSuffix)
    {
        var entitySetName = NamingHelper.Pluralize(entityName);
        return new EntityPaths(
            ModifyModelPath: Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Models", $"{entityName}ModifyModel{modelSuffix}"),
            ReadModelPath: Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Models", $"{entityName}ReadModel{modelSuffix}"),
            ValidatorPath: Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Validations", $"{entityName}Validator.cs"),
            EntityPath: Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Database", "Tables", $"{entityName}.cs"),
            EntityConfigPath: Path.Combine(info.ProjectsPath, $"{info.SolutionName}.EntityFramework", "EntityConfigurations", $"{entityName}Config.cs"),
            ServicePath: Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Services", $"{entityName}Service.cs"),
            ControllerPath: Path.Combine(info.ProjectsPath, $"{info.SolutionName}.AppApi", "Controllers", "v1", $"{entitySetName}Controller.cs"));
    }

    private static string BuildPropertyConfigLine(string normalizedType, string propertyName) => normalizedType switch
    {
        "string" => $"builder.Property(e => e.{propertyName}).HasMaxLength(255);",
        "decimal" => $"builder.Property(e => e.{propertyName}).HasColumnType(\"decimal(9,3)\");",
        "DateTime" => $"builder.Property(e => e.{propertyName}).HasColumnType(\"datetime\");",
        "Guid" => $"builder.Property(e => e.{propertyName}).HasDefaultValue(\"newid()\");",
        "byte[]" => $"builder.Property(e => e.{propertyName}).HasColumnType(\"varbinary(max)\");",
        "TimeSpan" => $"builder.Property(e => e.{propertyName}).HasColumnType(\"time\");",
        _ => string.Empty
    };

    private static string ComputeCombinedPlanHash(string manifestHash, string filesPlanHash) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Utf8NoBom.GetBytes($"{manifestHash}\0{filesPlanHash}")));

    private static void RecordPaths(
        GenerationSummary summary,
        HashSet<string> createdSet,
        HashSet<string> modifiedSet)
    {
        foreach (var path in summary.CreatedFiles)
        {
            createdSet.Add(path);
            modifiedSet.Remove(path);
        }

        foreach (var path in summary.ModifiedFiles)
        {
            if (!createdSet.Contains(path))
            {
                modifiedSet.Add(path);
            }
        }
    }

    private sealed record VirtualFileState(FileChangeKind Kind, string Content);

    private sealed record EntityPaths(
        string ModifyModelPath,
        string ReadModelPath,
        string ValidatorPath,
        string EntityPath,
        string EntityConfigPath,
        string ServicePath,
        string ControllerPath);

    private sealed record ComputedPlanContext(
        SolutionInfo SolutionInfo,
        MapperKind Mapper,
        ProjectManifest Manifest,
        List<ManifestEntity> PendingEntities,
        List<(string EntityName, ManifestProperty Property)> PendingProperties,
        List<ManifestRelation> PendingRelations,
        PlanDiffResponse Response);

    private sealed class InlineProgress(Action<GenerationEvent> handler) : IProgress<GenerationEvent>
    {
        public void Report(GenerationEvent value) => handler(value);
    }
}
