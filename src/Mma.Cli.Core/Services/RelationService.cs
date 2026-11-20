using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Services;

public sealed class RelationService : IRelationService
{
    private readonly ISolutionLocator _solutionLocator;
    private readonly ILogger<RelationService>? _logger;

    public RelationService(
        ISolutionLocator solutionLocator,
        ILogger<RelationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(solutionLocator);

        _solutionLocator = solutionLocator;
        _logger = logger;
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

        var located = _solutionLocator.Locate(request.SolutionPath);
        if (!located.IsSuccess || located.Value is null)
        {
            return OperationResult<GenerationSummary>.Failure(
                located.Error ?? new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Solution could not be located."));
        }

        var info = located.Value;
        var modelSuffix = info.DetectedMapper == MapperKind.Mapster ? ".g.cs" : ".cs";
        var parentEntity = request.ParentEntity;
        var childEntity = request.ChildEntity;
        var foreignKey = request.ForeignKey;
        var childrenSetName = NamingHelper.Pluralize(childEntity);

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
        var parentConfigLine =
            $"builder.HasMany(e => e.{childrenSetName}).WithOne(e => e.{parentEntity}).HasForeignKey(e => e.{foreignKey}).OnDelete(DeleteBehavior.Cascade);";

        try
        {
            var parentModifyLines = await ServiceFileHelper.ReadLinesAsync(parentModifyModelPath, ct).ConfigureAwait(false);
            var childModifyLines = await ServiceFileHelper.ReadLinesAsync(childModifyModelPath, ct).ConfigureAwait(false);
            var childReadLines = await ServiceFileHelper.ReadLinesAsync(childReadModelPath, ct).ConfigureAwait(false);
            var parentEntityLines = await ServiceFileHelper.ReadLinesAsync(parentEntityPath, ct).ConfigureAwait(false);
            var childEntityLines = await ServiceFileHelper.ReadLinesAsync(childEntityPath, ct).ConfigureAwait(false);
            var parentConfigLines = await ServiceFileHelper.ReadLinesAsync(parentConfigPath, ct).ConfigureAwait(false);

            if (request.Remove)
            {
                var relationExists = parentConfigLines.Any(l => l.Contains(parentConfigLine, StringComparison.Ordinal))
                                     || childEntityLines.Any(l => l.Contains(childEntityFk, StringComparison.Ordinal));
                if (!relationExists)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.RELATION_NOT_FOUND,
                        $"Relation '{parentEntity} -> {childEntity}' via '{foreignKey}' was not found.");
                }

                ServiceFileHelper.RemoveLineContaining(parentModifyLines, parentDtoNav);
                ServiceFileHelper.RemoveLineContaining(childModifyLines, childDtoFk);
                ServiceFileHelper.RemoveLineContaining(childModifyLines, childDtoNav);
                ServiceFileHelper.RemoveLineContaining(childReadLines, childDtoFk);

                ServiceFileHelper.RemoveLineContaining(parentEntityLines, parentEntityNav);
                ServiceFileHelper.RemoveLineContaining(parentEntityLines, parentEntityAssign);
                ServiceFileHelper.RemoveLineContaining(parentEntityLines, parentEntityAssign);

                ServiceFileHelper.RemoveLineContaining(childEntityLines, childEntityFk);
                ServiceFileHelper.RemoveLineContaining(childEntityLines, childEntityNav);
                ServiceFileHelper.RemoveLineContaining(childEntityLines, childEntityAssign);
                ServiceFileHelper.RemoveLineContaining(childEntityLines, childEntityAssign);

                ServiceFileHelper.RemoveLineContaining(parentConfigLines, parentConfigLine);
            }
            else
            {
                var relationExists = parentConfigLines.Any(l => l.Contains(parentConfigLine, StringComparison.Ordinal))
                                     || childEntityLines.Any(l => l.Contains(childEntityFk, StringComparison.Ordinal));
                if (relationExists)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.RELATION_EXISTS,
                        $"Relation '{parentEntity} -> {childEntity}' via '{foreignKey}' already exists.");
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

                var configIndexLine = parentConfigLines.FindIndex(l => l.Contains("builder.HasIndex(e => e.IsDeleted);", StringComparison.Ordinal));
                if (configIndexLine < 0)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.TEMPLATE_ERROR,
                        $"Could not locate configuration anchor in '{parentConfigPath}'.");
                }
                parentConfigLines.Insert(Math.Min(configIndexLine + 2, parentConfigLines.Count), parentConfigLine);
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
                foreach (var (path, lines) in pendingWrites)
                {
                    ct.ThrowIfCancellationRequested();
                    await ServiceFileHelper.WriteLinesAsync(path, lines, ct).ConfigureAwait(false);
                    ServiceFileHelper.Report(progress, GenerationStage.Updating, $"Updated {Path.GetFileName(path)}", path);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to update relation '{ParentEntity} -> {ChildEntity}'.", parentEntity, childEntity);
            ServiceFileHelper.Report(progress, GenerationStage.Failed, ex.Message);
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to update relation '{parentEntity} -> {childEntity}': {ex.Message}",
                ex.ToString());
        }
    }

    private static string BuildRelationProperty(bool isPrivateSet, bool isVirtual, string type, string name)
    {
        var modifier = isPrivateSet ? "private " : string.Empty;
        var virtualized = isVirtual ? "virtual " : string.Empty;
        return $"public {virtualized}{type}? {name} {{get; {modifier}set;}}";
    }
}
