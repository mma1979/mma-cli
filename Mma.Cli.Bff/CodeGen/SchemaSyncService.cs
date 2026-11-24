using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Mma.Cli.Bff.Projects;
using Mma.Cli.Shared.Data;
using Mma.Cli.Shared.Exceptions;

namespace Mma.Cli.Bff.CodeGen;

public sealed class SchemaSyncService(CliDbContext dbContext)
{
    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async Task<ProjectSchemaResponseDto> GetProjectSchemaAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .AsNoTracking()
            .Include(p => p.Entities)
                .ThenInclude(e => e.Rows)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new MmaCliException("project_not_found", $"Project {projectId} was not found.", exitCode: 66);

        SchemaModelDto? schema = null;
        if (!string.IsNullOrWhiteSpace(project.SchemaJson))
        {
            schema = JsonSerializer.Deserialize<SchemaModelDto>(project.SchemaJson, SchemaJsonOptions);
        }

        if (schema is null && !string.IsNullOrWhiteSpace(project.Path))
        {
            var diskSchemaPath = Path.Combine(project.Path, ".mma", "schema.json");
            if (File.Exists(diskSchemaPath))
            {
                var diskJson = await File.ReadAllTextAsync(diskSchemaPath, cancellationToken);
                schema = JsonSerializer.Deserialize<SchemaModelDto>(diskJson, SchemaJsonOptions);
            }
        }

        if (schema is null)
        {
            var entityIds = project.Entities.Select(e => e.Id).ToHashSet();
            var relations = await dbContext.Relations
                .AsNoTracking()
                .Where(r => r.ParentId.HasValue && entityIds.Contains(r.ParentId.Value))
                .ToListAsync(cancellationToken);

            schema = MmaSchemaMapper.BuildSchemaModelFromLegacy(project, project.Entities, relations);
        }

        var syncState = await MmaSchemaMapper.ComputeSyncStateAsync(dbContext, projectId, cancellationToken);
        return new ProjectSchemaResponseDto(ProjectService.ToSummaryDto(project), schema, syncState);
    }

    public async Task<SaveSchemaResponseDto> SaveProjectSchemaAsync(
        int projectId,
        SchemaModelDto schema,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new MmaCliException("project_not_found", $"Project {projectId} was not found.", exitCode: 66);

        var now = DateTime.UtcNow;
        var normalizedSchema = schema with { UpdatedAt = now.ToString("O") };
        var serialized = JsonSerializer.Serialize(normalizedSchema, SchemaJsonOptions);

        project.SchemaJson = serialized;
        project.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var syncState = await MmaSchemaMapper.SyncSchemaToDbContextAsync(dbContext, projectId, normalizedSchema, cancellationToken);

        if (!string.IsNullOrWhiteSpace(project.Path) && Directory.Exists(project.Path))
        {
            var mmaDir = Path.Combine(project.Path, ".mma");
            Directory.CreateDirectory(mmaDir);
            await File.WriteAllTextAsync(Path.Combine(mmaDir, "schema.json"), serialized, cancellationToken);
            dbContext.DumpProject(projectId, project.Path);
        }

        return new SaveSchemaResponseDto(now, syncState);
    }
}
