using Microsoft.EntityFrameworkCore;

using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Data;
using Mma.Cli.Shared.Exceptions;
using Mma.Cli.Shared.Helpers;
using Mma.Cli.Shared.Models;

namespace Mma.Cli.Bff.CodeGen;

public sealed record PendingEntityDto(string EntityName, string PkType, bool HasApi);

public sealed record PendingPropertyDto(string EntityName, string ColumnName, string DataType, bool Nullable);

public sealed record PendingRelationDto(string ParentEntity, string ChildEntity, string ForeignKey, string ForeignKeyType);

public sealed record CodeGenPreviewDto(
    IReadOnlyList<PendingEntityDto> PendingEntities,
    IReadOnlyList<PendingPropertyDto> PendingProperties,
    IReadOnlyList<PendingRelationDto> PendingRelations,
    IReadOnlyList<string> EquivalentCommands);

public sealed record CodeGenApplyRequest(
    bool RunEfMigration = false,
    string? MigrationName = null);

public sealed record CodeGenStepLogDto(
    string Kind,
    string Target,
    bool Succeeded,
    string Message);

public sealed record CodeGenApplyResultDto(
    bool Succeeded,
    IReadOnlyList<CodeGenStepLogDto> Steps,
    SchemaSyncStateDto SyncState);

public sealed class MmaCodeGenOrchestrator(CliDbContext dbContext, IEfMigrationRunner efMigrationRunner)
{
    public async Task<CodeGenPreviewDto> PreviewAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new MmaCliException("project_not_found", $"Project {projectId} was not found.", exitCode: 66);

        var mapper = Directory.Exists(project.Path) ? BuildHelper.DetectMapper(project.Path) : Mappers.AutoMapper;

        var entities = await dbContext.Entities
            .AsNoTracking()
            .Include(e => e.Rows)
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.EntityName)
            .ToListAsync(cancellationToken);

        var entityIds = entities.Select(e => e.Id).ToHashSet();
        var relations = await dbContext.Relations
            .AsNoTracking()
            .Include(r => r.ParentEntity)
            .Include(r => r.ChiledEntity)
            .Where(r => !r.Applied && r.ParentId.HasValue && entityIds.Contains(r.ParentId.Value))
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var pendingEntities = new List<PendingEntityDto>();
        var pendingProperties = new List<PendingPropertyDto>();
        var pendingRelations = new List<PendingRelationDto>();
        var commands = new List<string>();

        foreach (var entity in entities)
        {
            if (!entity.Applied)
            {
                pendingEntities.Add(new PendingEntityDto(entity.EntityName, entity.PkType, entity.HasApi));
                var apiFlag = entity.HasApi ? string.Empty : " --no-api";
                commands.Add($"mma g e {entity.EntityName} {entity.PkType} --mapper {mapper}{apiFlag}");
            }

            foreach (var row in entity.Rows.Where(r => !r.Applied && !r.IsForeignKey && !string.IsNullOrWhiteSpace(r.ColumnName)))
            {
                var colType = string.IsNullOrWhiteSpace(row.DataType) ? PkTypes.STRING : row.DataType!;
                pendingProperties.Add(new PendingPropertyDto(entity.EntityName, row.ColumnName!, colType, row.Nullable));
                commands.Add($"mma g p {entity.EntityName} {row.ColumnName} {colType} {row.Nullable.ToString().ToLowerInvariant()}");
            }
        }

        foreach (var rel in relations)
        {
            if (rel.ParentEntity is null || rel.ChiledEntity is null)
            {
                continue;
            }

            var fkName = $"{rel.ParentEntity.EntityName}Id";
            pendingRelations.Add(new PendingRelationDto(
                rel.ParentEntity.EntityName,
                rel.ChiledEntity.EntityName,
                fkName,
                rel.ParentEntity.PkType));
            commands.Add($"mma g r {rel.ParentEntity.EntityName} {rel.ChiledEntity.EntityName} {fkName} {rel.ParentEntity.PkType}");
        }

        return new CodeGenPreviewDto(pendingEntities, pendingProperties, pendingRelations, commands);
    }

    public async Task<CodeGenApplyResultDto> ApplyAsync(
        int projectId,
        CodeGenApplyRequest request,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new MmaCliException("project_not_found", $"Project {projectId} was not found.", exitCode: 66);

        BuildHelper.CheckSolutionPath(project.Path);
        var mapper = BuildHelper.DetectMapper(project.Path);

        var entities = await dbContext.Entities
            .Include(e => e.Rows)
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);

        var entityIds = entities.Select(e => e.Id).ToHashSet();
        var relations = await dbContext.Relations
            .Include(r => r.ParentEntity)
            .Include(r => r.ChiledEntity)
            .Where(r => !r.Applied && r.ParentId.HasValue && entityIds.Contains(r.ParentId.Value))
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var steps = new List<CodeGenStepLogDto>();
        var allSucceeded = true;

        // 1. Sequentially generate unapplied entities
        foreach (var entity in entities.Where(e => !e.Applied))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var args = new List<string> { "g", "e", entity.EntityName, entity.PkType, Flags.MapperFlag, mapper };
                if (!entity.HasApi)
                {
                    args.Add(Flags.ApiFlag);
                }

                EntityBuilder.New(args.ToArray(), project.Path)
                    .GenerateModels()
                    .GenerateValidator()
                    .GenerateEntity()
                    .GenerateEntityConfig()
                    .DbContextMapping()
                    .GenerateService()
                    .GenerateController(entity.HasApi);

                entity.Applied = true;
                await dbContext.SaveChangesAsync(cancellationToken);
                steps.Add(new CodeGenStepLogDto("entity", entity.EntityName, true, $"Generated entity '{entity.EntityName}'."));
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                steps.Add(new CodeGenStepLogDto("entity", entity.EntityName, false, ex.Message));
            }
        }

        // 2. Sequentially generate unapplied non-FK properties on applied entities
        foreach (var entity in entities.Where(e => e.Applied))
        {
            foreach (var row in entity.Rows.Where(r => !r.Applied && !r.IsForeignKey && !string.IsNullOrWhiteSpace(r.ColumnName)).ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = $"{entity.EntityName}.{row.ColumnName}";
                try
                {
                    var colType = string.IsNullOrWhiteSpace(row.DataType) ? PkTypes.STRING : row.DataType!;
                    string[] args = ["g", "p", entity.EntityName, row.ColumnName!, colType, row.Nullable.ToString().ToLowerInvariant()];

                    PropertiesBuilder.New(args, mapper, project.Path)
                        .UpdateEntityModels()
                        .UpdateEntity()
                        .UpdateEntityConfig();

                    row.Applied = true;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    steps.Add(new CodeGenStepLogDto("property", target, true, $"Generated property '{target}'."));
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    steps.Add(new CodeGenStepLogDto("property", target, false, ex.Message));
                }
            }
        }

        // 3. Sequentially generate unapplied relations when both parent & child are applied
        foreach (var rel in relations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rel.ParentEntity is null || rel.ChiledEntity is null)
            {
                continue;
            }

            if (!rel.ParentEntity.Applied || !rel.ChiledEntity.Applied)
            {
                allSucceeded = false;
                steps.Add(new CodeGenStepLogDto(
                    "relation",
                    rel.Name,
                    false,
                    $"Skipped relation '{rel.Name}' because parent or child entity is not yet applied."));
                continue;
            }

            var target = $"{rel.ParentEntity.EntityName} -> {rel.ChiledEntity.EntityName}";
            try
            {
                string[] args =
                [
                    "g", "r",
                    rel.ParentEntity.EntityName,
                    rel.ChiledEntity.EntityName,
                    $"{rel.ParentEntity.EntityName}Id",
                    rel.ParentEntity.PkType,
                ];

                RelationsBuilder.New(args, project.Path)
                    .UpdateParentDtos()
                    .UpdateChildDtos()
                    .UpdateParentEntity()
                    .UpdateChildEntity()
                    .UpdateParentEntityConfig();

                rel.Applied = true;
                await dbContext.SaveChangesAsync(cancellationToken);
                steps.Add(new CodeGenStepLogDto("relation", target, true, $"Generated relation '{target}'."));
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                steps.Add(new CodeGenStepLogDto("relation", target, false, ex.Message));
            }
        }

        // 4. Refresh .mma/project.mma snapshot
        project.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.DumpProject(projectId, project.Path);

        // 5. Optional EF Core migration
        if (request.RunEfMigration && allSucceeded)
        {
            var shortName = project.Name.Split('.')[^1];
            var migrationName = string.IsNullOrWhiteSpace(request.MigrationName)
                ? $"{shortName}_{DateTime.UtcNow:yyyyMMddHHmmss}"
                : request.MigrationName.Trim();

            var efResult = await efMigrationRunner.AddMigrationAsync(project.Path, project.Name, migrationName, cancellationToken);
            if (!efResult.Succeeded)
            {
                allSucceeded = false;
            }

            steps.Add(new CodeGenStepLogDto(
                "ef-migration",
                migrationName,
                efResult.Succeeded,
                efResult.Succeeded
                    ? (string.IsNullOrWhiteSpace(efResult.Output) ? $"Created EF migration '{migrationName}'." : efResult.Output)
                    : (efResult.Error ?? "EF Core migration failed.")));
        }

        var syncState = await MmaSchemaMapper.ComputeSyncStateAsync(dbContext, projectId, cancellationToken);
        return new CodeGenApplyResultDto(allSucceeded, steps, syncState);
    }
}
