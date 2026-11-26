using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Mma.Cli.Bff.CodeGen;
using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Data;
using Mma.Cli.Shared.Exceptions;
using Mma.Cli.Shared.Helpers;
using Mma.Cli.Shared.Models;
using Mma.Cli.Shared.Solutions;

using Newtonsoft.Json;

namespace Mma.Cli.Bff.Projects;

public sealed partial class ProjectService(
    CliDbContext dbContext,
    IEfMigrationRunner efMigrationRunner,
    IDotnetTemplateRunner templateRunner)
{
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex ValidSolutionNameRegex();

    public async Task<IReadOnlyList<ProjectSummaryDto>> ListProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = await dbContext.Projects
            .AsNoTracking()
            .Include(p => p.Entities)
                .ThenInclude(e => e.Rows)
            .OrderByDescending(p => p.UpdatedAtUtc)
            .ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        return projects.Select(ToSummaryDto).ToList();
    }

    public async Task<ProjectSummaryDto> CreateProjectAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var parentDir = request.ParentDirectory?.Trim() ?? string.Empty;

        if (!ValidSolutionNameRegex().IsMatch(name))
        {
            throw new MmaCliException(
                "invalid_solution_name",
                "Solution name must start with a letter and contain only letters, digits, dots, hyphens, or underscores.",
                exitCode: 2);
        }

        if (string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir))
        {
            throw new MmaCliException("path_not_found", $"Parent directory '{parentDir}' does not exist.", exitCode: 66);
        }

        var normalizedParent = Path.GetFullPath(parentDir);
        var solutionPath = Path.Combine(normalizedParent, name);

        if (await dbContext.Projects.AnyAsync(p => p.Path == solutionPath, cancellationToken))
        {
            throw new MmaCliException("project_exists", $"A project is already registered at '{solutionPath}'.", exitCode: 1);
        }

        if (Directory.Exists(solutionPath) && Directory.EnumerateFileSystemEntries(solutionPath).Any())
        {
            throw new MmaCliException("directory_not_empty", $"Target directory '{solutionPath}' already exists and is not empty.", exitCode: 1);
        }

        var mapper = string.Equals(request.Mapper?.Trim(), Mappers.Mapster, StringComparison.OrdinalIgnoreCase)
            ? Mappers.Mapster
            : Mappers.AutoMapper;

        var builder = SolutionBuilder.New(name, mapper)
            .CreateSolutionDirectory(normalizedParent);
        await builder.ScaffoldFromTemplateAsync(templateRunner, cancellationToken: cancellationToken);
        builder.CreateMmaFolder();

        var project = new ProjectModel
        {
            Name = name,
            Path = solutionPath,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (request.RunInitialMigration)
        {
            var migrationResult = await efMigrationRunner.AddMigrationAsync(solutionPath, name, "InitialMigration", cancellationToken);
            if (!migrationResult.Succeeded)
            {
                throw new MmaCliException(
                    "ef_migration_failed",
                    migrationResult.Error ?? "Initial EF Core migration failed.",
                    exitCode: 1);
            }
        }

        return ToSummaryDto(project);
    }

    public async Task<ProjectSummaryDto> ImportProjectAsync(ImportProjectRequest request, CancellationToken cancellationToken)
    {
        var rawPath = request.SolutionPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new MmaCliException("invalid_solution_path", "Solution path is required.", exitCode: 2);
        }

        var fullPath = Path.GetFullPath(rawPath);
        var (solutionName, _) = BuildHelper.CheckSolutionPath(fullPath);

        var existing = await dbContext.Projects
            .Include(p => p.Entities)
                .ThenInclude(e => e.Rows)
            .FirstOrDefaultAsync(p => p.Path == fullPath, cancellationToken);

        if (existing is not null)
        {
            return ToSummaryDto(existing);
        }

        var schemaJsonPath = Path.Combine(fullPath, ".mma", "schema.json");
        var legacyDumpPath = Path.Combine(fullPath, ".mma", "project.mma");

        var project = new ProjectModel
        {
            Name = solutionName,
            Path = fullPath,
            SchemaJson = File.Exists(schemaJsonPath) ? await File.ReadAllTextAsync(schemaJsonPath, cancellationToken) : null,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (File.Exists(legacyDumpPath))
        {
            var dumpText = await File.ReadAllTextAsync(legacyDumpPath, cancellationToken);
            var dump = JsonConvert.DeserializeObject<ProjectDumpModel>(dumpText);
            if (dump?.Entities is { Count: > 0 })
            {
                var entityIdMap = new Dictionary<int, int>();

                foreach (var legacyEntity in dump.Entities)
                {
                    var newEntity = new EntityModel
                    {
                        ProjectId = project.Id,
                        EntityName = legacyEntity.EntityName,
                        PkType = string.IsNullOrWhiteSpace(legacyEntity.PkType) ? PkTypes.GUID : legacyEntity.PkType,
                        HasService = legacyEntity.HasService,
                        HasApi = legacyEntity.HasApi,
                        ServiceMethods = legacyEntity.ServiceMethods,
                        Applied = legacyEntity.Applied,
                    };

                    dbContext.Entities.Add(newEntity);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    entityIdMap[legacyEntity.Id] = newEntity.Id;
                }

                if (dump.Rows is { Count: > 0 })
                {
                    foreach (var row in dump.Rows)
                    {
                        if (row.EntityId is int oldEntityId && entityIdMap.TryGetValue(oldEntityId, out var newEntityId))
                        {
                            dbContext.EntityRows.Add(new EntityRowModel
                            {
                                EntityId = newEntityId,
                                ColumnName = row.ColumnName,
                                DataType = row.DataType,
                                Nullable = row.Nullable,
                                Applied = row.Applied,
                                IsForeignKey = row.IsForeignKey,
                            });
                        }
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                if (dump.Relations is { Count: > 0 })
                {
                    foreach (var rel in dump.Relations)
                    {
                        if (rel.ParentId is int oldParent && rel.ChildId is int oldChild &&
                            entityIdMap.TryGetValue(oldParent, out var newParent) &&
                            entityIdMap.TryGetValue(oldChild, out var newChild))
                        {
                            dbContext.Relations.Add(new RelationModel
                            {
                                Name = rel.Name,
                                Applied = rel.Applied,
                                ParentId = newParent,
                                ChildId = newChild,
                            });
                        }
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }

        var loaded = await dbContext.Projects
            .AsNoTracking()
            .Include(p => p.Entities)
                .ThenInclude(e => e.Rows)
            .FirstAsync(p => p.Id == project.Id, cancellationToken);

        return ToSummaryDto(loaded);
    }

    public async Task<bool> DeleteProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return false;
        }

        dbContext.Projects.Remove(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static ProjectSummaryDto ToSummaryDto(ProjectModel project)
    {
        var exists = !string.IsNullOrWhiteSpace(project.Path) && Directory.Exists(project.Path);
        var mapper = exists ? BuildHelper.DetectMapper(project.Path) : Mappers.AutoMapper;
        var entityCount = project.Entities?.Count ?? 0;
        var pendingEntities = project.Entities?.Count(e => !e.Applied) ?? 0;
        var pendingRows = project.Entities?.Sum(e => e.Rows?.Count(r => !r.Applied && !r.IsForeignKey) ?? 0) ?? 0;

        return new ProjectSummaryDto(
            project.Id,
            project.Name,
            project.Path,
            mapper,
            exists,
            entityCount,
            pendingEntities + pendingRows,
            project.UpdatedAtUtc);
    }
}
