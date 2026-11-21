using Microsoft.Extensions.Logging;
using Mma.Cli.Core.CodeGen;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Roslyn;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Services;

public sealed class RelationService : IRelationService
{
    private readonly ISolutionLocator _solutionLocator;
    private readonly ILogger<RelationService>? _logger;
    private readonly ISolutionLockManager _lockManager;
    private readonly IManifestStore _manifestStore;
    private readonly IDryRunDiffEngine _diffEngine;

    public RelationService(
        ISolutionLocator solutionLocator,
        ILogger<RelationService>? logger = null,
        ISolutionLockManager? lockManager = null,
        IManifestStore? manifestStore = null,
        IDryRunDiffEngine? diffEngine = null)
    {
        ArgumentNullException.ThrowIfNull(solutionLocator);

        _solutionLocator = solutionLocator;
        _logger = logger;
        _lockManager = lockManager ?? new SolutionLockManager();
        _manifestStore = manifestStore ?? new ManifestStore(_lockManager, solutionLocator);
        _diffEngine = diffEngine ?? new DryRunDiffEngine();
    }

    public async Task<OperationResult<GenerationSummary>> ApplyAsync(
        AddRelationRequest request,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Relation operation was cancelled.");
        }

        ServiceFileHelper.Report(
            progress,
            GenerationStage.Validating,
            $"Validating relation '{request.ParentEntity} -> {request.ChildEntity}'.");

        if (!NamingHelper.IsValidIdentifier(request.ParentEntity))
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid parent entity name '{request.ParentEntity}'.");
        }

        if (!NamingHelper.IsValidIdentifier(request.ChildEntity))
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid child entity name '{request.ChildEntity}'.");
        }

        if (!NamingHelper.IsValidIdentifier(request.ForeignKey))
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid foreign key name '{request.ForeignKey}'.");
        }

        var normalizedFkType = DataTypes.Normalize(request.ForeignKeyType);
        if (normalizedFkType is null)
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Unsupported foreign key data type '{request.ForeignKeyType}'.");
        }

        if (!ServiceFileHelper.TryResolveExistingSolutionPath(request.SolutionPath, out var fullSolutionPath, out var pathError))
        {
            return OperationResult<GenerationSummary>.Failure(pathError!);
        }

        var parentEntity = request.ParentEntity;
        var childEntity = request.ChildEntity;
        var foreignKey = request.ForeignKey;
        var childrenSetName = NamingHelper.Pluralize(childEntity);

        IAsyncDisposable? lockHandle = null;
        try
        {
            if (!request.DryRun)
            {
                lockHandle = await _lockManager.AcquireAsync(fullSolutionPath, ct: ct).ConfigureAwait(false);
            }

            var located = _solutionLocator.Locate(fullSolutionPath);
            if (!located.IsSuccess || located.Value is null)
            {
                return OperationResult<GenerationSummary>.Failure(
                    located.Error ?? new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Solution could not be located."));
            }

            var info = located.Value;
            var modelSuffix = info.DetectedMapper == MapperKind.Mapster ? ".g.cs" : ".cs";

            var parentModifyModelPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Models", $"{parentEntity}ModifyModel{modelSuffix}");
            var childModifyModelPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Models", $"{childEntity}ModifyModel{modelSuffix}");
            var childReadModelPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Models", $"{childEntity}ReadModel{modelSuffix}");
            var parentEntityPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Database", "Tables", $"{parentEntity}.cs");
            var childEntityPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Database", "Tables", $"{childEntity}.cs");
            var parentConfigPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.EntityFramework", "EntityConfigurations", $"{parentEntity}Config.cs");

            if (!File.Exists(parentEntityPath) || !File.Exists(childEntityPath)
                || !File.Exists(parentModifyModelPath) || !File.Exists(childModifyModelPath)
                || !File.Exists(childReadModelPath) || !File.Exists(parentConfigPath))
            {
                return OperationResult<GenerationSummary>.Failure(
                    ErrorCodes.ENTITY_NOT_FOUND,
                    $"Parent entity '{parentEntity}' or child entity '{childEntity}' files could not be found.");
            }

            var parentModifyText = await ServiceFileHelper.ReadUtf8TextAsync(parentModifyModelPath, ct).ConfigureAwait(false);
            var childModifyText = await ServiceFileHelper.ReadUtf8TextAsync(childModifyModelPath, ct).ConfigureAwait(false);
            var childReadText = await ServiceFileHelper.ReadUtf8TextAsync(childReadModelPath, ct).ConfigureAwait(false);
            var parentEntityText = await ServiceFileHelper.ReadUtf8TextAsync(parentEntityPath, ct).ConfigureAwait(false);
            var childEntityText = await ServiceFileHelper.ReadUtf8TextAsync(childEntityPath, ct).ConfigureAwait(false);
            var parentConfigText = await ServiceFileHelper.ReadUtf8TextAsync(parentConfigPath, ct).ConfigureAwait(false);

            foreach (var (path, text) in new[]
            {
                (parentModifyModelPath, parentModifyText),
                (childModifyModelPath, childModifyText),
                (childReadModelPath, childReadText),
                (parentEntityPath, parentEntityText),
                (childEntityPath, childEntityText),
                (parentConfigPath, parentConfigText)
            })
            {
                var syntaxCheck = RoslynSyntaxHelper.ParseAndValidate(text, path);
                if (!syntaxCheck.IsSuccess)
                {
                    return OperationResult<GenerationSummary>.Failure(syntaxCheck.Error!);
                }
            }

            var modelRewriter = new ModelSyntaxRewriter();
            var entityRewriter = new EntitySyntaxRewriter();
            var configRewriter = new EntityConfigSyntaxRewriter();

            string updatedParentModify;
            string updatedChildModify;
            string updatedChildRead;
            string updatedParentEntity;
            string updatedChildEntity;
            string updatedParentConfig;

            if (request.Remove)
            {
                var relationExists = configRewriter.HasRelationConfiguration(parentConfigText, childrenSetName, parentEntity, foreignKey)
                                     || entityRewriter.HasProperty(childEntityText, foreignKey);
                if (!relationExists)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.RELATION_NOT_FOUND,
                        $"Relation '{parentEntity} -> {childEntity}' via '{foreignKey}' was not found.");
                }

                var parentModifyResult = modelRewriter.RemoveProperty(parentModifyText, childrenSetName, requireVirtual: true);
                if (!parentModifyResult.IsSuccess || parentModifyResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(parentModifyResult.Error!);
                }
                updatedParentModify = parentModifyResult.Value;

                var childModifyFkResult = modelRewriter.RemoveProperty(childModifyText, foreignKey);
                if (!childModifyFkResult.IsSuccess || childModifyFkResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childModifyFkResult.Error!);
                }

                var childModifyNavResult = modelRewriter.RemoveProperty(childModifyFkResult.Value, parentEntity, requireVirtual: true);
                if (!childModifyNavResult.IsSuccess || childModifyNavResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childModifyNavResult.Error!);
                }
                updatedChildModify = childModifyNavResult.Value;

                var childReadResult = modelRewriter.RemoveProperty(childReadText, foreignKey);
                if (!childReadResult.IsSuccess || childReadResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childReadResult.Error!);
                }
                updatedChildRead = childReadResult.Value;

                var parentEntityResult = entityRewriter.RemovePropertyAndAssignments(parentEntityText, childrenSetName);
                if (!parentEntityResult.IsSuccess || parentEntityResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(parentEntityResult.Error!);
                }
                updatedParentEntity = parentEntityResult.Value;

                var childEntityFkResult = entityRewriter.RemovePropertyAndAssignments(childEntityText, foreignKey);
                if (!childEntityFkResult.IsSuccess || childEntityFkResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childEntityFkResult.Error!);
                }

                var childEntityNavResult = entityRewriter.RemovePropertyAndAssignments(childEntityFkResult.Value, parentEntity);
                if (!childEntityNavResult.IsSuccess || childEntityNavResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childEntityNavResult.Error!);
                }
                updatedChildEntity = childEntityNavResult.Value;

                var parentConfigResult = configRewriter.RemoveRelationConfiguration(parentConfigText, childrenSetName, parentEntity, foreignKey);
                if (!parentConfigResult.IsSuccess || parentConfigResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(parentConfigResult.Error!);
                }
                updatedParentConfig = parentConfigResult.Value;
            }
            else
            {
                if (configRewriter.HasRelationConfiguration(parentConfigText, childrenSetName, parentEntity, foreignKey))
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.RELATION_EXISTS,
                        $"Relation '{parentEntity} -> {childEntity}' via '{foreignKey}' already exists.");
                }

                if (entityRewriter.HasProperty(childEntityText, foreignKey)
                    || modelRewriter.HasProperty(childModifyText, foreignKey)
                    || modelRewriter.HasProperty(childReadText, foreignKey))
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.PROPERTY_EXISTS,
                        $"Property '{foreignKey}' already exists on entity '{childEntity}'.");
                }

                if (entityRewriter.HasProperty(childEntityText, parentEntity)
                    || modelRewriter.HasProperty(childModifyText, parentEntity)
                    || entityRewriter.HasProperty(parentEntityText, childrenSetName)
                    || modelRewriter.HasProperty(parentModifyText, childrenSetName))
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.PROPERTY_EXISTS,
                        $"Navigation property for '{parentEntity} -> {childEntity}' already exists.");
                }

                var parentModifyResult = modelRewriter.AddProperty(
                    parentModifyText,
                    childrenSetName,
                    $"List<{childEntity}>",
                    isNullable: true,
                    isVirtual: true);
                if (!parentModifyResult.IsSuccess || parentModifyResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(parentModifyResult.Error!);
                }
                updatedParentModify = parentModifyResult.Value;

                var childModifyFkResult = modelRewriter.AddProperty(
                    childModifyText,
                    foreignKey,
                    normalizedFkType,
                    isNullable: true,
                    isVirtual: false);
                if (!childModifyFkResult.IsSuccess || childModifyFkResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childModifyFkResult.Error!);
                }

                var childModifyNavResult = modelRewriter.AddProperty(
                    childModifyFkResult.Value,
                    parentEntity,
                    parentEntity,
                    isNullable: true,
                    isVirtual: true);
                if (!childModifyNavResult.IsSuccess || childModifyNavResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childModifyNavResult.Error!);
                }
                updatedChildModify = childModifyNavResult.Value;

                var childReadResult = modelRewriter.AddProperty(
                    childReadText,
                    foreignKey,
                    normalizedFkType,
                    isNullable: true,
                    isVirtual: false);
                if (!childReadResult.IsSuccess || childReadResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childReadResult.Error!);
                }
                updatedChildRead = childReadResult.Value;

                var parentEntityPropResult = entityRewriter.AddProperty(
                    parentEntityText,
                    parentEntity,
                    childrenSetName,
                    $"ICollection<{childEntity}>",
                    isNullable: true,
                    isVirtual: true,
                    isPrivateSetter: false);
                if (!parentEntityPropResult.IsSuccess || parentEntityPropResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(parentEntityPropResult.Error!);
                }

                var parentEntityAssign = $"{childrenSetName} ??= new List<{childEntity}>();";
                var parentEntityAssignResult = entityRewriter.AddConstructorAndUpdateAssignment(
                    parentEntityPropResult.Value,
                    parentEntity,
                    parentEntityAssign);
                if (!parentEntityAssignResult.IsSuccess || parentEntityAssignResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(parentEntityAssignResult.Error!);
                }
                updatedParentEntity = parentEntityAssignResult.Value;

                var childEntityFkResult = entityRewriter.AddProperty(
                    childEntityText,
                    childEntity,
                    foreignKey,
                    normalizedFkType,
                    isNullable: true,
                    isVirtual: false,
                    isPrivateSetter: true);
                if (!childEntityFkResult.IsSuccess || childEntityFkResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childEntityFkResult.Error!);
                }

                var childEntityNavResult = entityRewriter.AddProperty(
                    childEntityFkResult.Value,
                    childEntity,
                    parentEntity,
                    parentEntity,
                    isNullable: true,
                    isVirtual: true,
                    isPrivateSetter: true);
                if (!childEntityNavResult.IsSuccess || childEntityNavResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childEntityNavResult.Error!);
                }

                var childEntityAssign = $"{foreignKey} = dto.{foreignKey};";
                var childEntityAssignResult = entityRewriter.AddConstructorAndUpdateAssignment(
                    childEntityNavResult.Value,
                    childEntity,
                    childEntityAssign);
                if (!childEntityAssignResult.IsSuccess || childEntityAssignResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(childEntityAssignResult.Error!);
                }
                updatedChildEntity = childEntityAssignResult.Value;

                var parentConfigLine =
                    $"builder.HasMany(e => e.{childrenSetName}).WithOne(e => e.{parentEntity}).HasForeignKey(e => e.{foreignKey}).OnDelete(DeleteBehavior.Cascade);";
                var parentConfigResult = configRewriter.AddConfigurationStatement(parentConfigText, parentConfigLine);
                if (!parentConfigResult.IsSuccess || parentConfigResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(parentConfigResult.Error!);
                }
                updatedParentConfig = parentConfigResult.Value;
            }

            var stagedChanges = new List<StagedFileChange>
            {
                new(parentModifyModelPath, FileChangeKind.Modify, updatedParentModify),
                new(childModifyModelPath, FileChangeKind.Modify, updatedChildModify),
                new(childReadModelPath, FileChangeKind.Modify, updatedChildRead),
                new(parentEntityPath, FileChangeKind.Modify, updatedParentEntity),
                new(childEntityPath, FileChangeKind.Modify, updatedChildEntity),
                new(parentConfigPath, FileChangeKind.Modify, updatedParentConfig)
            };

            var planResult = await _diffEngine.ComputePlanAsync(info.SolutionPath, stagedChanges, ct).ConfigureAwait(false);
            if (!planResult.IsSuccess || planResult.Value is null)
            {
                return OperationResult<GenerationSummary>.Failure(
                    planResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to compute generation plan."));
            }

            var plan = planResult.Value;
            var modifiedPaths = stagedChanges.Select(c => c.FullPath).ToArray();

            if (!request.DryRun)
            {
                var (manifest, expectedHash, loadError) = await ServiceFileHelper.LoadOrCreateManifestAsync(
                    _manifestStore, info, overrideMapper: null, ct).ConfigureAwait(false);
                if (loadError is not null || manifest is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        loadError ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Failed to load manifest."));
                }

                var commitResult = await _diffEngine.CommitAsync(plan, ct).ConfigureAwait(false);
                if (!commitResult.IsSuccess || commitResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        commitResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to commit relation changes."));
                }

                foreach (var path in modifiedPaths)
                {
                    ServiceFileHelper.Report(progress, GenerationStage.Updating, $"Updated {Path.GetFileName(path)}", path);
                }

                var entities = manifest.Entities.Select(e => e with { Properties = [.. e.Properties] }).ToList();
                var relations = manifest.Relations.ToList();

                _ = ServiceFileHelper.GetOrAddEntity(entities, parentEntity);
                var childEntry = ServiceFileHelper.GetOrAddEntity(entities, childEntity);

                if (request.Remove)
                {
                    relations.RemoveAll(r =>
                        string.Equals(r.ParentEntity, parentEntity, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.ChildEntity, childEntity, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.ForeignKey, foreignKey, StringComparison.OrdinalIgnoreCase));

                    childEntry.Properties.RemoveAll(p =>
                        string.Equals(p.Name, foreignKey, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var relationName = $"{parentEntity}_{childEntity}_{foreignKey}";
                    var relIdx = relations.FindIndex(r =>
                        string.Equals(r.ParentEntity, parentEntity, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.ChildEntity, childEntity, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.ForeignKey, foreignKey, StringComparison.OrdinalIgnoreCase));

                    var relationModel = new ManifestRelation(
                        relationName,
                        parentEntity,
                        childEntity,
                        foreignKey,
                        normalizedFkType,
                        "1:N",
                        "Cascade",
                        Applied: true);

                    if (relIdx >= 0)
                    {
                        relations[relIdx] = relationModel;
                    }
                    else
                    {
                        relations.Add(relationModel);
                    }

                    ServiceFileHelper.UpsertProperty(
                        childEntry.Properties,
                        new ManifestProperty(
                            foreignKey,
                            normalizedFkType,
                            Nullable: true,
                            IsForeignKey: true,
                            Applied: true));
                }

                var updatedManifest = manifest with
                {
                    Entities = entities,
                    Relations = relations,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };

                var saveResult = await ServiceFileHelper.SaveManifestUnderLockAsync(
                    _manifestStore, info.SolutionPath, updatedManifest, expectedHash, ct).ConfigureAwait(false);
                if (!saveResult.IsSuccess)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        saveResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to synchronize manifest."));
                }
            }

            ServiceFileHelper.Report(
                progress,
                GenerationStage.Completed,
                $"Relation '{parentEntity} -> {childEntity}' {(request.Remove ? "removal" : "addition")} {(request.DryRun ? "planned" : "applied")}.",
                parentConfigPath);

            return OperationResult<GenerationSummary>.Success(new GenerationSummary(
                Array.Empty<string>(),
                modifiedPaths,
                Array.Empty<string>(),
                request.DryRun,
                Plan: plan));
        }
        catch (OperationCanceledException)
        {
            ServiceFileHelper.Report(progress, GenerationStage.Failed, "Relation operation was cancelled.");
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Relation operation was cancelled.");
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "Lock timeout updating relation '{ParentEntity} -> {ChildEntity}'.", parentEntity, childEntity);
            ServiceFileHelper.Report(progress, GenerationStage.Failed, ex.Message);
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.IO_ERROR, ex.Message, ex.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to update relation '{ParentEntity} -> {ChildEntity}'.", parentEntity, childEntity);
            ServiceFileHelper.Report(progress, GenerationStage.Failed, ex.Message);
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to update relation '{parentEntity} -> {childEntity}': {ex.Message}",
                ex.ToString());
        }
        finally
        {
            if (lockHandle is not null)
            {
                await lockHandle.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
