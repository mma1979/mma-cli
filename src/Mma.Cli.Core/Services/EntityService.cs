using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Core.Templates;

namespace Mma.Cli.Core.Services;

public sealed class EntityService : IEntityService
{
    private readonly ISolutionLocator _solutionLocator;
    private readonly ITemplateProvider _templateProvider;
    private readonly ILogger<EntityService>? _logger;
    private readonly ISolutionLockManager _lockManager;
    private readonly IManifestStore _manifestStore;

    public EntityService(
        ISolutionLocator solutionLocator,
        ITemplateProvider templateProvider,
        ILogger<EntityService>? logger = null,
        ISolutionLockManager? lockManager = null,
        IManifestStore? manifestStore = null)
    {
        ArgumentNullException.ThrowIfNull(solutionLocator);
        ArgumentNullException.ThrowIfNull(templateProvider);

        _solutionLocator = solutionLocator;
        _templateProvider = templateProvider;
        _logger = logger;
        _lockManager = lockManager ?? new SolutionLockManager();
        _manifestStore = manifestStore ?? new ManifestStore(_lockManager, solutionLocator);
    }

    public async Task<OperationResult<GenerationSummary>> GenerateAsync(
        AddEntityRequest request,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Entity generation was cancelled.");
        }

        ServiceFileHelper.Report(progress, GenerationStage.Validating, $"Validating entity '{request.EntityName}'.");

        if (!NamingHelper.IsValidIdentifier(request.EntityName))
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid entity name '{request.EntityName}'. Must be a valid non-keyword C# identifier.");
        }

        var normalizedPk = DataTypes.Normalize(request.PkType);
        if (normalizedPk is null)
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Unsupported primary key type '{request.PkType}'.");
        }

        var located = _solutionLocator.Locate(request.SolutionPath);
        if (!located.IsSuccess || located.Value is null)
        {
            return OperationResult<GenerationSummary>.Failure(
                located.Error ?? new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Solution could not be located."));
        }

        var info = located.Value;
        var mapper = request.Mapper ?? info.DetectedMapper;
        var entityName = request.EntityName;
        var entitySetName = NamingHelper.Pluralize(entityName);
        var entityVarName = NamingHelper.ToCamelCase(entityName);
        var modelSuffix = mapper == MapperKind.Mapster ? ".g.cs" : ".cs";

        var modifyModelPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Models", $"{entityName}ModifyModel{modelSuffix}");
        var readModelPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Models", $"{entityName}ReadModel{modelSuffix}");
        var validatorPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Validations", $"{entityName}Validator.cs");
        var entityPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "Database", "Tables", $"{entityName}.cs");
        var entityConfigPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.EntityFramework", "EntityConfigurations", $"{entityName}Config.cs");
        var servicePath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Services", $"{entityName}Service.cs");
        var controllerPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.AppApi", "Controllers", "v1", $"{entitySetName}Controller.cs");
        var dbContextPath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.EntityFramework", "ApplicationDbContext.cs");
        var mappingProfilePath = Path.Combine(info.ProjectsPath, $"{info.SolutionName}.Core", "MappingProfile.cs");

        IAsyncDisposable? lockHandle = null;
        try
        {
            if (!request.DryRun)
            {
                lockHandle = await _lockManager.AcquireAsync(info.SolutionPath, ct: ct).ConfigureAwait(false);
            }

            if (File.Exists(entityPath))
            {
                return OperationResult<GenerationSummary>.Failure(
                    ErrorCodes.ENTITY_EXISTS,
                    $"Entity '{entityName}' already exists at '{entityPath}'.");
            }

            if (!File.Exists(dbContextPath))
            {
                return OperationResult<GenerationSummary>.Failure(
                    ErrorCodes.IO_ERROR,
                    $"ApplicationDbContext not found at '{dbContextPath}'.");
            }

            if (mapper == MapperKind.AutoMapper && !File.Exists(mappingProfilePath))
            {
                return OperationResult<GenerationSummary>.Failure(
                    ErrorCodes.IO_ERROR,
                    $"MappingProfile not found at '{mappingProfilePath}'.");
            }

            ServiceFileHelper.Report(progress, GenerationStage.Scaffolding, $"Preparing files for entity '{entityName}'.");

            var createdEntries = new List<(string Path, string Content)>
            {
                (modifyModelPath, ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "ModifyModel"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk)),
                (readModelPath, ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "ReadModel"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk)),
                (validatorPath, ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "Validator"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk)),
                (entityPath, ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "Entity"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk)),
                (entityConfigPath, ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "EntityConfig"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk)),
                (servicePath, ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "Service"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk))
            };

            if (request.GenerateApi)
            {
                createdEntries.Add((
                    controllerPath,
                    ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "Controller"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk)));
            }

            var modifiedEntries = new List<(string Path, List<string> Lines)>();

            var dbContextLines = await ServiceFileHelper.ReadLinesAsync(dbContextPath, ct).ConfigureAwait(false);
            var dbSetIdx = dbContextLines.FindLastIndex(l => l.Contains("public virtual DbSet<", StringComparison.Ordinal));
            if (dbSetIdx < 0)
            {
                return OperationResult<GenerationSummary>.Failure(
                    ErrorCodes.TEMPLATE_ERROR,
                    $"Could not locate DbSet anchor in '{dbContextPath}'.");
            }

            dbContextLines.Insert(
                dbSetIdx + 1,
                ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "DbSetEntry"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk));

            var configIdx = dbContextLines.FindLastIndex(l => l.Contains("modelBuilder.ApplyConfiguration(new", StringComparison.Ordinal));
            if (configIdx < 0)
            {
                return OperationResult<GenerationSummary>.Failure(
                    ErrorCodes.TEMPLATE_ERROR,
                    $"Could not locate ApplyConfiguration anchor in '{dbContextPath}'.");
            }

            dbContextLines.Insert(
                configIdx + 1,
                ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "ConfigEntry"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk));

            modifiedEntries.Add((dbContextPath, dbContextLines));

            if (mapper == MapperKind.AutoMapper)
            {
                var profileLines = await ServiceFileHelper.ReadLinesAsync(mappingProfilePath, ct).ConfigureAwait(false);
                var lastSemicolonIdx = profileLines.FindLastIndex(l => l.EndsWith(";", StringComparison.Ordinal));
                if (lastSemicolonIdx < 0)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.TEMPLATE_ERROR,
                        $"Could not locate mapping registration anchor in '{mappingProfilePath}'.");
                }

                profileLines.Insert(
                    lastSemicolonIdx + 1,
                    ApplyTokens(_templateProvider.GetTemplate(info.SolutionPath, mapper, "Config"), info.SolutionName, entityName, entityVarName, entitySetName, normalizedPk));

                modifiedEntries.Add((mappingProfilePath, profileLines));
            }

            if (!request.DryRun)
            {
                var (manifest, expectedHash, loadError) = await ServiceFileHelper.LoadOrCreateManifestAsync(
                    _manifestStore, info, request.Mapper, ct).ConfigureAwait(false);
                if (loadError is not null || manifest is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        loadError ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Failed to load manifest."));
                }

                foreach (var (path, content) in createdEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    await ServiceFileHelper.WriteFileAsync(path, content, ct).ConfigureAwait(false);
                    ServiceFileHelper.Report(progress, GenerationStage.Scaffolding, $"Created {Path.GetFileName(path)}", path);
                }

                foreach (var (path, lines) in modifiedEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    await ServiceFileHelper.WriteLinesAsync(path, lines, ct).ConfigureAwait(false);
                    ServiceFileHelper.Report(progress, GenerationStage.Updating, $"Updated {Path.GetFileName(path)}", path);
                }

                var entities = manifest.Entities.Select(e => e with { Properties = [.. e.Properties] }).ToList();
                var existingIdx = entities.FindIndex(e => string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase));
                if (existingIdx >= 0)
                {
                    entities[existingIdx] = entities[existingIdx] with
                    {
                        PkType = normalizedPk,
                        HasService = true,
                        HasApi = request.GenerateApi,
                        Applied = true
                    };
                }
                else
                {
                    entities.Add(new ManifestEntity(entityName, normalizedPk, true, request.GenerateApi, Applied: true, []));
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
                $"Entity '{entityName}' {(request.DryRun ? "planned" : "generated")} successfully.",
                entityPath);

            return OperationResult<GenerationSummary>.Success(new GenerationSummary(
                createdEntries.Select(e => e.Path).ToArray(),
                modifiedEntries.Select(e => e.Path).ToArray(),
                Array.Empty<string>(),
                request.DryRun));
        }
        catch (OperationCanceledException)
        {
            ServiceFileHelper.Report(progress, GenerationStage.Failed, "Entity generation was cancelled.");
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Entity generation was cancelled.");
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "Lock timeout while generating entity '{EntityName}'.", entityName);
            ServiceFileHelper.Report(progress, GenerationStage.Failed, ex.Message);
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.IO_ERROR, ex.Message, ex.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to generate entity '{EntityName}'.", entityName);
            ServiceFileHelper.Report(progress, GenerationStage.Failed, ex.Message);
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to generate entity '{entityName}': {ex.Message}",
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

    private static string ApplyTokens(
        string template,
        string solutionName,
        string entityName,
        string entityVarName,
        string entitySetName,
        string pkType) =>
        template
            .Replace("$SolutionName", solutionName, StringComparison.Ordinal)
            .Replace("$EntityName", entityName, StringComparison.Ordinal)
            .Replace("$EntityVarName", entityVarName, StringComparison.Ordinal)
            .Replace("$EntitySetName", entitySetName, StringComparison.Ordinal)
            .Replace("$PK", pkType, StringComparison.Ordinal);
}
