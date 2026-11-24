using Microsoft.EntityFrameworkCore;

using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Data;
using Mma.Cli.Shared.Models;

namespace Mma.Cli.Bff.CodeGen;

public static class MmaSchemaMapper
{
    public static string MapSqlTypeToCSharp(string? sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
        {
            return PkTypes.STRING;
        }

        var normalized = sqlType.Trim();
        var parenIdx = normalized.IndexOf('(');
        if (parenIdx > 0)
        {
            normalized = normalized[..parenIdx].Trim();
        }

        // Preserve already-canonical C# PkTypes.
        return normalized.ToLowerInvariant() switch
        {
            "guid" or "uuid" or "uniqueidentifier" => PkTypes.GUID,
            "int" or "integer" or "int4" or "int2" or "smallint" or "tinyint" or "mediumint" or "serial" => PkTypes.INT,
            "long" or "bigint" or "int8" or "bigserial" => PkTypes.LONG,
            "decimal" or "numeric" or "money" or "smallmoney" => PkTypes.DECIMAL,
            "float" or "double" or "double precision" or "real" or "float4" or "float8" => PkTypes.FLOAT,
            "bool" or "boolean" or "bit" => PkTypes.BOOL,
            "datetime" or "datetime2" or "datetimeoffset" or "smalldatetime" or "timestamp" or "timestamptz" or "timestamp with time zone" or "timestamp without time zone" or "date" => PkTypes.DATE_TIME,
            _ => PkTypes.STRING,
        };
    }

    public static string MapCSharpToSqlType(string? csharpType, string engine = "mssql")
    {
        var isPg = string.Equals(engine, "postgresql", StringComparison.OrdinalIgnoreCase);
        var isMySql = string.Equals(engine, "mysql", StringComparison.OrdinalIgnoreCase);

        return (csharpType?.Trim().ToLowerInvariant()) switch
        {
            "guid" => isPg ? "uuid" : isMySql ? "char(36)" : "uniqueidentifier",
            "int" => isPg ? "integer" : "int",
            "long" => "bigint",
            "decimal" => isPg ? "numeric(18,2)" : "decimal(18,2)",
            "float" => isPg ? "double precision" : "float",
            "bool" => isPg ? "boolean" : isMySql ? "tinyint(1)" : "bit",
            "datetime" => isPg ? "timestamptz" : isMySql ? "datetime" : "datetime2",
            _ => isPg ? "varchar(255)" : isMySql ? "varchar(255)" : "nvarchar(255)",
        };
    }

    public static SchemaModelDto BuildSchemaModelFromLegacy(
        ProjectModel project,
        IReadOnlyList<EntityModel> entities,
        IReadOnlyList<RelationModel> relations,
        string engine = "mssql")
    {
        var colors = new[] { "blue", "indigo", "emerald", "amber", "rose", "cyan", "slate" };
        var tables = new List<SchemaTableDto>();
        var entityIdToTableId = new Dictionary<int, string>();
        var entityIdToPkColId = new Dictionary<int, string>();
        var fkColLookup = new Dictionary<(int EntityId, string ColName), string>();

        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            var tableId = $"tbl_{entity.Id}_{entity.EntityName.ToLowerInvariant()}";
            entityIdToTableId[entity.Id] = tableId;

            var pkColId = $"{tableId}_col_id";
            entityIdToPkColId[entity.Id] = pkColId;

            var columns = new List<SchemaColumnDto>
            {
                new(
                    pkColId,
                    "Id",
                    MapCSharpToSqlType(entity.PkType, engine),
                    IsPrimaryKey: true,
                    IsNullable: false,
                    IsUnique: true,
                    IsAutoIncrement: entity.PkType is PkTypes.INT or PkTypes.LONG,
                    DefaultValue: string.Empty,
                    CheckConstraint: string.Empty,
                    Comment: string.Empty),
            };

            foreach (var row in entity.Rows ?? [])
            {
                if (string.IsNullOrWhiteSpace(row.ColumnName) ||
                    string.Equals(row.ColumnName, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var colId = $"{tableId}_col_{row.Id}_{row.ColumnName.ToLowerInvariant()}";
                columns.Add(new SchemaColumnDto(
                    colId,
                    row.ColumnName,
                    MapCSharpToSqlType(row.DataType, engine),
                    IsPrimaryKey: false,
                    IsNullable: row.Nullable,
                    IsUnique: false,
                    IsAutoIncrement: false,
                    DefaultValue: string.Empty,
                    CheckConstraint: string.Empty,
                    Comment: string.Empty));

                fkColLookup[(entity.Id, row.ColumnName.ToLowerInvariant())] = colId;
            }

            var colIndex = i % 3;
            var rowIndex = i / 3;
            var x = 80 + colIndex * 360;
            var y = 80 + rowIndex * 280;

            tables.Add(new SchemaTableDto(
                tableId,
                entity.EntityName,
                engine == "postgresql" ? "public" : "dbo",
                StorageEngine: null,
                Charset: null,
                CodeGen: new TableCodeGenConfigDto(
                    AddService: entity.HasService,
                    ServiceMethods: new Dictionary<string, bool>
                    {
                        ["getAll"] = true,
                        ["select"] = true,
                        ["find"] = true,
                        ["add"] = true,
                        ["update"] = true,
                        ["delete"] = true,
                    },
                    AddApiController: entity.HasApi),
                Color: colors[i % colors.Length],
                Comment: string.Empty,
                X: x,
                Y: y,
                Columns: columns));
        }

        var schemaRelations = new List<SchemaRelationDto>();
        foreach (var rel in relations)
        {
            if (rel.ParentId is not int parentId || rel.ChildId is not int childId)
            {
                continue;
            }

            if (!entityIdToTableId.TryGetValue(parentId, out var parentTableId) ||
                !entityIdToTableId.TryGetValue(childId, out var childTableId) ||
                !entityIdToPkColId.TryGetValue(parentId, out var parentPkColId))
            {
                continue;
            }

            var parentEntity = entities.FirstOrDefault(e => e.Id == parentId);
            var expectedFkName = parentEntity is not null ? $"{parentEntity.EntityName}Id".ToLowerInvariant() : "parentid";
            var childFkColId = fkColLookup.TryGetValue((childId, expectedFkName), out var foundColId)
                ? foundColId
                : entityIdToPkColId[childId];

            schemaRelations.Add(new SchemaRelationDto(
                Id: $"rel_{rel.Id}",
                Name: string.IsNullOrWhiteSpace(rel.Name) ? $"fk_{parentId}_{childId}" : rel.Name,
                SourceTableId: childTableId,
                SourceColumnId: childFkColId,
                TargetTableId: parentTableId,
                TargetColumnId: parentPkColId,
                SourceColumnIds: [childFkColId],
                TargetColumnIds: [parentPkColId],
                Cardinality: "N:1",
                OnDelete: "NO ACTION",
                OnUpdate: "NO ACTION"));
        }

        return new SchemaModelDto(
            Id: $"project-{project.Id}",
            Name: project.Name,
            Engine: engine,
            Description: $"MMA CLI solution at {project.Path}",
            UpdatedAt: (project.UpdatedAtUtc ?? DateTime.UtcNow).ToString("O"),
            Tables: tables,
            Relations: schemaRelations);
    }

    public static async Task<SchemaSyncStateDto> SyncSchemaToDbContextAsync(
        CliDbContext dbContext,
        int projectId,
        SchemaModelDto schema,
        CancellationToken cancellationToken)
    {
        var existingEntities = await dbContext.Entities
            .Include(e => e.Rows)
            .Where(e => e.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        var existingByName = existingEntities
            .GroupBy(e => e.EntityName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var fkColumnsByTableId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in schema.Relations)
        {
            if (!fkColumnsByTableId.TryGetValue(rel.SourceTableId, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                fkColumnsByTableId[rel.SourceTableId] = set;
            }
            set.Add(rel.SourceColumnId);
            if (rel.SourceColumnIds is not null)
            {
                foreach (var cid in rel.SourceColumnIds)
                {
                    set.Add(cid);
                }
            }
        }

        var tableIdToEntity = new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);
        var activeEntityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in schema.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Name))
            {
                continue;
            }

            var entityName = table.Name.Trim();
            activeEntityNames.Add(entityName);

            var pkCol = table.Columns.FirstOrDefault(c => c.IsPrimaryKey)
                ?? table.Columns.FirstOrDefault(c => string.Equals(c.Name, "Id", StringComparison.OrdinalIgnoreCase));
            var pkType = pkCol is not null ? MapSqlTypeToCSharp(pkCol.Type) : PkTypes.GUID;
            var hasService = table.CodeGen?.AddService ?? true;
            var hasApi = table.CodeGen?.AddApiController ?? true;

            if (!existingByName.TryGetValue(entityName, out var entity))
            {
                entity = new EntityModel
                {
                    ProjectId = projectId,
                    EntityName = entityName,
                    PkType = pkType,
                    HasService = hasService,
                    HasApi = hasApi,
                    Applied = false,
                };
                dbContext.Entities.Add(entity);
                await dbContext.SaveChangesAsync(cancellationToken);
                existingByName[entityName] = entity;
            }
            else
            {
                entity.PkType = pkType;
                entity.HasService = hasService;
                entity.HasApi = hasApi;
            }

            tableIdToEntity[table.Id] = entity;

            var existingRowsByName = entity.Rows
                .Where(r => !string.IsNullOrWhiteSpace(r.ColumnName))
                .GroupBy(r => r.ColumnName!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var fkSet = fkColumnsByTableId.GetValueOrDefault(table.Id);
            var activeColNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var col in table.Columns)
            {
                if (string.IsNullOrWhiteSpace(col.Name) ||
                    col.IsPrimaryKey ||
                    string.Equals(col.Name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var colName = col.Name.Trim();
                activeColNames.Add(colName);
                var mappedType = MapSqlTypeToCSharp(col.Type);
                var isFk = fkSet?.Contains(col.Id) == true;

                if (!existingRowsByName.TryGetValue(colName, out var row))
                {
                    entity.Rows.Add(new EntityRowModel
                    {
                        EntityId = entity.Id,
                        ColumnName = colName,
                        DataType = mappedType,
                        Nullable = col.IsNullable,
                        IsForeignKey = isFk,
                        Applied = false,
                    });
                }
                else
                {
                    row.DataType = mappedType;
                    row.Nullable = col.IsNullable;
                    row.IsForeignKey = isFk;
                }
            }

            // Remove unapplied rows that were deleted from the canvas.
            var removedUnappliedRows = entity.Rows
                .Where(r => !r.Applied && !string.IsNullOrWhiteSpace(r.ColumnName) && !activeColNames.Contains(r.ColumnName!))
                .ToList();
            foreach (var row in removedUnappliedRows)
            {
                dbContext.EntityRows.Remove(row);
            }
        }

        // Remove unapplied entities deleted from the canvas.
        var removedUnappliedEntities = existingEntities
            .Where(e => !e.Applied && !activeEntityNames.Contains(e.EntityName))
            .ToList();
        if (removedUnappliedEntities.Count > 0)
        {
            dbContext.Entities.RemoveRange(removedUnappliedEntities);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Sync relations
        var projectEntityIds = tableIdToEntity.Values.Select(e => e.Id).ToHashSet();
        var existingRelations = await dbContext.Relations
            .Where(r => (r.ParentId.HasValue && projectEntityIds.Contains(r.ParentId.Value)) ||
                        (r.ChildId.HasValue && projectEntityIds.Contains(r.ChildId.Value)))
            .ToListAsync(cancellationToken);

        var existingRelKeys = existingRelations
            .Where(r => r.ParentId.HasValue && r.ChildId.HasValue)
            .GroupBy(r => (r.ParentId!.Value, r.ChildId!.Value))
            .ToDictionary(g => g.Key, g => g.First());

        var activeRelKeys = new HashSet<(int ParentId, int ChildId)>();

        foreach (var rel in schema.Relations)
        {
            if (!tableIdToEntity.TryGetValue(rel.TargetTableId, out var parentEntity) ||
                !tableIdToEntity.TryGetValue(rel.SourceTableId, out var childEntity))
            {
                continue;
            }

            var key = (parentEntity.Id, childEntity.Id);
            activeRelKeys.Add(key);

            if (!existingRelKeys.ContainsKey(key))
            {
                dbContext.Relations.Add(new RelationModel
                {
                    ParentId = parentEntity.Id,
                    ChildId = childEntity.Id,
                    Name = string.IsNullOrWhiteSpace(rel.Name)
                        ? $"{parentEntity.EntityName}_{childEntity.EntityName}_{parentEntity.EntityName}Id"
                        : rel.Name,
                    Applied = false,
                });
            }
        }

        var removedUnappliedRels = existingRelations
            .Where(r => !r.Applied && r.ParentId.HasValue && r.ChildId.HasValue && !activeRelKeys.Contains((r.ParentId.Value, r.ChildId.Value)))
            .ToList();
        if (removedUnappliedRels.Count > 0)
        {
            dbContext.Relations.RemoveRange(removedUnappliedRels);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await ComputeSyncStateAsync(dbContext, projectId, cancellationToken);
    }

    public static async Task<SchemaSyncStateDto> ComputeSyncStateAsync(
        CliDbContext dbContext,
        int projectId,
        CancellationToken cancellationToken)
    {
        var entities = await dbContext.Entities
            .AsNoTracking()
            .Include(e => e.Rows)
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.EntityName)
            .ToListAsync(cancellationToken);

        var entityIds = entities.Select(e => e.Id).ToHashSet();
        var pendingRelations = await dbContext.Relations
            .AsNoTracking()
            .CountAsync(r => !r.Applied && r.ParentId.HasValue && entityIds.Contains(r.ParentId.Value), cancellationToken);

        var entityStatuses = entities.Select(e =>
        {
            var nonFkRows = e.Rows.Where(r => !r.IsForeignKey).ToList();
            return new EntitySyncStatusDto(
                e.EntityName,
                e.Applied,
                PendingPropertyCount: nonFkRows.Count(r => !r.Applied),
                AppliedPropertyCount: nonFkRows.Count(r => r.Applied));
        }).ToList();

        return new SchemaSyncStateDto(
            TotalEntities: entities.Count,
            AppliedEntities: entities.Count(e => e.Applied),
            PendingEntities: entities.Count(e => !e.Applied),
            PendingProperties: entityStatuses.Sum(s => s.PendingPropertyCount),
            PendingRelations: pendingRelations,
            Entities: entityStatuses);
    }
}
