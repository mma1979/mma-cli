using System.Text.RegularExpressions;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Database.Introspection;

internal sealed record RawColumnMetadata(
    string SchemaName,
    string TableName,
    string ColumnName,
    string RawDataType,
    bool IsPrimaryKey,
    bool IsNullable,
    bool IsForeignKey,
    int? MaxLength = null,
    bool IsSystemTable = false);

internal sealed record RawRelationMetadata(
    string ConstraintName,
    string ParentSchema,
    string ParentTable,
    string ChildSchema,
    string ChildTable,
    string ForeignKeyColumn,
    string RawDataType,
    string DeleteAction);

internal static partial class DatabaseIntrospectionHelper
{
    [GeneratedRegex(
        @"(?<key>(?:password|pwd|passcode|user\s*password|secret|access\s*token|accesstoken|sharedaccesskey|accountkey|token|credentials)\s*=\s*)(?<val>'[^']*'|""[^""]*""|[^;\r\n]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretPairRegex();

    [GeneratedRegex(
        @"(?<prefix>[a-zA-Z][a-zA-Z0-9+.-]*://[^/:@\s]+:)(?<secret>[^/\s?#;'""]+)(?<suffix>@)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretUriRegex();

    public static IReadOnlyList<string> ExtractSecrets(DatabaseIntrospectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var secrets = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddSecret(string? candidate)
        {
            if (!string.IsNullOrEmpty(candidate) && seen.Add(candidate))
            {
                secrets.Add(candidate);
            }
        }

        AddSecret(request.Parameters?.Password);

        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            foreach (Match match in SecretPairRegex().Matches(request.ConnectionString))
            {
                var val = match.Groups["val"].Value.Trim();
                if (val.Length >= 2
                    && ((val[0] == '\'' && val[^1] == '\'') || (val[0] == '"' && val[^1] == '"')))
                {
                    val = val[1..^1];
                }

                AddSecret(val);
            }

            foreach (Match match in SecretUriRegex().Matches(request.ConnectionString))
            {
                AddSecret(match.Groups["secret"].Value);
            }
        }

        return secrets;
    }

    public static string RedactSecrets(string? text, string? explicitPassword = null) =>
        string.IsNullOrEmpty(explicitPassword)
            ? RedactSecrets(text, (IReadOnlyList<string>?)null)
            : RedactSecrets(text, [explicitPassword]);

    public static string RedactSecrets(string? text, IReadOnlyList<string>? explicitSecrets)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var redacted = SecretPairRegex().Replace(text, "${key}***");
        redacted = SecretUriRegex().Replace(redacted, "${prefix}***${suffix}");

        if (explicitSecrets is { Count: > 0 })
        {
            foreach (var secret in explicitSecrets
                .Where(static s => !string.IsNullOrEmpty(s) && s.Length >= 3)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(static s => s.Length))
            {
                redacted = redacted.Replace(secret, "***", StringComparison.Ordinal);
            }
        }

        return redacted;
    }

    public static string FormatRedactedDetails(Exception ex, string? explicitPassword = null) =>
        $"{ex.GetType().Name}: {RedactSecrets(ex.Message, explicitPassword)}";

    public static string FormatRedactedDetails(Exception ex, IReadOnlyList<string>? explicitSecrets) =>
        $"{ex.GetType().Name}: {RedactSecrets(ex.Message, explicitSecrets)}";

    public static OperationResult<string> ResolveConnectionString(
        DatabaseProviderKind expectedProvider,
        DatabaseIntrospectRequest request,
        Func<DatabaseConnectionParameters, OperationResult<string>> buildFromParameters,
        Func<string, IReadOnlyList<string>, OperationResult<string>> validateRawConnectionString,
        out IReadOnlyList<string> secrets)
    {
        ArgumentNullException.ThrowIfNull(request);
        secrets = ExtractSecrets(request);

        if (request.Provider != expectedProvider)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Introspector for '{expectedProvider}' cannot process provider '{request.Provider}'.");
        }

        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return validateRawConnectionString(request.ConnectionString, secrets);
        }

        if (request.Parameters is not null)
        {
            return buildFromParameters(request.Parameters);
        }

        return OperationResult<string>.Failure(
            ErrorCodes.VALIDATION_ERROR,
            "Either ConnectionString or Parameters must be provided.");
    }

    public static string NormalizeDeleteBehavior(string? rawAction)
    {
        if (string.IsNullOrWhiteSpace(rawAction))
        {
            return "Cascade";
        }

        return rawAction.Trim().ToUpperInvariant() switch
        {
            "CASCADE" => "Cascade",
            "RESTRICT" => "Restrict",
            "SET NULL" or "SET_NULL" => "SetNull",
            "SET DEFAULT" or "SET_DEFAULT" => "SetDefault",
            "NO ACTION" or "NO_ACTION" => "NoAction",
            _ => "NoAction"
        };
    }

    public static DatabaseSchemaSnapshot BuildSnapshot(
        DatabaseProviderKind provider,
        IReadOnlyList<RawColumnMetadata> rawColumns,
        IReadOnlyList<RawRelationMetadata> rawRelations,
        SchemaFilterOptions? filters,
        SchemaNamingService naming)
    {
        var effectiveFilters = filters ?? new SchemaFilterOptions();
        var includedSchemas = effectiveFilters.IncludedSchemas.Count > 0
            ? new HashSet<string>(effectiveFilters.IncludedSchemas, StringComparer.OrdinalIgnoreCase)
            : null;

        var tableGroups = rawColumns
            .GroupBy(c => new TableKey(c.SchemaName, c.TableName))
            .OrderBy(g => g.Key.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.TableName, StringComparer.OrdinalIgnoreCase);

        var tables = new List<IntrospectedTable>();
        var tableByKey = new Dictionary<TableKey, IntrospectedTable>();
        var columnMetaLookup = new Dictionary<ColumnKey, IntrospectedColumn>();

        foreach (var group in tableGroups)
        {
            var schemaName = group.Key.SchemaName;
            var tableName = group.Key.TableName;

            if (includedSchemas is not null && !includedSchemas.Contains(schemaName))
            {
                continue;
            }

            if (!effectiveFilters.IncludeSystemTables && group.Any(c => c.IsSystemTable))
            {
                continue;
            }

            var qualifiedTableName = string.IsNullOrWhiteSpace(schemaName)
                ? tableName
                : $"{schemaName}.{tableName}";

            if (naming.IsExcluded(tableName, effectiveFilters.ExcludedTables)
                || naming.IsExcluded(qualifiedTableName, effectiveFilters.ExcludedTables))
            {
                continue;
            }

            var entityName = ResolveEntityName(schemaName, tableName, effectiveFilters.TableNameOverrides, naming);
            var columns = new List<IntrospectedColumn>();
            var seenPropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawCol in group)
            {
                if (naming.IsExcluded(rawCol.ColumnName, effectiveFilters.ExcludedColumns))
                {
                    continue;
                }

                var propertyName = naming.ToPropertyName(rawCol.ColumnName);
                if (!seenPropNames.Add(propertyName))
                {
                    continue;
                }

                var clrType = naming.MapSqlType(provider, rawCol.RawDataType);
                var maxLength = rawCol.MaxLength ?? SchemaNamingService.TryExtractMaxLength(rawCol.RawDataType, clrType);

                var introspectedCol = new IntrospectedColumn(
                    RawName: rawCol.ColumnName,
                    PropertyName: propertyName,
                    RawDataType: rawCol.RawDataType,
                    ClrType: clrType,
                    IsPrimaryKey: rawCol.IsPrimaryKey,
                    IsNullable: rawCol.IsPrimaryKey ? false : rawCol.IsNullable,
                    IsForeignKey: rawCol.IsForeignKey,
                    MaxLength: maxLength);

                columns.Add(introspectedCol);
                columnMetaLookup.TryAdd(new ColumnKey(schemaName, tableName, rawCol.ColumnName), introspectedCol);
            }

            if (columns.Count == 0)
            {
                continue;
            }

            // If table has no explicit PK constraint, promote 'Id' column if present
            if (!columns.Any(c => c.IsPrimaryKey))
            {
                var idIdx = columns.FindIndex(c => string.Equals(c.PropertyName, "Id", StringComparison.OrdinalIgnoreCase));
                if (idIdx >= 0)
                {
                    var promoted = columns[idIdx] with { IsPrimaryKey = true, IsNullable = false };
                    columns[idIdx] = promoted;
                    columnMetaLookup[new ColumnKey(schemaName, tableName, promoted.RawName)] = promoted;
                }
            }

            var pkCol = columns.FirstOrDefault(c => c.IsPrimaryKey);
            var pkType = pkCol?.ClrType ?? "int";

            var table = new IntrospectedTable(schemaName, tableName, entityName, pkType, columns);
            tables.Add(table);
            tableByKey[group.Key] = table;
        }

        var relations = new List<IntrospectedRelation>();
        var seenRelations = new HashSet<RelationKey>();

        foreach (var rawRel in rawRelations)
        {
            var parentKey = new TableKey(rawRel.ParentSchema, rawRel.ParentTable);
            var childKey = new TableKey(rawRel.ChildSchema, rawRel.ChildTable);
            var fkColKey = new ColumnKey(rawRel.ChildSchema, rawRel.ChildTable, rawRel.ForeignKeyColumn);

            if (!tableByKey.TryGetValue(parentKey, out var parentTable)
                || !tableByKey.TryGetValue(childKey, out var childTable)
                || !columnMetaLookup.TryGetValue(fkColKey, out var fkColumn))
            {
                continue;
            }

            var dedupeKey = new RelationKey(parentKey, childKey, fkColumn.PropertyName);
            if (!seenRelations.Add(dedupeKey))
            {
                continue;
            }

            var relationName = $"{parentTable.EntityName}_{childTable.EntityName}_{fkColumn.PropertyName}";
            var deleteBehavior = NormalizeDeleteBehavior(rawRel.DeleteAction);

            relations.Add(new IntrospectedRelation(
                Name: relationName,
                ParentTable: parentTable.TableName,
                ParentEntity: parentTable.EntityName,
                ChildTable: childTable.TableName,
                ChildEntity: childTable.EntityName,
                ForeignKey: fkColumn.PropertyName,
                ForeignKeyType: fkColumn.ClrType,
                RelationType: "1:N",
                DeleteBehavior: deleteBehavior));
        }

        var sortedRelations = relations
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ParentEntity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ChildEntity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ForeignKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DatabaseSchemaSnapshot(provider, tables, sortedRelations, DateTimeOffset.UtcNow);
    }

    private static string ResolveEntityName(
        string schemaName,
        string tableName,
        IReadOnlyDictionary<string, string>? overrides,
        SchemaNamingService naming)
    {
        if (overrides is not null && overrides.Count > 0 && !string.IsNullOrWhiteSpace(schemaName))
        {
            var qualified = $"{schemaName}.{tableName}";
            foreach (var kvp in overrides)
            {
                if (string.Equals(kvp.Key, qualified, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    return naming.ToEntityName(qualified, overrides);
                }
            }
        }

        return naming.ToEntityName(tableName, overrides);
    }

    private readonly record struct TableKey : IEquatable<TableKey>
    {
        public TableKey(string schemaName, string tableName)
        {
            SchemaName = schemaName ?? string.Empty;
            TableName = tableName ?? string.Empty;
        }

        public string SchemaName { get; }
        public string TableName { get; }

        public bool Equals(TableKey other) =>
            string.Equals(SchemaName, other.SchemaName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TableName, other.TableName, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(SchemaName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(TableName));
    }

    private readonly record struct ColumnKey : IEquatable<ColumnKey>
    {
        public ColumnKey(string schemaName, string tableName, string columnName)
        {
            SchemaName = schemaName ?? string.Empty;
            TableName = tableName ?? string.Empty;
            ColumnName = columnName ?? string.Empty;
        }

        public string SchemaName { get; }
        public string TableName { get; }
        public string ColumnName { get; }

        public bool Equals(ColumnKey other) =>
            string.Equals(SchemaName, other.SchemaName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TableName, other.TableName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ColumnName, other.ColumnName, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(SchemaName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(TableName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(ColumnName));
    }

    private readonly record struct RelationKey : IEquatable<RelationKey>
    {
        public RelationKey(TableKey parent, TableKey child, string foreignKey)
        {
            Parent = parent;
            Child = child;
            ForeignKey = foreignKey ?? string.Empty;
        }

        public TableKey Parent { get; }
        public TableKey Child { get; }
        public string ForeignKey { get; }

        public bool Equals(RelationKey other) =>
            Parent.Equals(other.Parent)
            && Child.Equals(other.Child)
            && string.Equals(ForeignKey, other.ForeignKey, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            HashCode.Combine(
                Parent.GetHashCode(),
                Child.GetHashCode(),
                StringComparer.OrdinalIgnoreCase.GetHashCode(ForeignKey));
    }
}
