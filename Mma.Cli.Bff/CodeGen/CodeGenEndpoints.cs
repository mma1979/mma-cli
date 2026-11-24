using Mma.Cli.Bff.Projects;
using Mma.Cli.Shared.Exceptions;

namespace Mma.Cli.Bff.CodeGen;

public static class CodeGenEndpoints
{
    public static void MapCodeGenEndpoints(this WebApplication app)
    {
        app.MapPost("/api/projects/{id:int}/generate/preview", async (
            int id,
            MmaCodeGenOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await orchestrator.PreviewAsync(id, ct));
            }
            catch (MmaCliException ex)
            {
                return ProjectEndpoints.MapCliError(ex);
            }
        });

        app.MapPost("/api/projects/{id:int}/generate/apply", async (
            int id,
            CodeGenApplyRequest? request,
            MmaCodeGenOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            try
            {
                var result = await orchestrator.ApplyAsync(id, request ?? new CodeGenApplyRequest(), ct);
                return Results.Ok(result);
            }
            catch (MmaCliException ex)
            {
                return ProjectEndpoints.MapCliError(ex);
            }
        });
    }
}
