using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Services;

public sealed class RelationService : IRelationService
{
    private readonly ISolutionLocator _solutionLocator;
    private readonly ILogger<RelationService>? _logger;
    private readonly ISolutionLockManager _lockManager;
    private readonly IManifestStore _manifestStore;

    public RelationService(
        ISolutionLocator solutionLocator,
        ILogger<RelationService>? logger = null,
        ISolutionLockManager? lockManager = null,
        IManifestStore? manifestStore = null)
    {
        ArgumentNullException.ThrowIfNull(solutionLocator);

        _solutionLocator = solutionLocator;
        _logger = logger;
        _lockManager = lockManager ?? new SolutionLockManager();
        _manifestStore = manifestStore ?? new ManifestStore(_lockManager, solutionLocator);
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

            var parentDtoNav = BuildRelationProperty(false, true, $"List<{childEntity}>", childrenSetName);
            var childDtoNav = BuildRelationProperty(false, true, parentEntity, parentEntity);
            var childDtoFk = BuildRelationProperty(false, false, normalizedFkType, foreignKey);
            var parentEntityNav = BuildRelationProperty(false, true, $"ICollection<{childEntity}>", childrenSetName);
            var parentEntityAssign = $"{childrenSetName} ??= new List<{childEntity}>();";
            var childEntityNav = BuildRelationProperty(true, true, parentEntity, parentEntity);
            var childEntityFk = BuildRelationProperty(true, false, normalizedFkType, foreignKey);
            var childEntityAssign = $"{foreignKey} = dto.{foreignKey};";
            var parentConfigPrefix =
                $"builder.HasMany(e => e.{childrenSetName}).WithOne(e => e.{parentEntity}).HasForeignKey(e => e.{foreignKey})";
            var parentConfigLine = $"{parentConfigPrefix}.OnDelete(DeleteBehavior.Cascade);";

            var parentModifyLines = await ServiceFileHelper.ReadLinesAsync(parentModifyModelPath, ct).ConfigureAwait(false);
            var childModifyLines = await ServiceFileHelper.ReadLinesAsync(childModifyModelPath, ct).ConfigureAwait(false);
            var childReadLines = await ServiceFileHelper.ReadLinesAsync(childReadModelPath, ct).ConfigureAwait(false);
            var parentEntityLines = await ServiceFileHelper.ReadLinesAsync(parentEntityPath, ct).ConfigureAwait(false);
            var childEntityLines = await ServiceFileHelper.ReadLinesAsync(childEntityPath, ct).ConfigureAwait(false);
            var parentConfigLines = await ServiceFileHelper.ReadLinesAsync(parentConfigPath, ct).ConfigureAwait(false);

            if (request.Remove)
            {
                var relationExists = parentConfigLines.Any(l => l.Contains(parentConfigPrefix, StringComparison.Ordinal))
                                     || ServiceFileHelper.HasPropertyNamed(childEntityLines, foreignKey);
                if (!relationExists)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.RELATION_NOT_FOUND,
                        $"Relation '{parentEntity} -> {childEntity}' via '{foreignKey}' was not found.");
                }

                RemoveVirtualProperty(parentModifyLines, $"List<{childEntity}>", childrenSetName);
                ServiceFileHelper.RemovePropertyNamed(childModifyLines, foreignKey);
                RemoveVirtualProperty(childModifyLines, parentEntity, parentEntity);
                ServiceFileHelper.RemovePropertyNamed(childReadLines, foreignKey);

                RemoveVirtualProperty(parentEntityLines, $"ICollection<{childEntity}>", childrenSetName);
                ServiceFileHelper.RemoveLineContaining(parentEntityLines, parentEntityAssign);
                ServiceFileHelper.RemoveLineContaining(parentEntityLines, parentEntityAssign);

                ServiceFileHelper.RemovePropertyNamed(childEntityLines, foreignKey);
                RemoveVirtualProperty(childEntityLines, parentEntity, parentEntity);
                ServiceFileHelper.RemoveLineContaining(childEntityLines, childEntityAssign);
                ServiceFileHelper.RemoveLineContaining(childEntityLines, childEntityAssign);

                ServiceFileHelper.RemoveLineContaining(parentConfigLines, parentConfigPrefix);
            }
            else
            {
                if (parentConfigLines.Any(l => l.Contains(parentConfigPrefix, StringComparison.Ordinal)))
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.RELATION_EXISTS,
                        $"Relation '{parentEntity} -> {childEntity}' via '{foreignKey}' already exists.");
                }

                if (ServiceFileHelper.HasPropertyNamed(childEntityLines, foreignKey)
                    || ServiceFileHelper.HasPropertyNamed(childModifyLines, foreignKey)
                    || ServiceFileHelper.HasPropertyNamed(childReadLines, foreignKey))
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.PROPERTY_EXISTS,
                        $"Property '{foreignKey}' already exists on entity '{childEntity}'.");
                }

                if (ServiceFileHelper.HasPropertyNamed(childEntityLines, parentEntity)
                    || ServiceFileHelper.HasPropertyNamed(childModifyLines, parentEntity)
                    || ServiceFileHelper.HasPropertyNamed(parentEntityLines, childrenSetName)
                    || ServiceFileHelper.HasPropertyNamed(parentModifyLines, childrenSetName))
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.PROPERTY_EXISTS,
                        $"Navigation property for '{parentEntity} -> {childEntity}' already exists.");
                }

                ServiceFileHelper.InsertBeforeClosingBraces(parentModifyLines, parentDtoNav);

                var childModifyIdx = Math.Max(0, childModifyLines.Count - 2);
                childModifyLines.Insert(childModifyIdx, childDtoNav);
                childModifyLines.Insert(childModifyIdx, childDtoFk);

                ServiceFileHelper.InsertBeforeClosingBraces(childReadLines, childDtoFk);

                var parentValidatorIdx = parentEntityLines.FindIndex(l => l.Contains($"{parentEntity}Validator _Validator;", StringComparison.Ordinal));
                if (parentValidatorIdx < 1)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.TEMPLATE_ERROR,
                        $"Could not locate validator anchor in '{parentEntityPath}'.");
                }
                parentEntityLines.Insert(parentValidatorIdx - 1, parentEntityNav);

                ServiceFileHelper.InsertAtAnchorOffset(parentEntityLines, $"public {parentEntity}(", 10, parentEntityAssign);
                ServiceFileHelper.InsertAtAnchorOffset(parentEntityLines, $"public {parentEntity} Update(", 10, parentEntityAssign);

                var childValidatorIdx = childEntityLines.FindIndex(l => l.Contains($"{childEntity}Validator _Validator;", StringComparison.Ordinal));
                if (childValidatorIdx < 1)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.TEMPLATE_ERROR,
                        $"Could not locate validator anchor in '{childEntityPath}'.");
                }
                childEntityLines.Insert(childValidatorIdx - 1, childEntityNav);
                childEntityLines.Insert(childValidatorIdx - 1, childEntityFk);

                ServiceFileHelper.InsertAtAnchorOffset(childEntityLines, $"public {childEntity}(", 10, childEntityAssign);
                ServiceFileHelper.InsertAtAnchorOffset(childEntityLines, $"public {childEntity} Update(", 10, childEntityAssign);

                if (!ServiceFileHelper.TryInsertAfterEntityConfigAnchor(parentConfigLines, parentConfigLine))
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.TEMPLATE_ERROR,
                        $"Could not locate configuration anchor in '{parentConfigPath}'.");
                }
            }

            var pendingWrites = new List<(string Path, List<string> Lines)>
            {
                (parentModifyModelPath, parentModifyLines),
                (childModifyModelPath, childModifyLines),
                (childReadModelPath, childReadLines),
                (parentEntityPath, parentEntityLines),
                (childEntityPath, childEntityLines),
                (parentConfigPath, parentConfigLines)
            };

            if (!request.DryRun)
            {
                var (manifest, expectedHash, loadError) = await ServiceFileHelper.LoadOrCreateManifestAsync(
                    _manifestStore, info, overrideMapper: null, ct).ConfigureAwait(false);
                if (loadError is not null || manifest is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        loadError ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Failed to load manifest."));
                }

                foreach (var (path, lines) in pendingWrites)
                {
                    ct.ThrowIfCancellationRequested();
                    await ServiceFileHelper.WriteLinesAsync(path, lines, ct).ConfigureAwait(false);
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
                pendingWrites.Select(w => w.Path).ToArray(),
                Array.Empty<string>(),
                request.DryRun));
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

    private static string BuildRelationProperty(bool isPrivateSet, bool isVirtual, string type, string name)
    {
        var modifier = isPrivateSet ? "private " : string.Empty;
        var virtualized = isVirtual ? "virtual " : string.Empty;
        return $"public {virtualized}{type}? {name} {{get; {modifier}set;}}";
    }

    private static bool RemoveVirtualProperty(List<string> lines, string type, string name)
    {
        var pattern = $@"^\s*public\s+virtual\s+{Regex.Escape(type)}\??\s+{Regex.Escape(name)}\s*\{{\s*(?:get|init)\b";
        return ServiceFileHelper.RemoveLineMatching(lines, l => Regex.IsMatch(l, pattern, RegexOptions.CultureInvariant));
    }
}
