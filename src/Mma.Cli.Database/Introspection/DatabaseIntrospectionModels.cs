using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;

namespace Mma.Cli.Database.Introspection;

public enum DatabaseProviderKind
{
    SqlServer = 0,
    PostgreSql = 1,
    Sqlite = 2,
    MySql = 3
}

public enum SqlServerAuthKind
{
    WindowsIntegrated = 0,
    SqlPassword = 1,
    ActiveDirectoryIntegrated = 2
}

public sealed record DatabaseConnectionParameters(
    string ServerOrPath,
    string? Database = null,
    string? Username = null,
    string? Password = null,
    int? Port = null,
    SqlServerAuthKind SqlServerAuth = SqlServerAuthKind.WindowsIntegrated,
    bool TrustServerCertificate = false);

public sealed record SchemaFilterOptions
{
    public static IReadOnlyList<string> DefaultExcludedTables { get; } =
    [
        "__EFMigrationsHistory",
        "sysdiagrams",
        "AppUsers",
        "Attachments",
        "ELMAH_Error",
        "NotificationStatuses",
        "NotificationTypes",
        "Roles",
        "SysSettings",
        "Notifications",
        "RefreshTokens",
        "UserClaims",
        "UserLogins",
        "UserTokens",
        "RoleClaims",
        "UserRoles",
        "sqlite_sequence",
        "sqlite_stat*"
    ];

    public static IReadOnlyList<string> DefaultExcludedColumns { get; } =
    [
        "CreatedBy",
        "CreatedDate",
        "ModifiedBy",
        "ModifiedDate",
        "IsDeleted",
        "DeletedBy",
        "DeletedDate"
    ];

    public IReadOnlyList<string> ExcludedTables { get; init; } = DefaultExcludedTables;
    public IReadOnlyList<string> ExcludedColumns { get; init; } = DefaultExcludedColumns;
    public IReadOnlyList<string> IncludedSchemas { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string>? TableNameOverrides { get; init; }
    public bool IncludeSystemTables { get; init; }
}

public sealed record DatabaseIntrospectRequest(
    DatabaseProviderKind Provider,
    string? ConnectionString = null,
    DatabaseConnectionParameters? Parameters = null,
    SchemaFilterOptions? Filters = null);

public sealed record IntrospectedColumn(
    string RawName,
    string PropertyName,
    string RawDataType,
    string ClrType,
    bool IsPrimaryKey,
    bool IsNullable,
    bool IsForeignKey,
    int? MaxLength = null);

public sealed record IntrospectedRelation(
    string Name,
    string ParentTable,
    string ParentEntity,
    string ChildTable,
    string ChildEntity,
    string ForeignKey,
    string ForeignKeyType,
    string RelationType = "1:N",
    string DeleteBehavior = "Cascade");

public sealed record IntrospectedTable(
    string SchemaName,
    string TableName,
    string EntityName,
    string PkType,
    IReadOnlyList<IntrospectedColumn> Columns);

public sealed record DatabaseSchemaSnapshot(
    DatabaseProviderKind Provider,
    IReadOnlyList<IntrospectedTable> Tables,
    IReadOnlyList<IntrospectedRelation> Relations,
    DateTimeOffset CapturedAtUtc)
{
    public ProjectManifest ToProjectManifest(
        string solutionName,
        MapperKind mapper = MapperKind.AutoMapper,
        string solutionFormat = ".sln")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionName);

        var entities = new List<ManifestEntity>(Tables.Count);
        foreach (var table in Tables)
        {
            var pkCount = table.Columns.Count(c => c.IsPrimaryKey);
            var seenProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var properties = new List<ManifestProperty>();

            foreach (var col in table.Columns)
            {
                if (IsPrimaryKeyRepresentedByBaseEntity(col, pkCount))
                {
                    continue;
                }

                if (!seenProps.Add(col.PropertyName))
                {
                    continue;
                }

                properties.Add(new ManifestProperty(
                    Name: col.PropertyName,
                    DataType: DataTypes.Normalize(col.ClrType) ?? "string",
                    Nullable: col.IsNullable,
                    IsForeignKey: col.IsForeignKey,
                    Applied: false,
                    MaxLength: col.MaxLength));
            }

            entities.Add(new ManifestEntity(
                Name: table.EntityName,
                PkType: DataTypes.Normalize(table.PkType) ?? "Guid",
                HasService: true,
                HasApi: true,
                Applied: false,
                Properties: properties));
        }

        var relations = Relations
            .Select(r => new ManifestRelation(
                Name: r.Name,
                ParentEntity: r.ParentEntity,
                ChildEntity: r.ChildEntity,
                ForeignKey: r.ForeignKey,
                ForeignKeyType: DataTypes.Normalize(r.ForeignKeyType) ?? "Guid",
                RelationType: string.IsNullOrWhiteSpace(r.RelationType) ? "1:N" : r.RelationType,
                DeleteBehavior: string.IsNullOrWhiteSpace(r.DeleteBehavior) ? "Cascade" : r.DeleteBehavior,
                Applied: false))
            .ToList();

        return new ProjectManifest
        {
            SchemaVersion = 2,
            SolutionName = solutionName.Trim(),
            Mapper = mapper.ToFolderName(),
            SolutionFormat = string.IsNullOrWhiteSpace(solutionFormat) ? ".sln" : solutionFormat.Trim(),
            Entities = entities,
            Relations = relations,
            UpdatedAtUtc = CapturedAtUtc == default ? DateTimeOffset.UtcNow : CapturedAtUtc
        };
    }

    private static bool IsPrimaryKeyRepresentedByBaseEntity(IntrospectedColumn col, int pkCount)
    {
        if (string.Equals(col.PropertyName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return col.IsPrimaryKey && !col.IsForeignKey && pkCount == 1;
    }
}
