using System.Data;
using Microsoft.Data.SqlClient;

public sealed record SqlServerTableRow(
    string SchemaName,
    string TableName,
    string? TableComment);

public sealed record SqlServerColumnRow(
    string SchemaName,
    string TableName,
    string ColumnName,
    string TypeName,
    short MaxLength,
    byte Precision,
    byte Scale,
    bool IsNullable,
    bool IsIdentity,
    string? DefaultDefinition,
    int ColumnId,
    string? ColumnComment);

public sealed record SqlServerKeyRow(
    string SchemaName,
    string TableName,
    string ConstraintName,
    bool IsPrimaryKey,
    bool IsUniqueConstraint,
    string ColumnName,
    int KeyOrdinal);

public sealed record SqlServerCheckRow(
    string SchemaName,
    string TableName,
    string ConstraintName,
    string Definition,
    string? ColumnName);

public sealed record SqlServerIndexColumnRow(
    string SchemaName,
    string TableName,
    string IndexName,
    bool IsUnique,
    string IndexTypeDesc,
    bool HasFilter,
    bool IsIncludedColumn,
    int KeyOrdinal,
    string ColumnName,
    string? FilterDefinition = null);

public sealed record SqlServerForeignKeyColumnRow(
    string ForeignKeyName,
    string ChildSchema,
    string ChildTable,
    string ChildColumn,
    string ParentSchema,
    string ParentTable,
    string ParentColumn,
    int ConstraintColumnId,
    string DeleteAction,
    string UpdateAction);

public sealed record SqlServerCatalogRows(
    IReadOnlyList<SqlServerTableRow> Tables,
    IReadOnlyList<SqlServerColumnRow> Columns,
    IReadOnlyList<SqlServerKeyRow> Keys,
    IReadOnlyList<SqlServerCheckRow> Checks,
    IReadOnlyList<SqlServerIndexColumnRow> IndexColumns,
    IReadOnlyList<SqlServerForeignKeyColumnRow> ForeignKeyColumns);

public sealed class SqlServerIntrospectionProvider : IDatabaseIntrospectionProvider
{
    public string Engine => "mssql";

    public async Task<IntrospectionResult> IntrospectAsync(
        IntrospectionRequest request,
        ValidatedDatabaseTarget target,
        CancellationToken cancellationToken)
    {
        var useSsl = request.Ssl ?? true;
        var csb = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{target.Address},{target.Port}",
            InitialCatalog = request.Database,
            UserID = request.Username,
            Password = request.Password ?? string.Empty,
            ConnectTimeout = 8,
            CommandTimeout = 10,
            Pooling = false,
            Encrypt = useSsl ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = !useSsl,
        };

        if (useSsl)
        {
            csb.HostNameInCertificate = target.OriginalHost;
        }

        await using var connection = new SqlConnection(csb.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw ClassifyException(ex, isConnectionPhase: true);
        }

        try
        {
            var schemaFilter = request.SchemaFilter?.Trim() ?? string.Empty;
            var rows = await ReadCatalogAsync(connection, schemaFilter, cancellationToken);
            return MapCatalog(request.Database, rows);
        }
        catch (DatabaseIntrospectionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw ClassifyException(ex, isConnectionPhase: false);
        }
    }

    internal static IntrospectionResult MapCatalog(string database, SqlServerCatalogRows rows)
    {
        var warnings = new List<MetadataWarning>();
        var tablesInput = rows.Tables;
        if (tablesInput.Count > CatalogMappingHelpers.MaxDiscoveredTables)
        {
            warnings.Add(new MetadataWarning(
                "table_limit_exceeded",
                $"Catalog exceeded the {CatalogMappingHelpers.MaxDiscoveredTables}-table limit; results were truncated."));
            tablesInput = tablesInput.Take(CatalogMappingHelpers.MaxDiscoveredTables).ToList();
        }

        var pkCols = new HashSet<string>(StringComparer.Ordinal);
        var singleColUniqueCols = new HashSet<string>(StringComparer.Ordinal);
        var compositeUniqueGroups = new List<(string TableKey, string ConstraintName, List<string> ColumnNames)>();

        foreach (var group in rows.Keys.GroupBy(k => (k.SchemaName, k.TableName, k.ConstraintName, k.IsPrimaryKey, k.IsUniqueConstraint)))
        {
            var members = group.OrderBy(k => k.KeyOrdinal).ToList();
            var tblKey = $"{group.Key.SchemaName}.{group.Key.TableName}";
            if (group.Key.IsPrimaryKey)
            {
                foreach (var m in members)
                {
                    pkCols.Add($"{tblKey}.{m.ColumnName}");
                }
            }
            else if (group.Key.IsUniqueConstraint)
            {
                if (members.Count == 1)
                {
                    singleColUniqueCols.Add($"{tblKey}.{members[0].ColumnName}");
                }
                else if (members.Count > 1)
                {
                    compositeUniqueGroups.Add((tblKey, group.Key.ConstraintName, members.Select(m => m.ColumnName).ToList()));
                }
            }
        }

        var singleColChecks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var tableChecksByTable = new Dictionary<string, List<IntrospectionCheckConstraint>>(StringComparer.Ordinal);
        var chkCounter = 0;

        foreach (var check in rows.Checks)
        {
            var cleaned = CatalogMappingHelpers.StripOuterCheckWrapper(check.Definition);
            var tblKey = $"{check.SchemaName}.{check.TableName}";
            if (!string.IsNullOrWhiteSpace(check.ColumnName))
            {
                var colKey = $"{tblKey}.{check.ColumnName}";
                if (!singleColChecks.TryGetValue(colKey, out var list))
                {
                    list = [];
                    singleColChecks[colKey] = list;
                }
                list.Add(cleaned);
            }
            else
            {
                if (!tableChecksByTable.TryGetValue(tblKey, out var list))
                {
                    list = [];
                    tableChecksByTable[tblKey] = list;
                }
                list.Add(new IntrospectionCheckConstraint($"chk-ms-{++chkCounter}", check.ConstraintName, cleaned));
            }
        }

        var colsByTable = new Dictionary<string, List<IntrospectionColumn>>(StringComparer.Ordinal);
        var colIdLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        var colCounter = 0;

        foreach (var col in rows.Columns
            .OrderBy(c => c.SchemaName, StringComparer.Ordinal)
            .ThenBy(c => c.TableName, StringComparer.Ordinal)
            .ThenBy(c => c.ColumnId))
        {
            var tblKey = $"{col.SchemaName}.{col.TableName}";
            var colKey = $"{tblKey}.{col.ColumnName}";
            var colId = $"col-ms-{++colCounter}";
            colIdLookup[colKey] = colId;

            var isPk = pkCols.Contains(colKey);
            var formattedType = FormatColumnType(col);
            var defaultVal = CatalogMappingHelpers.StripSingleOuterParens(col.DefaultDefinition);
            var checkExpr = singleColChecks.TryGetValue(colKey, out var checks)
                ? string.Join(" AND ", checks)
                : string.Empty;

            var dto = new IntrospectionColumn(
                Id: colId,
                Name: col.ColumnName,
                Type: formattedType,
                IsPrimaryKey: isPk,
                IsNullable: col.IsNullable && !isPk,
                IsUnique: singleColUniqueCols.Contains(colKey),
                IsAutoIncrement: col.IsIdentity,
                DefaultValue: defaultVal,
                CheckConstraint: checkExpr,
                Comment: col.ColumnComment ?? string.Empty);

            if (!colsByTable.TryGetValue(tblKey, out var list))
            {
                list = [];
                colsByTable[tblKey] = list;
            }
            list.Add(dto);
        }

        var uniqueConstraintsByTable = new Dictionary<string, List<IntrospectionUniqueConstraint>>(StringComparer.Ordinal);
        var uqCounter = 0;
        foreach (var (tblKey, constraintName, colNames) in compositeUniqueGroups)
        {
            var resolvedIds = colNames
                .Select(c => colIdLookup.TryGetValue($"{tblKey}.{c}", out var id) ? id : null)
                .Where(id => id is not null)
                .Cast<string>()
                .ToList();
            if (resolvedIds.Count < 2) continue;

            if (!uniqueConstraintsByTable.TryGetValue(tblKey, out var list))
            {
                list = [];
                uniqueConstraintsByTable[tblKey] = list;
            }
            list.Add(new IntrospectionUniqueConstraint($"uq-ms-{++uqCounter}", constraintName, resolvedIds));
        }

        var indexesByTable = new Dictionary<string, List<IntrospectionIndex>>(StringComparer.Ordinal);
        var idxCounter = 0;

        foreach (var indexGroup in rows.IndexColumns
            .GroupBy(i => (i.SchemaName, i.TableName, i.IndexName))
            .OrderBy(g => g.Key.SchemaName, StringComparer.Ordinal)
            .ThenBy(g => g.Key.TableName, StringComparer.Ordinal)
            .ThenBy(g => g.Key.IndexName, StringComparer.Ordinal))
        {
            var entries = indexGroup.ToList();
            var first = entries[0];
            var tblKey = $"{indexGroup.Key.SchemaName}.{indexGroup.Key.TableName}";

            var keyColumns = entries
                .Where(e => !e.IsIncludedColumn)
                .OrderBy(e => e.KeyOrdinal)
                .Select(e => colIdLookup.TryGetValue($"{tblKey}.{e.ColumnName}", out var id) ? id : null)
                .Where(id => id is not null)
                .Cast<string>()
                .ToList();

            if (keyColumns.Count == 0)
            {
                continue;
            }

            var includeCols = entries
                .Where(e => e.IsIncludedColumn)
                .Select(e => colIdLookup.TryGetValue($"{tblKey}.{e.ColumnName}", out var id) ? id : null)
                .Where(id => id is not null)
                .Cast<string>()
                .ToList();

            var method = !first.IndexTypeDesc.Equals("NONCLUSTERED", StringComparison.OrdinalIgnoreCase)
                && !first.IndexTypeDesc.Equals("CLUSTERED", StringComparison.OrdinalIgnoreCase)
                ? first.IndexTypeDesc.ToUpperInvariant()
                : null;
            var whereClause = !string.IsNullOrWhiteSpace(first.FilterDefinition)
                ? CatalogMappingHelpers.StripSingleOuterParens(first.FilterDefinition)
                : null;

            if (!indexesByTable.TryGetValue(tblKey, out var idxList))
            {
                idxList = [];
                indexesByTable[tblKey] = idxList;
            }

            idxList.Add(new IntrospectionIndex(
                Id: $"idx-ms-{++idxCounter}",
                Name: indexGroup.Key.IndexName,
                Columns: keyColumns,
                IsUnique: first.IsUnique,
                Method: method,
                WhereClause: whereClause,
                IncludeColumns: includeCols.Count > 0 ? includeCols : null));
        }

        var tableIdLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        var tables = new List<IntrospectionTable>(tablesInput.Count);
        for (var i = 0; i < tablesInput.Count; i++)
        {
            var t = tablesInput[i];
            var tblKey = $"{t.SchemaName}.{t.TableName}";
            var tblId = $"tbl-ms-{i + 1}";
            tableIdLookup[tblKey] = tblId;
            var (x, y) = CatalogMappingHelpers.GetGridPosition(i);

            tables.Add(new IntrospectionTable(
                Id: tblId,
                Name: t.TableName,
                Schema: string.IsNullOrWhiteSpace(t.SchemaName) ? "dbo" : t.SchemaName,
                Color: CatalogMappingHelpers.GetAccentColor(i),
                Comment: t.TableComment ?? string.Empty,
                X: x,
                Y: y,
                Columns: colsByTable.TryGetValue(tblKey, out var cols) ? cols : [],
                Indexes: indexesByTable.TryGetValue(tblKey, out var idxs) ? idxs : [],
                UniqueConstraints: uniqueConstraintsByTable.TryGetValue(tblKey, out var uqs) && uqs.Count > 0 ? uqs : null,
                CheckConstraints: tableChecksByTable.TryGetValue(tblKey, out var chks) && chks.Count > 0 ? chks : null));
        }

        var relations = new List<IntrospectionRelation>();
        var seenRelations = new HashSet<string>(StringComparer.Ordinal);
        var relCounter = 0;

        foreach (var fkGroup in rows.ForeignKeyColumns
            .GroupBy(f => (f.ChildSchema, f.ChildTable, f.ForeignKeyName))
            .OrderBy(g => g.Key.ChildSchema, StringComparer.Ordinal)
            .ThenBy(g => g.Key.ChildTable, StringComparer.Ordinal)
            .ThenBy(g => g.Key.ForeignKeyName, StringComparer.Ordinal))
        {
            var members = fkGroup.OrderBy(m => m.ConstraintColumnId).ToList();
            if (members.Count == 0) continue;

            var first = members[0];
            var srcKey = $"{first.ParentSchema}.{first.ParentTable}";
            var tgtKey = $"{first.ChildSchema}.{first.ChildTable}";

            if (!tableIdLookup.TryGetValue(srcKey, out var sourceTableId)
                || !tableIdLookup.TryGetValue(tgtKey, out var targetTableId))
            {
                continue;
            }

            var sourceColIds = new List<string>(members.Count);
            var targetColIds = new List<string>(members.Count);
            foreach (var m in members)
            {
                if (colIdLookup.TryGetValue($"{srcKey}.{m.ParentColumn}", out var srcColId)
                    && colIdLookup.TryGetValue($"{tgtKey}.{m.ChildColumn}", out var tgtColId))
                {
                    sourceColIds.Add(srcColId);
                    targetColIds.Add(tgtColId);
                }
            }

            if (sourceColIds.Count != members.Count) continue;

            var dedup = $"{sourceTableId}:{string.Join(",", sourceColIds)}->{targetTableId}:{string.Join(",", targetColIds)}";
            if (!seenRelations.Add(dedup))
            {
                continue;
            }

            var isComposite = sourceColIds.Count > 1;
            var isChildUnique = !isComposite && singleColUniqueCols.Contains($"{tgtKey}.{first.ChildColumn}");
            relations.Add(new IntrospectionRelation(
                Id: $"rel-ms-{++relCounter}",
                Name: string.IsNullOrWhiteSpace(first.ForeignKeyName) ? $"fk_{first.ChildTable}_{first.ChildColumn}" : first.ForeignKeyName,
                SourceTableId: sourceTableId,
                SourceColumnId: sourceColIds[0],
                TargetTableId: targetTableId,
                TargetColumnId: targetColIds[0],
                Cardinality: isChildUnique ? "1:1" : "1:N",
                OnDelete: CatalogMappingHelpers.NormalizeReferentialAction(first.DeleteAction),
                OnUpdate: CatalogMappingHelpers.NormalizeReferentialAction(first.UpdateAction),
                SourceColumnIds: isComposite ? sourceColIds : null,
                TargetColumnIds: isComposite ? targetColIds : null));
        }

        return new IntrospectionResult(
            Database: database,
            Engine: "mssql",
            Tables: tables,
            Relations: relations,
            Warnings: warnings);
    }

    private static string FormatColumnType(SqlServerColumnRow col)
    {
        var upper = (string.IsNullOrWhiteSpace(col.TypeName) ? "NVARCHAR" : col.TypeName).ToUpperInvariant();
        if (upper is "NVARCHAR" or "NCHAR")
        {
            return col.MaxLength == -1 ? $"{upper}(MAX)" : $"{upper}({col.MaxLength / 2})";
        }
        if (upper is "VARCHAR" or "CHAR" or "VARBINARY")
        {
            return col.MaxLength == -1 ? $"{upper}(MAX)" : $"{upper}({col.MaxLength})";
        }
        if (upper is "DECIMAL" or "NUMERIC")
        {
            return $"{upper}({col.Precision},{col.Scale})";
        }

        return upper;
    }

    private static async Task<SqlServerCatalogRows> ReadCatalogAsync(
        SqlConnection connection,
        string schemaFilter,
        CancellationToken cancellationToken)
    {
        var tables = new List<SqlServerTableRow>();
        await using (var cmd = new SqlCommand("""
            SELECT TOP (501)
              s.name AS schema_name,
              t.name AS table_name,
              CAST(ep.value AS NVARCHAR(MAX)) AS table_comment
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN sys.extended_properties ep
              ON ep.major_id = t.object_id AND ep.minor_id = 0 AND ep.class = 1 AND ep.name = 'MS_Description'
            WHERE t.is_ms_shipped = 0
              AND (@schemaFilter = '' OR s.name = @schemaFilter)
            ORDER BY s.name, t.name
            """, connection))
        {
            cmd.Parameters.Add("@schemaFilter", SqlDbType.NVarChar, 128).Value = schemaFilter;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(new SqlServerTableRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        if (tables.Count == 0)
        {
            return new SqlServerCatalogRows([], [], [], [], [], []);
        }

        var columns = new List<SqlServerColumnRow>();
        await using (var cmd = new SqlCommand("""
            SELECT
              s.name AS schema_name,
              t.name AS table_name,
              c.name AS column_name,
              ty.name AS type_name,
              c.max_length,
              c.precision,
              c.scale,
              c.is_nullable,
              c.is_identity,
              dc.definition AS default_definition,
              c.column_id,
              CAST(ep.value AS NVARCHAR(MAX)) AS column_comment
            FROM sys.columns c
            JOIN sys.tables t ON c.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
            LEFT JOIN sys.extended_properties ep
              ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.class = 1 AND ep.name = 'MS_Description'
            WHERE t.is_ms_shipped = 0
              AND (@schemaFilter = '' OR s.name = @schemaFilter)
            ORDER BY s.name, t.name, c.column_id
            """, connection))
        {
            cmd.Parameters.Add("@schemaFilter", SqlDbType.NVarChar, 128).Value = schemaFilter;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new SqlServerColumnRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt16(4),
                    reader.GetByte(5),
                    reader.GetByte(6),
                    reader.GetBoolean(7),
                    reader.GetBoolean(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11)));
            }
        }

        var keys = new List<SqlServerKeyRow>();
        await using (var cmd = new SqlCommand("""
            SELECT
              s.name AS schema_name,
              t.name AS table_name,
              i.name AS constraint_name,
              i.is_primary_key,
              i.is_unique_constraint,
              c.name AS column_name,
              ic.key_ordinal
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE (i.is_primary_key = 1 OR i.is_unique_constraint = 1)
              AND t.is_ms_shipped = 0
              AND (@schemaFilter = '' OR s.name = @schemaFilter)
            ORDER BY s.name, t.name, i.name, ic.key_ordinal
            """, connection))
        {
            cmd.Parameters.Add("@schemaFilter", SqlDbType.NVarChar, 128).Value = schemaFilter;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add(new SqlServerKeyRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetBoolean(4),
                    reader.GetString(5),
                    reader.GetByte(6)));
            }
        }

        var checks = new List<SqlServerCheckRow>();
        await using (var cmd = new SqlCommand("""
            SELECT
              s.name AS schema_name,
              t.name AS table_name,
              cc.name AS constraint_name,
              cc.definition,
              c.name AS column_name
            FROM sys.check_constraints cc
            JOIN sys.tables t ON cc.parent_object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN sys.columns c ON cc.parent_object_id = c.object_id AND cc.parent_column_id = c.column_id AND cc.parent_column_id > 0
            WHERE t.is_ms_shipped = 0
              AND (@schemaFilter = '' OR s.name = @schemaFilter)
            ORDER BY s.name, t.name, cc.name
            """, connection))
        {
            cmd.Parameters.Add("@schemaFilter", SqlDbType.NVarChar, 128).Value = schemaFilter;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                checks.Add(new SqlServerCheckRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        var indexColumns = new List<SqlServerIndexColumnRow>();
        await using (var cmd = new SqlCommand("""
            SELECT
              s.name AS schema_name,
              t.name AS table_name,
              i.name AS index_name,
              i.is_unique,
              i.type_desc,
              i.has_filter,
              ic.is_included_column,
              ic.key_ordinal,
              c.name AS column_name,
              i.filter_definition
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE i.is_primary_key = 0
              AND i.type > 0
              AND i.name IS NOT NULL
              AND t.is_ms_shipped = 0
              AND (@schemaFilter = '' OR s.name = @schemaFilter)
            ORDER BY s.name, t.name, i.name, ic.key_ordinal
            """, connection))
        {
            cmd.Parameters.Add("@schemaFilter", SqlDbType.NVarChar, 128).Value = schemaFilter;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                indexColumns.Add(new SqlServerIndexColumnRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetString(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetByte(7),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            }
        }

        var fkColumns = new List<SqlServerForeignKeyColumnRow>();
        await using (var cmd = new SqlCommand("""
            SELECT
              fk.name AS fk_name,
              cs.name AS child_schema,
              ct.name AS child_table,
              cc.name AS child_column,
              ps.name AS parent_schema,
              pt.name AS parent_table,
              pc.name AS parent_column,
              fkc.constraint_column_id,
              fk.delete_referential_action_desc AS delete_action,
              fk.update_referential_action_desc AS update_action
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables ct ON fkc.parent_object_id = ct.object_id
            JOIN sys.schemas cs ON ct.schema_id = cs.schema_id
            JOIN sys.columns cc ON fkc.parent_object_id = cc.object_id AND fkc.parent_column_id = cc.column_id
            JOIN sys.tables pt ON fkc.referenced_object_id = pt.object_id
            JOIN sys.schemas ps ON pt.schema_id = ps.schema_id
            JOIN sys.columns pc ON fkc.referenced_object_id = pc.object_id AND fkc.referenced_column_id = pc.column_id
            WHERE (@schemaFilter = '' OR cs.name = @schemaFilter)
            ORDER BY cs.name, ct.name, fk.name, fkc.constraint_column_id
            """, connection))
        {
            cmd.Parameters.Add("@schemaFilter", SqlDbType.NVarChar, 128).Value = schemaFilter;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                fkColumns.Add(new SqlServerForeignKeyColumnRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetInt32(7),
                    reader.GetString(8),
                    reader.GetString(9)));
            }
        }

        return new SqlServerCatalogRows(tables, columns, keys, checks, indexColumns, fkColumns);
    }

    internal static DatabaseIntrospectionException ClassifyException(Exception ex, bool isConnectionPhase)
    {
        if (ex is OperationCanceledException or TimeoutException)
        {
            return isConnectionPhase
                ? new DatabaseIntrospectionException("Connection timed out.", 504, "connection_timeout")
                : new DatabaseIntrospectionException("Query timed out.", 504, "query_timeout");
        }

        if (ex is SqlException sqlEx)
        {
            return sqlEx.Number switch
            {
                18456 or 18452 => new DatabaseIntrospectionException("Authentication failed.", 422, "authentication_failed"),
                4060 or 911 => new DatabaseIntrospectionException("Database not found.", 422, "database_not_found"),
                -2 => isConnectionPhase
                    ? new DatabaseIntrospectionException("Connection timed out.", 504, "connection_timeout")
                    : new DatabaseIntrospectionException("Query timed out.", 504, "query_timeout"),
                53 or -1 or 2 or 10060 or 10061 => new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable"),
                _ => isConnectionPhase
                    ? new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable")
                    : new DatabaseIntrospectionException("Metadata query failed.", 502, "metadata_query_failed"),
            };
        }

        return isConnectionPhase
            ? new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable")
            : new DatabaseIntrospectionException("Metadata query failed.", 502, "metadata_query_failed");
    }
}
