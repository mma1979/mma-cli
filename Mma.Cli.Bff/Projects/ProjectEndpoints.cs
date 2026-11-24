using System.Diagnostics;

using Mma.Cli.Bff.CodeGen;
using Mma.Cli.Shared.Exceptions;

namespace Mma.Cli.Bff.Projects;

public static class ProjectEndpoints
{
    public static void MapProjectAndFileSystemEndpoints(this WebApplication app)
    {
        app.MapGet("/api/fs/browse", (string? path, FileSystemService fs) =>
        {
            try
            {
                return Results.Ok(fs.Browse(path));
            }
            catch (MmaCliException ex)
            {
                return MapCliError(ex);
            }
        });

        app.MapPost("/api/fs/mkdir", (MkdirRequest request, FileSystemService fs) =>
        {
            try
            {
                var created = fs.CreateDirectory(request);
                return Results.Created(string.Empty, created);
            }
            catch (MmaCliException ex)
            {
                return MapCliError(ex);
            }
        });

        app.MapGet("/api/projects", async (ProjectService service, CancellationToken ct) =>
            Results.Ok(await service.ListProjectsAsync(ct)));

        app.MapPost("/api/projects", async (CreateProjectRequest request, ProjectService service, CancellationToken ct) =>
        {
            try
            {
                var created = await service.CreateProjectAsync(request, ct);
                return Results.Created($"/api/projects/{created.Id}", created);
            }
            catch (MmaCliException ex)
            {
                return MapCliError(ex);
            }
        });

        app.MapPost("/api/projects/import", async (ImportProjectRequest request, ProjectService service, CancellationToken ct) =>
        {
            try
            {
                var imported = await service.ImportProjectAsync(request, ct);
                return Results.Ok(imported);
            }
            catch (MmaCliException ex)
            {
                return MapCliError(ex);
            }
        });

        app.MapDelete("/api/projects/{id:int}", async (int id, ProjectService service, CancellationToken ct) =>
        {
            var deleted = await service.DeleteProjectAsync(id, ct);
            return deleted
                ? Results.NoContent()
                : Results.Json(
                    new ApiErrorDto($"Project {id} was not found.", "project_not_found", TraceId()),
                    statusCode: StatusCodes.Status404NotFound);
        });

        app.MapGet("/api/projects/{id:int}/schema", async (int id, SchemaSyncService syncService, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await syncService.GetProjectSchemaAsync(id, ct));
            }
            catch (MmaCliException ex)
            {
                return MapCliError(ex);
            }
        });

        app.MapPut("/api/projects/{id:int}/schema", async (int id, SchemaModelDto schema, SchemaSyncService syncService, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await syncService.SaveProjectSchemaAsync(id, schema, ct));
            }
            catch (MmaCliException ex)
            {
                return MapCliError(ex);
            }
        });
    }

    internal static IResult MapCliError(MmaCliException ex)
    {
        var statusCode = ex.Code switch
        {
            "project_not_found" or "path_not_found" => StatusCodes.Status404NotFound,
            "project_exists" or "directory_not_empty" => StatusCodes.Status409Conflict,
            "access_denied" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Json(new ApiErrorDto(ex.Message, ex.Code, TraceId()), statusCode: statusCode);
    }

    private static string TraceId() => Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
}
