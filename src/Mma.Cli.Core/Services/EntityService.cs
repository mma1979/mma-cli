using System.Text;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.CodeGen;
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
    private readonly ITemplateRenderer _templateRenderer;
    private readonly IDryRunDiffEngine _diffEngine;

    public EntityService(
        ISolutionLocator solutionLocator,
        ITemplateProvider templateProvider,
        ILogger<EntityService>? logger = null,
        ISolutionLockManager? lockManager = null,
        IManifestStore? manifestStore = null,
        ITemplateRenderer? templateRenderer = null,
        IDryRunDiffEngine? diffEngine = null)
    {
        ArgumentNullException.ThrowIfNull(solutionLocator);
        ArgumentNullException.ThrowIfNull(templateProvider);

        _solutionLocator = solutionLocator;
        _templateProvider = templateProvider;
        _logger = logger;
        _lockManager = lockManager ?? new SolutionLockManager();
        _manifestStore = manifestStore ?? new ManifestStore(_lockManager, solutionLocator);
        _templateRenderer = templateRenderer ?? new ScribanTemplateRenderer(_templateProvider);
        _diffEngine = diffEngine ?? new DryRunDiffEngine();
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

            var templateModel = new TemplateModel(
                info.SolutionName,
                entityName,
                entityVarName,
                entitySetName,
                normalizedPk,
                mapper,
                Properties: [],
                Relations: []);

            var createdTemplates = new List<(string Path, string TemplateName)>
            {
                (modifyModelPath, "ModifyModel"),
                (readModelPath, "ReadModel"),
                (validatorPath, "Validator"),
                (entityPath, "Entity"),
                (entityConfigPath, "EntityConfig"),
                (servicePath, "Service")
            };

            if (request.GenerateApi)
            {
                createdTemplates.Add((controllerPath, "Controller"));
            }

            var createdEntries = new List<(string Path, string Content)>(createdTemplates.Count);
            foreach (var (path, templateName) in createdTemplates)
            {
                var rendered = _templateRenderer.RenderTemplate(info.SolutionPath, mapper, templateName, templateModel);
                if (!rendered.IsSuccess || rendered.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        rendered.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, $"Failed to render '{templateName}'."));
                }

                createdEntries.Add((path, rendered.Value));
            }

            var modifiedEntries = new List<(string Path, string Content)>();

            var (dbContextLines, dbContextNewLine, dbContextTrailingNewline) =
                await ReadLinesPreservingFormatAsync(dbContextPath, ct).ConfigureAwait(false);

            var dbSetIdx = dbContextLines.FindLastIndex(l => l.Contains("public virtual DbSet<", StringComparison.Ordinal));
            if (dbSetIdx < 0)
            {
                return OperationResult<GenerationSummary>.Failure(
                    ErrorCodes.TEMPLATE_ERROR,
                    $"Could not locate DbSet anchor in '{dbContextPath}'.");
            }

            var dbSetRendered = _templateRenderer.RenderTemplate(info.SolutionPath, mapper, "DbSetEntry", templateModel);
            if (!dbSetRendered.IsSuccess || dbSetRendered.Value is null)
            {
                return OperationResult<GenerationSummary>.Failure(
                    dbSetRendered.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, "Failed to render 'DbSetEntry'."));
            }

            dbContextLines.Insert(dbSetIdx + 1, dbSetRendered.Value);

            var configIdx = dbContextLines.FindLastIndex(l => l.Contains("modelBuilder.ApplyConfiguration(new", StringComparison.Ordinal));
            if (configIdx < 0)
            {
                return OperationResult<GenerationSummary>.Failure(
                    ErrorCodes.TEMPLATE_ERROR,
                    $"Could not locate ApplyConfiguration anchor in '{dbContextPath}'.");
            }

            var configEntryRendered = _templateRenderer.RenderTemplate(info.SolutionPath, mapper, "ConfigEntry", templateModel);
            if (!configEntryRendered.IsSuccess || configEntryRendered.Value is null)
            {
                return OperationResult<GenerationSummary>.Failure(
                    configEntryRendered.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, "Failed to render 'ConfigEntry'."));
            }

            dbContextLines.Insert(configIdx + 1, configEntryRendered.Value);
            modifiedEntries.Add((dbContextPath, JoinLinesPreservingFormat(dbContextLines, dbContextNewLine, dbContextTrailingNewline)));

            if (mapper == MapperKind.AutoMapper)
            {
                var (profileLines, profileNewLine, profileTrailingNewline) =
                    await ReadLinesPreservingFormatAsync(mappingProfilePath, ct).ConfigureAwait(false);

                var lastSemicolonIdx = profileLines.FindLastIndex(l => l.EndsWith(";", StringComparison.Ordinal));
                if (lastSemicolonIdx < 0)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.TEMPLATE_ERROR,
                        $"Could not locate mapping registration anchor in '{mappingProfilePath}'.");
                }

                var autoMapperConfigRendered = _templateRenderer.RenderTemplate(info.SolutionPath, mapper, "Config", templateModel);
                if (!autoMapperConfigRendered.IsSuccess || autoMapperConfigRendered.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        autoMapperConfigRendered.Error ?? new GenerationError(ErrorCodes.TEMPLATE_ERROR, "Failed to render 'Config'."));
                }

                var normalizedConfig = autoMapperConfigRendered.Value
                    .Replace("\r\n", profileNewLine, StringComparison.Ordinal);
                profileLines.Insert(lastSemicolonIdx + 1, normalizedConfig);
                modifiedEntries.Add((mappingProfilePath, JoinLinesPreservingFormat(profileLines, profileNewLine, profileTrailingNewline)));
            }

            var stagedChanges = new List<StagedFileChange>(createdEntries.Count + modifiedEntries.Count);
            foreach (var (path, content) in createdEntries)
            {
                stagedChanges.Add(new StagedFileChange(path, FileChangeKind.Create, content));
            }

            foreach (var (path, content) in modifiedEntries)
            {
                stagedChanges.Add(new StagedFileChange(path, FileChangeKind.Modify, content));
            }

            var planResult = await _diffEngine.ComputePlanAsync(info.SolutionPath, stagedChanges, ct).ConfigureAwait(false);
            if (!planResult.IsSuccess || planResult.Value is null)
            {
                return OperationResult<GenerationSummary>.Failure(
                    planResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to compute generation plan."));
            }

            var plan = planResult.Value;
            var createdPaths = createdEntries.Select(e => e.Path).ToArray();
            var modifiedPaths = modifiedEntries.Select(e => e.Path).ToArray();

            if (!request.DryRun)
            {
                var (manifest, expectedHash, loadError) = await ServiceFileHelper.LoadOrCreateManifestAsync(
                    _manifestStore, info, request.Mapper, ct).ConfigureAwait(false);
                if (loadError is not null || manifest is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        loadError ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Failed to load manifest."));
                }

                var commitResult = await _diffEngine.CommitAsync(plan, ct).ConfigureAwait(false);
                if (!commitResult.IsSuccess || commitResult.Value is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        commitResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to commit entity files."));
                }

                foreach (var path in createdPaths)
                {
                    ServiceFileHelper.Report(progress, GenerationStage.Scaffolding, $"Created {Path.GetFileName(path)}", path);
                }

                foreach (var path in modifiedPaths)
                {
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
                createdPaths,
                modifiedPaths,
                Array.Empty<string>(),
                request.DryRun,
                Plan: plan));
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

    private static async Task<(List<string> Lines, string NewLine, bool HasTrailingNewline)> ReadLinesPreservingFormatAsync(
        string path,
        CancellationToken ct)
    {
        var bytes = await ServiceFileHelper.ReadFileBytesSharedAsync(path, ct).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(ServiceFileHelper.StripUtf8Bom(bytes).Span);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var hasTrailingNewline = text.EndsWith('\n');
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (hasTrailingNewline)
        {
            normalized = normalized[..^1];
        }

        var lines = normalized.Length == 0 && !hasTrailingNewline
            ? []
            : normalized.Split('\n').ToList();

        return (lines, newLine, hasTrailingNewline);
    }

    private static string JoinLinesPreservingFormat(IReadOnlyList<string> lines, string newLine, bool hasTrailingNewline)
    {
        if (lines.Count == 0)
        {
            return hasTrailingNewline ? newLine : string.Empty;
        }

        var joined = string.Join(newLine, lines);
        return hasTrailingNewline ? joined + newLine : joined;
    }
}
