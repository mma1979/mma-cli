using System.Net.Sockets;
using Npgsql;

public sealed record PostgreSqlTableRow(
    string TableSchema,
    string TableName,
    string? TableComment);

public sealed record PostgreSqlColumnRow(
    string TableSchema,
    string TableName,
    string ColumnName,
    string DataType,
    string UdtName,
    int? CharacterMaximumLength,
    int? NumericPrecision,
    int? NumericScale,
    bool IsNullable,
    string? ColumnDefault,
    bool IsIdentity,
    int OrdinalPosition,
    string? ColumnComment);

public sealed record PostgreSqlKeyRow(
    string TableSchema,
    string TableName,
    string ConstraintName,
    string ConstraintType,
    string ColumnName,
    int OrdinalPosition);

public sealed record PostgreSqlCheckRow(
    string TableSchema,
    string TableName,
    string ConstraintName,
    string Expression,
    IReadOnlyList<string> Columns);

public sealed record PostgreSqlIndexRow(
    string TableSchema,
    string TableName,
    string IndexName,
    bool IsUnique,
    string AccessMethod,
    bool HasPredicate,
    bool HasExpressions,
    bool HasIncludedColumns,
    IReadOnlyList<string> KeyColumns,
    string? WhereClause = null,
    IReadOnlyList<string>? IncludedColumns = null);

public sealed record PostgreSqlForeignKeyRow(
    string ConstraintName,
    string ChildSchema,
    string ChildTable,
    IReadOnlyList<string> ChildColumns,
    string ParentSchema,
    string ParentTable,
    IReadOnlyList<string> ParentColumns,
    string DeleteRule,
    string UpdateRule);

public sealed record PostgreSqlCatalogRows(
    IReadOnlyList<PostgreSqlTableRow> Tables,
    IReadOnlyList<PostgreSqlColumnRow> Columns,
    IReadOnlyList<PostgreSqlKeyRow> Keys,
    IReadOnlyList<PostgreSqlCheckRow> Checks,
    IReadOnlyList<PostgreSqlIndexRow> Indexes,
    IReadOnlyList<PostgreSqlForeignKeyRow> ForeignKeys);

public sealed class PostgreSqlIntrospectionProvider : IDatabaseIntrospectionProvider
{
    public string Engine => "postgresql";

    public async Task<IntrospectionResult> IntrospectAsync(
        IntrospectionRequest request,
        ValidatedDatabaseTarget target,
        CancellationToken cancellationToken)
    {
        var useSsl = request.Ssl ?? true;
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = target.Address.ToString(),
            Port = target.Port,
            Database = request.Database,
            Username = request.Username,
            Password = request.Password ?? string.Empty,
            Timeout = 8,
            CommandTimeout = 10,
            Pooling = false,
            SslMode = useSsl ? SslMode.VerifyFull : SslMode.Disable,
        };

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(csb.ConnectionString);
        if (useSsl)
        {
            dataSourceBuilder.UseSslClientAuthenticationOptionsCallback(options =>
            {
                options.TargetHost = target.OriginalHost;
            });
        }

        await using var dataSource = dataSourceBuilder.Build();

        NpgsqlConnection connection;
        try
        {
            connection = await dataSource.OpenConnectionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw ClassifyException(ex, isConnectionPhase: true);
        }

        await using (connection)
        {
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
    }

    internal static IntrospectionResult MapCatalog(string database, PostgreSqlCatalogRows rows)
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

        foreach (var constraintGroup in rows.Keys.GroupBy(k => (k.TableSchema, k.TableName, k.ConstraintName, k.ConstraintType)))
        {
            var members = constraintGroup.OrderBy(k => k.OrdinalPosition).ToList();
            var tblKey = $"{constraintGroup.Key.TableSchema}.{constraintGroup.Key.TableName}";
            if (constraintGroup.Key.ConstraintType.Equals("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var member in members)
                {
                    pkCols.Add($"{tblKey}.{member.ColumnName}");
                }
            }
            else if (constraintGroup.Key.ConstraintType.Equals("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                if (members.Count == 1)
                {
                    singleColUniqueCols.Add($"{tblKey}.{members[0].ColumnName}");
                }
                else if (members.Count > 1)
                {
                    compositeUniqueGroups.Add((tblKey, constraintGroup.Key.ConstraintName, members.Select(m => m.ColumnName).ToList()));
                }
            }
        }

        var singleColChecks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var tableChecksByTable = new Dictionary<string, List<IntrospectionCheckConstraint>>(StringComparer.Ordinal);
        var chkCounter = 0;

        foreach (var check in rows.Checks)
        {
            var cleanedExpr = CatalogMappingHelpers.StripOuterCheckWrapper(check.Expression);
            var tblKey = $"{check.TableSchema}.{check.TableName}";
            if (check.Columns.Count == 1)
            {
                var colKey = $"{tblKey}.{check.Columns[0]}";
                if (!singleColChecks.TryGetValue(colKey, out var list))
                {
                    list = [];
                    singleColChecks[colKey] = list;
                }
                list.Add(cleanedExpr);
            }
            else
            {
                if (!tableChecksByTable.TryGetValue(tblKey, out var list))
                {
                    list = [];
                    tableChecksByTable[tblKey] = list;
                }
                list.Add(new IntrospectionCheckConstraint(
                    Id: $"chk-pg-{++chkCounter}",
                    Name: check.ConstraintName,
                    Expression: cleanedExpr));
            }
        }

        var colsByTable = new Dictionary<string, List<IntrospectionColumn>>(StringComparer.Ordinal);
        var colIdLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        var colOrdinalCounter = 0;

        foreach (var col in rows.Columns
            .OrderBy(c => c.TableSchema, StringComparer.Ordinal)
            .ThenBy(c => c.TableName, StringComparer.Ordinal)
            .ThenBy(c => c.OrdinalPosition))
        {
            var tblKey = $"{col.TableSchema}.{col.TableName}";
            var colKey = $"{tblKey}.{col.ColumnName}";
            var (formattedType, isSeqAutoInc) = FormatColumnType(col);
            var isPk = pkCols.Contains(colKey);
            var isAutoInc = isSeqAutoInc || col.IsIdentity;
            var colId = $"col-pg-{++colOrdinalCounter}";
            colIdLookup[colKey] = colId;

            var cleanDefault = isSeqAutoInc && (col.ColumnDefault?.StartsWith("nextval(", StringComparison.OrdinalIgnoreCase) ?? false)
                ? string.Empty
                : col.ColumnDefault ?? string.Empty;

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
                IsAutoIncrement: isAutoInc,
                DefaultValue: cleanDefault,
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
            list.Add(new IntrospectionUniqueConstraint($"uq-pg-{++uqCounter}", constraintName, resolvedIds));
        }

        var indexesByTable = new Dictionary<string, List<IntrospectionIndex>>(StringComparer.Ordinal);
        var idxCounter = 0;
        foreach (var index in rows.Indexes
            .OrderBy(i => i.TableSchema, StringComparer.Ordinal)
            .ThenBy(i => i.TableName, StringComparer.Ordinal)
            .ThenBy(i => i.IndexName, StringComparer.Ordinal))
        {
            var tblKey = $"{index.TableSchema}.{index.TableName}";
            if (index.HasExpressions)
            {
                warnings.Add(new MetadataWarning(
                    "unsupported_index_semantics",
                    $"Index '{index.IndexName}' on {tblKey} uses expression columns; only plain key columns are preserved."));
            }

            var resolvedColIds = new List<string>();
            foreach (var colName in index.KeyColumns)
            {
                if (colIdLookup.TryGetValue($"{tblKey}.{colName}", out var colId))
                {
                    resolvedColIds.Add(colId);
                }
            }

            if (resolvedColIds.Count == 0)
            {
                continue;
            }

            List<string>? includeColIds = null;
            if (index.IncludedColumns is { Count: > 0 })
            {
                includeColIds = index.IncludedColumns
                    .Select(c => colIdLookup.TryGetValue($"{tblKey}.{c}", out var id) ? id : null)
                    .Where(id => id is not null)
                    .Cast<string>()
                    .ToList();
                if (includeColIds.Count == 0) includeColIds = null;
            }

            var method = !string.IsNullOrWhiteSpace(index.AccessMethod)
                && !index.AccessMethod.Equals("btree", StringComparison.OrdinalIgnoreCase)
                ? index.AccessMethod.ToLowerInvariant()
                : null;
            var whereClause = !string.IsNullOrWhiteSpace(index.WhereClause) ? index.WhereClause.Trim() : null;

            if (!indexesByTable.TryGetValue(tblKey, out var idxList))
            {
                idxList = [];
                indexesByTable[tblKey] = idxList;
            }

            idxList.Add(new IntrospectionIndex(
                Id: $"idx-pg-{++idxCounter}",
                Name: index.IndexName,
                Columns: resolvedColIds,
                IsUnique: index.IsUnique,
                Method: method,
                WhereClause: whereClause,
                IncludeColumns: includeColIds));
        }

        var tableIdLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        var tables = new List<IntrospectionTable>(tablesInput.Count);
        for (var i = 0; i < tablesInput.Count; i++)
        {
            var t = tablesInput[i];
            var tblKey = $"{t.TableSchema}.{t.TableName}";
            var tblId = $"tbl-pg-{i + 1}";
            tableIdLookup[tblKey] = tblId;
            var (x, y) = CatalogMappingHelpers.GetGridPosition(i);

            tables.Add(new IntrospectionTable(
                Id: tblId,
                Name: t.TableName,
                Schema: string.IsNullOrWhiteSpace(t.TableSchema) ? "public" : t.TableSchema,
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

        foreach (var fk in rows.ForeignKeys)
        {
            if (fk.ChildColumns.Count == 0 || fk.ChildColumns.Count != fk.ParentColumns.Count)
            {
                continue;
            }

            var srcTblKey = $"{fk.ParentSchema}.{fk.ParentTable}";
            var tgtTblKey = $"{fk.ChildSchema}.{fk.ChildTable}";

            if (!tableIdLookup.TryGetValue(srcTblKey, out var sourceTableId)
                || !tableIdLookup.TryGetValue(tgtTblKey, out var targetTableId))
            {
                continue;
            }

            var sourceColIds = new List<string>(fk.ParentColumns.Count);
            var targetColIds = new List<string>(fk.ChildColumns.Count);
            for (var i = 0; i < fk.ChildColumns.Count; i++)
            {
                if (colIdLookup.TryGetValue($"{srcTblKey}.{fk.ParentColumns[i]}", out var srcColId)
                    && colIdLookup.TryGetValue($"{tgtTblKey}.{fk.ChildColumns[i]}", out var tgtColId))
                {
                    sourceColIds.Add(srcColId);
                    targetColIds.Add(tgtColId);
                }
            }

            if (sourceColIds.Count != fk.ChildColumns.Count)
            {
                continue;
            }

            var dedup = $"{sourceTableId}:{string.Join(",", sourceColIds)}->{targetTableId}:{string.Join(",", targetColIds)}";
            if (!seenRelations.Add(dedup))
            {
                continue;
            }

            var isComposite = sourceColIds.Count > 1;
            var isChildUnique = !isComposite && singleColUniqueCols.Contains($"{tgtTblKey}.{fk.ChildColumns[0]}");

            relations.Add(new IntrospectionRelation(
                Id: $"rel-pg-{++relCounter}",
                Name: string.IsNullOrWhiteSpace(fk.ConstraintName) ? $"fk_{fk.ChildTable}_{fk.ChildColumns[0]}" : fk.ConstraintName,
                SourceTableId: sourceTableId,
                SourceColumnId: sourceColIds[0],
                TargetTableId: targetTableId,
                TargetColumnId: targetColIds[0],
                Cardinality: isChildUnique ? "1:1" : "1:N",
                OnDelete: CatalogMappingHelpers.NormalizeReferentialAction(fk.DeleteRule),
                OnUpdate: CatalogMappingHelpers.NormalizeReferentialAction(fk.UpdateRule),
                SourceColumnIds: isComposite ? sourceColIds : null,
                TargetColumnIds: isComposite ? targetColIds : null));
        }

        return new IntrospectionResult(
            Database: database,
            Engine: "postgresql",
            Tables: tables,
            Relations: relations,
            Warnings: warnings);
    }

    private static (string Type, bool IsAutoIncrement) FormatColumnType(PostgreSqlColumnRow row)
    {
        var def = row.ColumnDefault ?? string.Empty;
        var isSeq = def.StartsWith("nextval(", StringComparison.OrdinalIgnoreCase);
        var dt = (row.DataType ?? string.Empty).ToLowerInvariant();
        var udt = (row.UdtName ?? string.Empty).ToLowerInvariant();

        if (isSeq && (dt == "bigint" || udt == "int8")) return ("bigserial", true);
        if (isSeq && (dt == "integer" || udt == "int4")) return ("serial", true);
        if (dt == "character varying")
        {
            return (row.CharacterMaximumLength.HasValue ? $"varchar({row.CharacterMaximumLength.Value})" : "varchar(255)", false);
        }
        if (dt == "character")
        {
            return (row.CharacterMaximumLength.HasValue ? $"char({row.CharacterMaximumLength.Value})" : "char(1)", false);
        }
        if (dt == "timestamp with time zone") return ("timestamptz", false);
        if (dt == "timestamp without time zone") return ("timestamp", false);
        if (dt == "numeric" && row.NumericPrecision.HasValue)
        {
            return ($"numeric({row.NumericPrecision.Value},{row.NumericScale ?? 0})", false);
        }
        if (dt == "array")
        {
            var baseType = udt.StartsWith('_') ? udt[1..] : udt;
            return ($"{baseType}[]", false);
        }
        if (dt == "user-defined") return (string.IsNullOrEmpty(udt) ? "text" : udt, false);
        return (!string.IsNullOrEmpty(dt) ? dt : !string.IsNullOrEmpty(udt) ? udt : "text", false);
    }

    private static async Task<PostgreSqlCatalogRows> ReadCatalogAsync(
        NpgsqlConnection connection,
        string schemaFilter,
        CancellationToken cancellationToken)
    {
        var tables = new List<PostgreSqlTableRow>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT
              t.table_schema,
              t.table_name,
              obj_description(c.oid, 'pg_class') AS table_comment
            FROM information_schema.tables t
            JOIN pg_catalog.pg_namespace n ON n.nspname = t.table_schema
            JOIN pg_catalog.pg_class c ON c.relnamespace = n.oid AND c.relname = t.table_name
            WHERE t.table_type = 'BASE TABLE'
              AND t.table_schema NOT IN ('pg_catalog', 'information_schema')
              AND (@schemaFilter = '' OR t.table_schema = @schemaFilter)
            ORDER BY t.table_schema, t.table_name
            LIMIT 501
            """, connection))
        {
            cmd.Parameters.AddWithValue("schemaFilter", schemaFilter);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(new PostgreSqlTableRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        if (tables.Count == 0)
        {
            return new PostgreSqlCatalogRows([], [], [], [], [], []);
        }

        var columns = new List<PostgreSqlColumnRow>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT
              c.table_schema,
              c.table_name,
              c.column_name,
              c.data_type,
              c.udt_name,
              c.character_maximum_length,
              c.numeric_precision,
              c.numeric_scale,
              c.is_nullable = 'YES' AS is_nullable,
              c.column_default,
              c.is_identity = 'YES' AS is_identity,
              c.ordinal_position,
              pg_catalog.col_description(cls.oid, a.attnum) AS column_comment
            FROM information_schema.columns c
            JOIN pg_catalog.pg_namespace n ON n.nspname = c.table_schema
            JOIN pg_catalog.pg_class cls ON cls.relnamespace = n.oid AND cls.relname = c.table_name
            LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid = cls.oid AND a.attname = c.column_name AND a.attnum > 0 AND NOT a.attisdropped
            WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema')
              AND (@schemaFilter = '' OR c.table_schema = @schemaFilter)
            ORDER BY c.table_schema, c.table_name, c.ordinal_position
            """, connection))
        {
            cmd.Parameters.AddWithValue("schemaFilter", schemaFilter);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new PostgreSqlColumnRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.GetBoolean(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetBoolean(10),
                    reader.GetInt32(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12)));
            }
        }

        var keys = new List<PostgreSqlKeyRow>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT
              tc.table_schema,
              tc.table_name,
              tc.constraint_name,
              tc.constraint_type,
              kcu.column_name,
              kcu.ordinal_position
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type IN ('PRIMARY KEY', 'UNIQUE')
              AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
              AND (@schemaFilter = '' OR tc.table_schema = @schemaFilter)
            ORDER BY tc.table_schema, tc.table_name, tc.constraint_name, kcu.ordinal_position
            """, connection))
        {
            cmd.Parameters.AddWithValue("schemaFilter", schemaFilter);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add(new PostgreSqlKeyRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5)));
            }
        }

        var checks = new List<PostgreSqlCheckRow>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT
              n.nspname AS table_schema,
              c.relname AS table_name,
              con.conname AS constraint_name,
              pg_catalog.pg_get_constraintdef(con.oid) AS expression,
              ARRAY(
                SELECT a.attname
                FROM unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_catalog.pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum
                ORDER BY k.ord
              ) AS columns
            FROM pg_catalog.pg_constraint con
            JOIN pg_catalog.pg_class c ON c.oid = con.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE con.contype = 'c'
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND (@schemaFilter = '' OR n.nspname = @schemaFilter)
            ORDER BY n.nspname, c.relname, con.conname
            """, connection))
        {
            cmd.Parameters.AddWithValue("schemaFilter", schemaFilter);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                checks.Add(new PostgreSqlCheckRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? [] : reader.GetFieldValue<string[]>(4)));
            }
        }

        var indexes = new List<PostgreSqlIndexRow>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT
              n.nspname AS table_schema,
              t.relname AS table_name,
              ic.relname AS index_name,
              i.indisunique,
              am.amname AS access_method,
              i.indpred IS NOT NULL AS has_predicate,
              (0 = ANY(i.indkey::int2[])) AS has_expressions,
              (i.indnkeyatts < i.indnatts) AS has_included_columns,
              ARRAY(
                SELECT a.attname
                FROM unnest(i.indkey::int2[]) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_catalog.pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
                WHERE k.attnum > 0 AND k.ord <= i.indnkeyatts
                ORDER BY k.ord
              ) AS key_columns,
              pg_catalog.pg_get_expr(i.indpred, i.indrelid) AS where_clause,
              ARRAY(
                SELECT a.attname
                FROM unnest(i.indkey::int2[]) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_catalog.pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
                WHERE k.attnum > 0 AND k.ord > i.indnkeyatts
                ORDER BY k.ord
              ) AS included_columns
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class t ON t.oid = i.indrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_catalog.pg_class ic ON ic.oid = i.indexrelid
            JOIN pg_catalog.pg_am am ON am.oid = ic.relam
            WHERE NOT i.indisprimary
              AND t.relkind = 'r'
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND (@schemaFilter = '' OR n.nspname = @schemaFilter)
            ORDER BY n.nspname, t.relname, ic.relname
            """, connection))
        {
            cmd.Parameters.AddWithValue("schemaFilter", schemaFilter);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                indexes.Add(new PostgreSqlIndexRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetString(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7),
                    reader.IsDBNull(8) ? [] : reader.GetFieldValue<string[]>(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<string[]>(10)));
            }
        }

        var fks = new List<PostgreSqlForeignKeyRow>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT
              con.conname AS constraint_name,
              cn.nspname AS child_schema,
              ct.relname AS child_table,
              ARRAY(
                SELECT a.attname
                FROM unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_catalog.pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum
                ORDER BY k.ord
              ) AS child_columns,
              pn.nspname AS parent_schema,
              pt.relname AS parent_table,
              ARRAY(
                SELECT a.attname
                FROM unnest(con.confkey) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_catalog.pg_attribute a ON a.attrelid = con.confrelid AND a.attnum = k.attnum
                ORDER BY k.ord
              ) AS parent_columns,
              CASE con.confdeltype
                WHEN 'c' THEN 'CASCADE'
                WHEN 'n' THEN 'SET NULL'
                WHEN 'd' THEN 'SET DEFAULT'
                WHEN 'r' THEN 'RESTRICT'
                ELSE 'NO ACTION'
              END AS delete_rule,
              CASE con.confupdtype
                WHEN 'c' THEN 'CASCADE'
                WHEN 'n' THEN 'SET NULL'
                WHEN 'd' THEN 'SET DEFAULT'
                WHEN 'r' THEN 'RESTRICT'
                ELSE 'NO ACTION'
              END AS update_rule
            FROM pg_catalog.pg_constraint con
            JOIN pg_catalog.pg_class ct ON ct.oid = con.conrelid
            JOIN pg_catalog.pg_namespace cn ON cn.oid = ct.relnamespace
            JOIN pg_catalog.pg_class pt ON pt.oid = con.confrelid
            JOIN pg_catalog.pg_namespace pn ON pn.oid = pt.relnamespace
            WHERE con.contype = 'f'
              AND cn.nspname NOT IN ('pg_catalog', 'information_schema')
              AND (@schemaFilter = '' OR cn.nspname = @schemaFilter)
            ORDER BY cn.nspname, ct.relname, con.conname
            """, connection))
        {
            cmd.Parameters.AddWithValue("schemaFilter", schemaFilter);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                fks.Add(new PostgreSqlForeignKeyRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetFieldValue<string[]>(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetFieldValue<string[]>(6),
                    reader.GetString(7),
                    reader.GetString(8)));
            }
        }

        return new PostgreSqlCatalogRows(tables, columns, keys, checks, indexes, fks);
    }

    internal static DatabaseIntrospectionException ClassifyException(Exception ex, bool isConnectionPhase)
    {
        if (ex is OperationCanceledException or TimeoutException)
        {
            return isConnectionPhase
                ? new DatabaseIntrospectionException("Connection timed out.", 504, "connection_timeout")
                : new DatabaseIntrospectionException("Query timed out.", 504, "query_timeout");
        }

        if (ex is PostgresException pgEx)
        {
            return pgEx.SqlState switch
            {
                "28P01" or "28000" => new DatabaseIntrospectionException("Authentication failed.", 422, "authentication_failed"),
                "3D000" => new DatabaseIntrospectionException("Database not found.", 422, "database_not_found"),
                "3F000" => new DatabaseIntrospectionException("Schema not found.", 422, "schema_not_found"),
                "57014" => new DatabaseIntrospectionException("Query timed out.", 504, "query_timeout"),
                _ => new DatabaseIntrospectionException("Metadata query failed.", 502, "metadata_query_failed"),
            };
        }

        if (ex is NpgsqlException npgsqlEx)
        {
            if (npgsqlEx.InnerException is TimeoutException or OperationCanceledException)
            {
                return isConnectionPhase
                    ? new DatabaseIntrospectionException("Connection timed out.", 504, "connection_timeout")
                    : new DatabaseIntrospectionException("Query timed out.", 504, "query_timeout");
            }

            if (npgsqlEx.InnerException is SocketException)
            {
                return new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable");
            }

            return isConnectionPhase
                ? new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable")
                : new DatabaseIntrospectionException("Metadata query failed.", 502, "metadata_query_failed");
        }

        return isConnectionPhase
            ? new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable")
            : new DatabaseIntrospectionException("Metadata query failed.", 502, "metadata_query_failed");
    }
}
