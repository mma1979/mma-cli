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

public sealed class PropertyService : IPropertyService
{
    private readonly ISolutionLocator _solutionLocator;
    private readonly ILogger<PropertyService>? _logger;
    private readonly ISolutionLockManager _lockManager;
    private readonly IManifestStore _manifestStore;
    private readonly IDryRunDiffEngine _diffEngine;

    public PropertyService(
        ISolutionLocator solutionLocator,
        ILogger<PropertyService>? logger = null,
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
        AddPropertyRequest request,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Property operation was cancelled.");
        }

        ServiceFileHelper.Report(
            progress,
            GenerationStage.Validating,
            $"Validating property '{request.EntityName}.{request.PropertyName}'.");

        if (!NamingHelper.IsValidIdentifier(request.EntityName))
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid entity name '{request.EntityName}'.");
        }

        if (!NamingHelper.IsValidIdentifier(request.PropertyName))
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid property name '{request.PropertyName}'.");
        }

        var normalizedType = DataTypes.Normalize(request.DataType);
        if (normalizedType is null)
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Unsupported property data type '{request.DataType}'.");
        }

        if (!ServiceFileHelper.TryResolveExistingSolutionPath(request.SolutionPath, out var fullSolutionPath, out var pathError))
        {
            return OperationResult<GenerationSummary>.Failure(pathError!);
        }

        var entityName = request.EntityName;
        var propertyName = request.PropertyName;

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

            var modifyModelPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Models", $"{entityName}ModifyModel{modelSuffix}");
            var readModelPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Models", $"{entityName}ReadModel{modelSuffix}");
            var entityPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Database", "Tables", $"{entityName}.cs");
            var entityConfigPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.EntityFramework", "EntityConfigurations", $"{entityName}Config.cs");

            if (!File.Exists(entityPath) || !File.Exists(modifyModelPath) || !File.Exists(readModelPath))
            {
                return OperationResult<GenerationSummary>.Failure(
                    ErrorCodes.ENTITY_NOT_FOUND,
                    $"Entity '{entityName}' or its models could not be found in '{info.ProjectsPath}'.");
            }

            var modifyModelText = await ServiceFileHelper.ReadUtf8TextAsync(modifyModelPath, ct).ConfigureAwait(false);
            var readModelText = await ServiceFileHelper.ReadUtf8TextAsync(readModelPath, ct).ConfigureAwait(false);
            var entityText = await ServiceFileHelper.ReadUtf8TextAsync(entityPath, ct).ConfigureAwait(false);

            foreach (var (path, text) in new[] { (modifyModelPath, modifyModelText), (readModelPath, readModelText), (entityPath, entityText) })
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

            var configLine = BuildConfigLine(normalizedType, propertyName);
            var stagedChanges = new List<StagedFileChange>();

            if (request.Remove)
            {
                var propExists = entityRewriter.HasProperty(entityText, propertyName)
                                 || modelRewriter.HasProperty(modifyModelText, propertyName);
                if (!propExists)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.PROPERTY_NOT_FOUND,
                        $"Property '{propertyName}' was not found on entity '{entityName}'.");
                }

                var updatedModify = modelRewriter.RemoveProperty(modifyModelText, propertyName);
                if (!updatedModify.IsSuccess || updatedModify.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(updatedModify.Error!);
                }

                var updatedRead = modelRewriter.RemoveProperty(readModelText, propertyName);
                if (!updatedRead.IsSuccess || updatedRead.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(updatedRead.Error!);
                }

                var updatedEntity = entityRewriter.RemovePropertyAndAssignments(entityText, propertyName);
                if (!updatedEntity.IsSuccess || updatedEntity.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(updatedEntity.Error!);
                }

                stagedChanges.Add(new StagedFileChange(modifyModelPath, FileChangeKind.Modify, updatedModify.Value));
                stagedChanges.Add(new StagedFileChange(readModelPath, FileChangeKind.Modify, updatedRead.Value));
                stagedChanges.Add(new StagedFileChange(entityPath, FileChangeKind.Modify, updatedEntity.Value));

                if (File.Exists(entityConfigPath))
                {
                    var configText = await ServiceFileHelper.ReadUtf8TextAsync(entityConfigPath, ct).ConfigureAwait(false);
                    var updatedConfig = configRewriter.RemovePropertyConfiguration(configText, propertyName);
                    if (!updatedConfig.IsSuccess || updatedConfig.Value is null)
                    {
                        return OperationResult<GenerationSummary>.Failure(updatedConfig.Error!);
                    }

                    if (!string.Equals(updatedConfig.Value, configText, StringComparison.Ordinal))
                    {
                        stagedChanges.Add(new StagedFileChange(entityConfigPath, FileChangeKind.Modify, updatedConfig.Value));
                    }
                }
            }
            else
            {
                var duplicate = entityRewriter.HasProperty(entityText, propertyName)
                                || modelRewriter.HasProperty(modifyModelText, propertyName);
                if (duplicate)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.PROPERTY_EXISTS,
                        $"Property '{propertyName}' already exists on entity '{entityName}'.");
                }

                var updatedModify = modelRewriter.AddProperty(modifyModelText, propertyName, normalizedType, isNullable: request.Nullable);
                if (!updatedModify.IsSuccess || updatedModify.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(updatedModify.Error!);
                }

                var updatedRead = modelRewriter.AddProperty(readModelText, propertyName, normalizedType, isNullable: request.Nullable);
                if (!updatedRead.IsSuccess || updatedRead.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(updatedRead.Error!);
                }

                var entityWithProp = entityRewriter.AddProperty(
                    entityText,
                    entityName,
                    propertyName,
                    normalizedType,
                    isNullable: request.Nullable,
                    isVirtual: false,
                    isPrivateSetter: true);
                if (!entityWithProp.IsSuccess || entityWithProp.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(entityWithProp.Error!);
                }

                var assignment = $"{propertyName} = dto.{propertyName};";
                var entityWithAssign = entityRewriter.AddConstructorAndUpdateAssignment(entityWithProp.Value, entityName, assignment);
                if (!entityWithAssign.IsSuccess || entityWithAssign.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(entityWithAssign.Error!);
                }

                stagedChanges.Add(new StagedFileChange(modifyModelPath, FileChangeKind.Modify, updatedModify.Value));
                stagedChanges.Add(new StagedFileChange(readModelPath, FileChangeKind.Modify, updatedRead.Value));
                stagedChanges.Add(new StagedFileChange(entityPath, FileChangeKind.Modify, entityWithAssign.Value));

                if (!string.IsNullOrEmpty(configLine) && File.Exists(entityConfigPath))
                {
                    var configText = await ServiceFileHelper.ReadUtf8TextAsync(entityConfigPath, ct).ConfigureAwait(false);
                    var updatedConfig = configRewriter.AddConfigurationStatement(configText, configLine);
                    if (!updatedConfig.IsSuccess || updatedConfig.Value is null)
                    {
                        return OperationResult<GenerationSummary>.Failure(updatedConfig.Error!);
                    }

                    stagedChanges.Add(new StagedFileChange(entityConfigPath, FileChangeKind.Modify, updatedConfig.Value));
                }
            }

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
                        commitResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to commit property changes."));
                }

                foreach (var path in modifiedPaths)
                {
                    ServiceFileHelper.Report(progress, GenerationStage.Updating, $"Updated {Path.GetFileName(path)}", path);
                }

                var entities = manifest.Entities.Select(e => e with { Properties = [.. e.Properties] }).ToList();
                var entityEntry = ServiceFileHelper.GetOrAddEntity(entities, entityName);

                if (request.Remove)
                {
                    entityEntry.Properties.RemoveAll(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var existingProp = entityEntry.Properties.FirstOrDefault(
                        p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
                    ServiceFileHelper.UpsertProperty(
                        entityEntry.Properties,
                        new ManifestProperty(
                            propertyName,
                            normalizedType,
                            Nullable: request.Nullable,
                            IsForeignKey: existingProp?.IsForeignKey ?? false,
                            Applied: true,
                            MaxLength: normalizedType == "string" ? 255 : existingProp?.MaxLength,
                            Required: existingProp?.Required));
                }

                var updatedManifest = manifest with
                {
                    Entities = entities,
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
                $"Property '{entityName}.{propertyName}' {(request.Remove ? "removal" : "addition")} {(request.DryRun ? "planned" : "applied")}.",
                entityPath);

            return OperationResult<GenerationSummary>.Success(new GenerationSummary(
                Array.Empty<string>(),
                modifiedPaths,
                Array.Empty<string>(),
                request.DryRun,
                Plan: plan));
        }
        catch (OperationCanceledException)
        {
            ServiceFileHelper.Report(progress, GenerationStage.Failed, "Property operation was cancelled.");
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Property operation was cancelled.");
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "Lock timeout updating property '{EntityName}.{PropertyName}'.", entityName, propertyName);
            ServiceFileHelper.Report(progress, GenerationStage.Failed, ex.Message);
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.IO_ERROR, ex.Message, ex.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to update property '{EntityName}.{PropertyName}'.", entityName, propertyName);
            ServiceFileHelper.Report(progress, GenerationStage.Failed, ex.Message);
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to update property '{entityName}.{propertyName}': {ex.Message}",
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

    private static string BuildConfigLine(string normalizedType, string propertyName) => normalizedType switch
    {
        "string" => $"builder.Property(e => e.{propertyName}).HasMaxLength(255);",
        "decimal" => $"builder.Property(e => e.{propertyName}).HasColumnType(\"decimal(9,3)\");",
        "DateTime" => $"builder.Property(e => e.{propertyName}).HasColumnType(\"datetime\");",
        "Guid" => $"builder.Property(e => e.{propertyName}).HasDefaultValue(\"newid()\");",
        "byte[]" => $"builder.Property(e => e.{propertyName}).HasColumnType(\"varbinary(max)\");",
        "TimeSpan" => $"builder.Property(e => e.{propertyName}).HasColumnType(\"time\");",
        _ => string.Empty
    };
}
