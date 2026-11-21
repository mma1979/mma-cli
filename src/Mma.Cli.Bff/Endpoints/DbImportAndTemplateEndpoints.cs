using System.Text;
using System.Text.RegularExpressions;
using Mma.Cli.Bff.Models;
using Mma.Cli.Bff.Security;
using Mma.Cli.Bff.Services;
using Mma.Cli.Core.CodeGen;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Core.Templates;
using Mma.Cli.Database.Entities;
using Mma.Cli.Database.Introspection;

namespace Mma.Cli.Bff.Endpoints;

public static partial class DbImportAndTemplateEndpoints
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly string[] CanonicalTemplateNames =
    [
        "ModifyModel",
        "ReadModel",
        "Entity",
        "Service",
        "Controller",
        "Validator",
        "EntityConfig",
        "Config",
        "DbSetEntry",
        "ConfigEntry"
    ];

    private static readonly Dictionary<string, string> AllowedTemplateMap = CanonicalTemplateNames
        .ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);

    public static IEndpointRouteBuilder MapDbImportAndTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Database Import & Templates");

        group.MapPost("/db-import/test", async (
            DbImportConnectionRequest request,
            WorkspacePathGuard pathGuard,
            IDatabaseIntrospectorFactory introspectorFactory,
            CancellationToken ct) =>
        {
            if (request.Provider == DatabaseProviderKind.Sqlite)
            {
                var pathCheck = pathGuard.ValidateSqlitePathIfLocal(request.Parameters?.ServerOrPath, request.ConnectionString);
                if (!pathCheck.IsSuccess)
                {
                    return BffProblemDetails.ToResult(pathCheck.Error, "SQLite path is outside allowed roots.");
                }
            }

            var introspector = introspectorFactory.GetIntrospector(request.Provider);
            var result = await introspector.TestConnectionAsync(
                new DatabaseIntrospectRequest(request.Provider, request.ConnectionString, request.Parameters),
                ct).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                var safeDetail = RedactSecret(result.Error?.Message ?? "Connection test failed.", request.Parameters?.Password);
                return BffProblemDetails.ToResult(result.Error?.Code ?? ErrorCodes.IO_ERROR, safeDetail);
            }

            return Results.Ok(new DbImportTestResponse(true, request.Provider.ToString()));
        })
        .WithName("TestDatabaseConnection")
        .Produces<DbImportTestResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/db-import/introspect", async (
            DbIntrospectApiRequest request,
            WorkspacePathGuard pathGuard,
            IDatabaseIntrospectorFactory introspectorFactory,
            CancellationToken ct) =>
        {
            if (request.Provider == DatabaseProviderKind.Sqlite)
            {
                var pathCheck = pathGuard.ValidateSqlitePathIfLocal(request.Parameters?.ServerOrPath, request.ConnectionString);
                if (!pathCheck.IsSuccess)
                {
                    return BffProblemDetails.ToResult(pathCheck.Error, "SQLite path is outside allowed roots.");
                }
            }

            var introspector = introspectorFactory.GetIntrospector(request.Provider);
            var result = await introspector.IntrospectAsync(
                new DatabaseIntrospectRequest(request.Provider, request.ConnectionString, request.Parameters, request.Filters),
                ct).ConfigureAwait(false);

            if (!result.IsSuccess || result.Value is null)
            {
                var safeDetail = RedactSecret(result.Error?.Message ?? "Database introspection failed.", request.Parameters?.Password);
                return BffProblemDetails.ToResult(result.Error?.Code ?? ErrorCodes.IO_ERROR, safeDetail);
            }

            return Results.Ok(result.Value);
        })
        .WithName("IntrospectDatabase")
        .Produces<DatabaseSchemaSnapshot>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/projects/{id:int}/db-import/apply", async (
            int id,
            DbImportApplyRequest request,
            BffProjectMutationService mutationService,
            CancellationToken ct) =>
        {
            var result = await mutationService.ApplyDbImportAsync(id, request, ct).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is null)
            {
                var safeDetail = RedactSecret(result.Error?.Message ?? "Database import apply failed.", request.Parameters?.Password);
                return BffProblemDetails.ToResult(result.Error?.Code ?? ErrorCodes.IO_ERROR, safeDetail);
            }

            return Results.Ok(result.Value);
        })
        .WithName("ApplyDatabaseImport")
        .Produces<DbImportApplyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/projects/{id:int}/templates", async (
            int id,
            BffProjectMutationService mutationService,
            IManifestStore manifestStore,
            ITemplateProvider templateProvider,
            CancellationToken ct) =>
        {
            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (_, info) = resolved.Value;
            var manifestDoc = await manifestStore.LoadAsync(info.SolutionPath, ct).ConfigureAwait(false);
            var activeMapper = manifestDoc.IsSuccess && manifestDoc.Value is not null
                && MapperKindParser.TryParse(manifestDoc.Value.Manifest.Mapper, out var parsed)
                ? parsed
                : info.DetectedMapper;

            var mapperFolder = activeMapper.ToFolderName();
            var items = new List<TemplateItemDto>();

            foreach (var name in CanonicalTemplateNames)
            {
                if (activeMapper == MapperKind.Mapster && string.Equals(name, "Config", StringComparison.Ordinal))
                {
                    continue;
                }

                var overridePath = FindOverridePath(info.SolutionPath, mapperFolder, name);
                var content = templateProvider.GetTemplate(info.SolutionPath, activeMapper, name);

                items.Add(new TemplateItemDto(
                    mapperFolder,
                    name,
                    overridePath is not null,
                    overridePath,
                    content));
            }

            return Results.Ok(new TemplateListResponse(mapperFolder, items));
        })
        .WithName("ListProjectTemplates")
        .Produces<TemplateListResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/projects/{id:int}/templates/{mapper}/{name}", async (
            int id,
            string mapper,
            string name,
            UpdateTemplateRequest request,
            BffProjectMutationService mutationService,
            ITemplateRenderer templateRenderer,
            ISolutionLockManager lockManager,
            CancellationToken ct) =>
        {
            if (!MapperKindParser.TryParse(mapper, out var mapperKind))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Invalid mapper '{mapper}'. Supported values: AutoMapper, Mapster.");
            }

            if (!AllowedTemplateMap.TryGetValue(name.Trim(), out var canonicalName))
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    $"Template name '{name}' is not in the allowed template list.");
            }

            if (request.Content is null)
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.VALIDATION_ERROR,
                    "Template content cannot be null.");
            }

            var resolved = await mutationService.ResolveWorkspaceAsync(id, ct).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return BffProblemDetails.ToResult(resolved.Error, "Project not found.");
            }

            var (_, info) = resolved.Value;
            var sampleModel = new TemplateModel(
                info.SolutionName,
                "SampleEntity",
                "sampleEntity",
                "SampleEntities",
                "Guid",
                mapperKind,
                Properties: [new ManifestProperty("Title", "string", false, false, false, 255, true)],
                Relations: [new ManifestRelation("Parent_SampleEntity_ParentId", "Parent", "SampleEntity", "ParentId", "Guid", "1:N", "Cascade", false)]);

            var renderCheck = templateRenderer.Render(request.Content, sampleModel);
            if (!renderCheck.IsSuccess || renderCheck.Value is null)
            {
                return BffProblemDetails.ToResult(
                    ErrorCodes.TEMPLATE_ERROR,
                    renderCheck.Error?.Message ?? "Template failed Scriban compilation.");
            }

            var mapperFolder = mapperKind.ToFolderName();
            var targetDir = Path.Combine(info.SolutionPath, ".mma", "templates", mapperFolder);
            var targetFile = Path.Combine(targetDir, $"{canonicalName}.scriban");
            var tmpFile = Path.Combine(targetDir, $"{canonicalName}.{Guid.NewGuid():N}.tmp");

            await using var _ = await lockManager.AcquireAsync(info.SolutionPath, ct: ct).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(targetDir);
                await File.WriteAllTextAsync(tmpFile, request.Content, Utf8NoBom, ct).ConfigureAwait(false);
                File.Move(tmpFile, targetFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return BffProblemDetails.ToResult(ErrorCodes.IO_ERROR, $"Failed writing template override: {ex.Message}");
            }
            finally
            {
                if (File.Exists(tmpFile))
                {
                    try
                    {
                        File.Delete(tmpFile);
                    }
                    catch (IOException)
                    {
                        // Best-effort temp cleanup.
                    }
                }
            }

            return Results.Ok(new UpdateTemplateResponse(
                mapperFolder,
                canonicalName,
                IsCustomOverride: true,
                request.Content,
                renderCheck.Value));
        })
        .WithName("UpdateProjectTemplate")
        .Produces<UpdateTemplateResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static string? FindOverridePath(string solutionPath, string mapperFolder, string templateName)
    {
        var candidates = new[]
        {
            Path.Combine(solutionPath, ".mma", "templates", mapperFolder, $"{templateName}.scriban"),
            Path.Combine(solutionPath, ".mma", "templates", mapperFolder.ToLowerInvariant(), $"{templateName}.scriban"),
            Path.Combine(solutionPath, ".mma", "templates", mapperFolder, $"{templateName}.cs"),
            Path.Combine(solutionPath, ".mma", "templates", mapperFolder.ToLowerInvariant(), $"{templateName}.cs")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    [GeneratedRegex(@"(?<key>(?:password|pwd|secret|token)\s*=\s*)(?<val>'[^']*'|""[^""]*""|[^;\r\n]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretPairRegex();

    private static string RedactSecret(string message, string? password)
    {
        var redacted = SecretPairRegex().Replace(message, "${key}***");
        if (!string.IsNullOrEmpty(password))
        {
            redacted = redacted.Replace(password, "***", StringComparison.Ordinal);
        }

        return redacted;
    }
}
