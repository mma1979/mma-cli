using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Services;

public sealed class PropertyService : IPropertyService
{
    private readonly ISolutionLocator _solutionLocator;
    private readonly ILogger<PropertyService>? _logger;
    private readonly ISolutionLockManager _lockManager;
    private readonly IManifestStore _manifestStore;

    public PropertyService(
        ISolutionLocator solutionLocator,
        ILogger<PropertyService>? logger = null,
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

        var located = _solutionLocator.Locate(request.SolutionPath);
        if (!located.IsSuccess || located.Value is null)
        {
            return OperationResult<GenerationSummary>.Failure(
                located.Error ?? new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Solution could not be located."));
        }

        var info = located.Value;
        var modelSuffix = info.DetectedMapper == MapperKind.Mapster ? ".g.cs" : ".cs";
        var entityName = request.EntityName;
        var propertyName = request.PropertyName;

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

        var nullableSuffix = request.Nullable ? "?" : string.Empty;
        var dtoProperty = $"public {normalizedType}{nullableSuffix} {propertyName} {{get; set;}}";
        var entityProperty = $"public {normalizedType}{nullableSuffix} {propertyName} {{get; private set;}}";
        var assignment = $"{propertyName} = dto.{propertyName};";
        var configLine = BuildConfigLine(normalizedType, propertyName);

        IAsyncDisposable? lockHandle = null;
        try
        {
            if (!request.DryRun)
            {
                lockHandle = await _lockManager.AcquireAsync(info.SolutionPath, ct: ct).ConfigureAwait(false);
            }

            var modifyLines = await ServiceFileHelper.ReadLinesAsync(modifyModelPath, ct).ConfigureAwait(false);
            var readLines = await ServiceFileHelper.ReadLinesAsync(readModelPath, ct).ConfigureAwait(false);
            var entityLines = await ServiceFileHelper.ReadLinesAsync(entityPath, ct).ConfigureAwait(false);

            var pendingWrites = new List<(string Path, List<string> Lines)>();

            if (request.Remove)
            {
                var propExists = entityLines.Any(l => l.Contains(entityProperty, StringComparison.Ordinal))
                                 || modifyLines.Any(l => l.Contains(dtoProperty, StringComparison.Ordinal));
                if (!propExists)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.PROPERTY_NOT_FOUND,
                        $"Property '{propertyName}' was not found on entity '{entityName}'.");
                }

                ServiceFileHelper.RemoveLineContaining(modifyLines, dtoProperty);
                ServiceFileHelper.RemoveLineContaining(readLines, dtoProperty);
                ServiceFileHelper.RemoveLineContaining(entityLines, entityProperty);
                ServiceFileHelper.RemoveLineContaining(entityLines, assignment);
                ServiceFileHelper.RemoveLineContaining(entityLines, assignment);

                pendingWrites.Add((modifyModelPath, modifyLines));
                pendingWrites.Add((readModelPath, readLines));
                pendingWrites.Add((entityPath, entityLines));

                if (!string.IsNullOrEmpty(configLine) && File.Exists(entityConfigPath))
                {
                    var configLines = await ServiceFileHelper.ReadLinesAsync(entityConfigPath, ct).ConfigureAwait(false);
                    if (ServiceFileHelper.RemoveLineContaining(configLines, configLine))
                    {
                        pendingWrites.Add((entityConfigPath, configLines));
                    }
                }
            }
            else
            {
                var duplicate = ServiceFileHelper.HasPropertyNamed(entityLines, propertyName)
                                || ServiceFileHelper.HasPropertyNamed(modifyLines, propertyName);
                if (duplicate)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.PROPERTY_EXISTS,
                        $"Property '{propertyName}' already exists on entity '{entityName}'.");
                }

                ServiceFileHelper.InsertBeforeClosingBraces(modifyLines, dtoProperty);
                ServiceFileHelper.InsertBeforeClosingBraces(readLines, dtoProperty);

                var validatorFieldIdx = entityLines.FindIndex(l => l.Contains($"{entityName}Validator _Validator;", StringComparison.Ordinal));
                if (validatorFieldIdx < 1)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.TEMPLATE_ERROR,
                        $"Could not locate validator anchor in '{entityPath}'.");
                }
                entityLines.Insert(validatorFieldIdx - 1, entityProperty);

                ServiceFileHelper.InsertAtAnchorOffset(entityLines, $"public {entityName}(", 10, assignment);
                ServiceFileHelper.InsertAtAnchorOffset(entityLines, $"public {entityName} Update(", 10, assignment);

                pendingWrites.Add((modifyModelPath, modifyLines));
                pendingWrites.Add((readModelPath, readLines));
                pendingWrites.Add((entityPath, entityLines));

                if (!string.IsNullOrEmpty(configLine) && File.Exists(entityConfigPath))
                {
                    var configLines = await ServiceFileHelper.ReadLinesAsync(entityConfigPath, ct).ConfigureAwait(false);
                    if (!ServiceFileHelper.TryInsertAfterEntityConfigAnchor(configLines, configLine))
                    {
                        return OperationResult<GenerationSummary>.Failure(
                            ErrorCodes.TEMPLATE_ERROR,
                            $"Could not locate configuration anchor in '{entityConfigPath}'.");
                    }

                    pendingWrites.Add((entityConfigPath, configLines));
                }
            }

            if (!request.DryRun)
            {
                foreach (var (path, lines) in pendingWrites)
                {
                    ct.ThrowIfCancellationRequested();
                    await ServiceFileHelper.WriteLinesAsync(path, lines, ct).ConfigureAwait(false);
                    ServiceFileHelper.Report(progress, GenerationStage.Updating, $"Updated {Path.GetFileName(path)}", path);
                }

                var (manifest, expectedHash, loadError) = await ServiceFileHelper.LoadOrCreateManifestAsync(
                    _manifestStore, info, overrideMapper: null, ct).ConfigureAwait(false);
                if (loadError is not null || manifest is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        loadError ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Failed to load manifest."));
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
                pendingWrites.Select(w => w.Path).ToArray(),
                Array.Empty<string>(),
                request.DryRun));
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
