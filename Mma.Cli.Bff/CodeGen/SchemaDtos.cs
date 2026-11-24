using Mma.Cli.Bff.Projects;

namespace Mma.Cli.Bff.CodeGen;

public sealed record SchemaColumnDto(
    string Id,
    string Name,
    string Type,
    bool IsPrimaryKey,
    bool IsNullable,
    bool IsUnique,
    bool IsAutoIncrement,
    string DefaultValue,
    string CheckConstraint,
    string Comment);

public sealed record TableCodeGenConfigDto(
    bool AddService,
    Dictionary<string, bool>? ServiceMethods,
    bool AddApiController);

public sealed record TableIndexDto(
    string Id,
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique,
    string? Method = null,
    string? WhereClause = null,
    IReadOnlyList<string>? IncludeColumns = null);

public sealed record TableUniqueConstraintDto(
    string Id,
    string Name,
    IReadOnlyList<string> Columns);

public sealed record TableCheckConstraintDto(
    string Id,
    string Name,
    string Expression);

public sealed record SchemaTableDto(
    string Id,
    string Name,
    string Schema,
    string? StorageEngine,
    string? Charset,
    TableCodeGenConfigDto? CodeGen,
    string Color,
    string Comment,
    double X,
    double Y,
    IReadOnlyList<SchemaColumnDto> Columns,
    IReadOnlyList<TableIndexDto>? Indexes = null,
    IReadOnlyList<TableUniqueConstraintDto>? UniqueConstraints = null,
    IReadOnlyList<TableCheckConstraintDto>? CheckConstraints = null);

public sealed record SchemaRelationDto(
    string Id,
    string Name,
    string SourceTableId,
    string SourceColumnId,
    string TargetTableId,
    string TargetColumnId,
    IReadOnlyList<string>? SourceColumnIds,
    IReadOnlyList<string>? TargetColumnIds,
    string Cardinality,
    string OnDelete,
    string OnUpdate);

public sealed record SchemaModelDto(
    string Id,
    string Name,
    string Engine,
    string Description,
    string UpdatedAt,
    IReadOnlyList<SchemaTableDto> Tables,
    IReadOnlyList<SchemaRelationDto> Relations);

public sealed record EntitySyncStatusDto(
    string EntityName,
    bool Applied,
    int PendingPropertyCount,
    int AppliedPropertyCount);

public sealed record SchemaSyncStateDto(
    int TotalEntities,
    int AppliedEntities,
    int PendingEntities,
    int PendingProperties,
    int PendingRelations,
    IReadOnlyList<EntitySyncStatusDto> Entities);

public sealed record ProjectSchemaResponseDto(
    ProjectSummaryDto Project,
    SchemaModelDto Schema,
    SchemaSyncStateDto SyncState);

public sealed record SaveSchemaResponseDto(
    DateTime UpdatedAtUtc,
    SchemaSyncStateDto SyncState);
