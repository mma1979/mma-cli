public sealed record IntrospectionResult(
    string Database,
    string Engine,
    IReadOnlyList<IntrospectionTable> Tables,
    IReadOnlyList<IntrospectionRelation> Relations,
    IReadOnlyList<MetadataWarning> Warnings);

public sealed record IntrospectionUniqueConstraint(
    string Id,
    string Name,
    IReadOnlyList<string> Columns);

public sealed record IntrospectionCheckConstraint(
    string Id,
    string Name,
    string Expression);

public sealed record IntrospectionTable(
    string Id,
    string Name,
    string Schema,
    string Color,
    string Comment,
    double X,
    double Y,
    IReadOnlyList<IntrospectionColumn> Columns,
    IReadOnlyList<IntrospectionIndex> Indexes,
    string? StorageEngine = null,
    string? Charset = null,
    IReadOnlyList<IntrospectionUniqueConstraint>? UniqueConstraints = null,
    IReadOnlyList<IntrospectionCheckConstraint>? CheckConstraints = null);

public sealed record IntrospectionColumn(
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

public sealed record IntrospectionIndex(
    string Id,
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique,
    string? Method = null,
    string? WhereClause = null,
    IReadOnlyList<string>? IncludeColumns = null);

public sealed record IntrospectionRelation(
    string Id,
    string Name,
    string SourceTableId,
    string SourceColumnId,
    string TargetTableId,
    string TargetColumnId,
    string Cardinality,
    string OnDelete,
    string OnUpdate,
    IReadOnlyList<string>? SourceColumnIds = null,
    IReadOnlyList<string>? TargetColumnIds = null);

public sealed record MetadataWarning(string Code, string Message);

public sealed record ApiErrorDto(string Error, string Code, string TraceId);
