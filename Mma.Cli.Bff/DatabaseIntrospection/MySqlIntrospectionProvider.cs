using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using MySqlConnector;

public sealed record MySqlTableRow(
    string TableName,
    string? Engine,
    string? TableCollation,
    string? TableComment);

public sealed record MySqlColumnRow(
    string TableName,
    string ColumnName,
    string ColumnType,
    bool IsNullable,
    string? ColumnKey,
    string? ColumnDefault,
    string? Extra,
    string? ColumnComment,
    int OrdinalPosition);

public sealed record MySqlKeyRow(
    string TableName,
    string ConstraintName,
    string ConstraintType,
    string ColumnName,
    int OrdinalPosition);

public sealed record MySqlCheckRow(
    string TableName,
    string ConstraintName,
    string CheckClause);

public sealed record MySqlIndexColumnRow(
    string TableName,
    string IndexName,
    bool IsUnique,
    int SeqInIndex,
    string? ColumnName,
    int? SubPart,
    string IndexType,
    string? Expression);

public sealed record MySqlForeignKeyColumnRow(
    string ConstraintName,
    string ChildTable,
    string ChildColumn,
    string ParentTable,
    string ParentColumn,
    int OrdinalPosition,
    string DeleteRule,
    string UpdateRule);

public sealed record MySqlCatalogRows(
    IReadOnlyList<MySqlTableRow> Tables,
    IReadOnlyList<MySqlColumnRow> Columns,
    IReadOnlyList<MySqlKeyRow> Keys,
    IReadOnlyList<MySqlCheckRow> Checks,
    IReadOnlyList<MySqlIndexColumnRow> IndexColumns,
    IReadOnlyList<MySqlForeignKeyColumnRow> ForeignKeyColumns);

public sealed partial class MySqlIntrospectionProvider : IDatabaseIntrospectionProvider
{
    public string Engine => "mysql";

    public async Task<IntrospectionResult> IntrospectAsync(
        IntrospectionRequest request,
        ValidatedDatabaseTarget target,
        CancellationToken cancellationToken)
    {
        var useSsl = request.Ssl ?? true;
        var csb = new MySqlConnectionStringBuilder
        {
            Server = target.Address.ToString(),
            Port = (uint)target.Port,
            Database = request.Database,
            UserID = request.Username,
            Password = request.Password ?? string.Empty,
            ConnectionTimeout = 8,
            DefaultCommandTimeout = 10,
            Pooling = false,
            SslMode = useSsl ? MySqlSslMode.VerifyFull : MySqlSslMode.None,
        };

        var dataSourceBuilder = new MySqlDataSourceBuilder(csb.ConnectionString);
        if (useSsl)
        {
            dataSourceBuilder.UseRemoteCertificateValidationCallback((_, certificate, _, sslPolicyErrors) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                if ((sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateNameMismatch) != SslPolicyErrors.None)
                {
                    return false;
                }

                using var cert2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
                return cert2.MatchesHostname(target.OriginalHost, allowWildcards: true, allowCommonName: true);
            });
        }

        await using var dataSource = dataSourceBuilder.Build();

        MySqlConnection connection;
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
                var rows = await ReadCatalogAsync(connection, request.Database, cancellationToken);
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

    internal static IntrospectionResult MapCatalog(string database, MySqlCatalogRows rows)
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
        var compositeUniqueGroups = new List<(string TableName, string ConstraintName, List<string> ColumnNames)>();

        foreach (var group in rows.Keys.GroupBy(k => (k.TableName, k.ConstraintName, k.ConstraintType)))
        {
            var members = group.OrderBy(k => k.OrdinalPosition).ToList();
            if (group.Key.ConstraintType.Equals("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var m in members)
                {
                    pkCols.Add($"{m.TableName}.{m.ColumnName}");
                }
            }
            else if (group.Key.ConstraintType.Equals("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                if (members.Count == 1)
                {
                    singleColUniqueCols.Add($"{members[0].TableName}.{members[0].ColumnName}");
                }
                else if (members.Count > 1)
                {
                    compositeUniqueGroups.Add((group.Key.TableName, group.Key.ConstraintName, members.Select(m => m.ColumnName).ToList()));
                }
            }
        }

        var columnsByTableName = rows.Columns
            .GroupBy(c => c.TableName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);

        var singleColChecks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var tableChecksByTable = new Dictionary<string, List<IntrospectionCheckConstraint>>(StringComparer.Ordinal);
        var chkCounter = 0;

        foreach (var check in rows.Checks)
        {
            var cleaned = CatalogMappingHelpers.StripOuterCheckWrapper(check.CheckClause);
            var referencedColumns = ExtractReferencedColumns(check.TableName, cleaned, columnsByTableName);
            if (referencedColumns.Count == 1)
            {
                var colKey = $"{check.TableName}.{referencedColumns[0]}";
                if (!singleColChecks.TryGetValue(colKey, out var list))
                {
                    list = [];
                    singleColChecks[colKey] = list;
                }
                list.Add(cleaned);
            }
            else
            {
                if (!tableChecksByTable.TryGetValue(check.TableName, out var list))
                {
                    list = [];
                    tableChecksByTable[check.TableName] = list;
                }
                list.Add(new IntrospectionCheckConstraint($"chk-my-{++chkCounter}", check.ConstraintName, cleaned));
            }
        }

        var colsByTable = new Dictionary<string, List<IntrospectionColumn>>(StringComparer.Ordinal);
        var colIdLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        var colCounter = 0;

        foreach (var col in rows.Columns
            .OrderBy(c => c.TableName, StringComparer.Ordinal)
            .ThenBy(c => c.OrdinalPosition))
        {
            var colKey = $"{col.TableName}.{col.ColumnName}";
            var colId = $"col-my-{++colCounter}";
            colIdLookup[colKey] = colId;

            var isPk = pkCols.Contains(colKey) || string.Equals(col.ColumnKey, "PRI", StringComparison.OrdinalIgnoreCase);
            var isUq = singleColUniqueCols.Contains(colKey) || (rows.Keys.Count == 0 && string.Equals(col.ColumnKey, "UNI", StringComparison.OrdinalIgnoreCase));
            if (isUq)
            {
                singleColUniqueCols.Add(colKey);
            }

            var isAutoInc = col.Extra?.Contains("auto_increment", StringComparison.OrdinalIgnoreCase) ?? false;
            var checkExpr = singleColChecks.TryGetValue(colKey, out var checks)
                ? string.Join(" AND ", checks)
                : string.Empty;

            var dto = new IntrospectionColumn(
                Id: colId,
                Name: col.ColumnName,
                Type: string.IsNullOrWhiteSpace(col.ColumnType) ? "VARCHAR(255)" : col.ColumnType.ToUpperInvariant(),
                IsPrimaryKey: isPk,
                IsNullable: col.IsNullable && !isPk,
                IsUnique: isUq,
                IsAutoIncrement: isAutoInc,
                DefaultValue: col.ColumnDefault ?? string.Empty,
                CheckConstraint: checkExpr,
                Comment: col.ColumnComment ?? string.Empty);

            if (!colsByTable.TryGetValue(col.TableName, out var list))
            {
                list = [];
                colsByTable[col.TableName] = list;
            }
            list.Add(dto);
        }

        var uniqueConstraintsByTable = new Dictionary<string, List<IntrospectionUniqueConstraint>>(StringComparer.Ordinal);
        var uqCounter = 0;
        foreach (var (tblName, constraintName, colNames) in compositeUniqueGroups)
        {
            var resolvedIds = colNames
                .Select(c => colIdLookup.TryGetValue($"{tblName}.{c}", out var id) ? id : null)
                .Where(id => id is not null)
                .Cast<string>()
                .ToList();
            if (resolvedIds.Count < 2) continue;

            if (!uniqueConstraintsByTable.TryGetValue(tblName, out var list))
            {
                list = [];
                uniqueConstraintsByTable[tblName] = list;
            }
            list.Add(new IntrospectionUniqueConstraint($"uq-my-{++uqCounter}", constraintName, resolvedIds));
        }

        var indexesByTable = new Dictionary<string, List<IntrospectionIndex>>(StringComparer.Ordinal);
        var idxCounter = 0;

        foreach (var indexGroup in rows.IndexColumns
            .Where(i => !i.IndexName.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => (i.TableName, i.IndexName))
            .OrderBy(g => g.Key.TableName, StringComparer.Ordinal)
            .ThenBy(g => g.Key.IndexName, StringComparer.Ordinal))
        {
            var orderedEntries = indexGroup.OrderBy(e => e.SeqInIndex).ToList();
            var indexType = orderedEntries[0].IndexType;
            var isUnique = orderedEntries[0].IsUnique;

            var unsupportedReasons = new List<string>();
            if (orderedEntries.Any(e => e.SubPart.HasValue))
            {
                unsupportedReasons.Add("prefix sub-part lengths");
            }
            if (orderedEntries.Any(e => !string.IsNullOrWhiteSpace(e.Expression)))
            {
                unsupportedReasons.Add("functional expressions");
            }

            if (unsupportedReasons.Count > 0)
            {
                warnings.Add(new MetadataWarning(
                    "unsupported_index_semantics",
                    $"Index '{indexGroup.Key.IndexName}' on {indexGroup.Key.TableName} uses unsupported features ({string.Join(", ", unsupportedReasons)}); only plain key columns are preserved."));
            }

            var resolvedColIds = new List<string>();
            foreach (var entry in orderedEntries)
            {
                if (!string.IsNullOrWhiteSpace(entry.ColumnName)
                    && colIdLookup.TryGetValue($"{indexGroup.Key.TableName}.{entry.ColumnName}", out var colId))
                {
                    resolvedColIds.Add(colId);
                }
            }

            if (resolvedColIds.Count == 0)
            {
                continue;
            }

            var method = !string.IsNullOrWhiteSpace(indexType) && !indexType.Equals("BTREE", StringComparison.OrdinalIgnoreCase)
                ? indexType.ToUpperInvariant()
                : null;

            if (!indexesByTable.TryGetValue(indexGroup.Key.TableName, out var idxList))
            {
                idxList = [];
                indexesByTable[indexGroup.Key.TableName] = idxList;
            }

            idxList.Add(new IntrospectionIndex(
                Id: $"idx-my-{++idxCounter}",
                Name: indexGroup.Key.IndexName,
                Columns: resolvedColIds,
                IsUnique: isUnique,
                Method: method));
        }

        var tableIdLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        var tables = new List<IntrospectionTable>(tablesInput.Count);
        for (var i = 0; i < tablesInput.Count; i++)
        {
            var t = tablesInput[i];
            var tblId = $"tbl-my-{i + 1}";
            tableIdLookup[t.TableName] = tblId;
            var (x, y) = CatalogMappingHelpers.GetGridPosition(i);

            var storageEngine = t.Engine is "MyISAM" or "MEMORY" ? t.Engine : "InnoDB";
            var collation = string.IsNullOrWhiteSpace(t.TableCollation) ? "utf8mb4_unicode_ci" : t.TableCollation;
            var charset = collation.Split('_')[0];
            if (string.IsNullOrWhiteSpace(charset))
            {
                charset = "utf8mb4";
            }

            tables.Add(new IntrospectionTable(
                Id: tblId,
                Name: t.TableName,
                Schema: string.Empty,
                Color: CatalogMappingHelpers.GetAccentColor(i),
                Comment: t.TableComment ?? string.Empty,
                X: x,
                Y: y,
                Columns: colsByTable.TryGetValue(t.TableName, out var cols) ? cols : [],
                Indexes: indexesByTable.TryGetValue(t.TableName, out var idxs) ? idxs : [],
                StorageEngine: storageEngine,
                Charset: charset,
                UniqueConstraints: uniqueConstraintsByTable.TryGetValue(t.TableName, out var uqs) && uqs.Count > 0 ? uqs : null,
                CheckConstraints: tableChecksByTable.TryGetValue(t.TableName, out var chks) && chks.Count > 0 ? chks : null));
        }

        var relations = new List<IntrospectionRelation>();
        var seenRelations = new HashSet<string>(StringComparer.Ordinal);
        var relCounter = 0;

        foreach (var fkGroup in rows.ForeignKeyColumns
            .GroupBy(f => (f.ChildTable, f.ConstraintName))
            .OrderBy(g => g.Key.ChildTable, StringComparer.Ordinal)
            .ThenBy(g => g.Key.ConstraintName, StringComparer.Ordinal))
        {
            var members = fkGroup.OrderBy(m => m.OrdinalPosition).ToList();
            if (members.Count == 0) continue;

            var first = members[0];
            if (!tableIdLookup.TryGetValue(first.ParentTable, out var sourceTableId)
                || !tableIdLookup.TryGetValue(first.ChildTable, out var targetTableId))
            {
                continue;
            }

            var sourceColIds = new List<string>(members.Count);
            var targetColIds = new List<string>(members.Count);
            foreach (var m in members)
            {
                if (colIdLookup.TryGetValue($"{m.ParentTable}.{m.ParentColumn}", out var srcColId)
                    && colIdLookup.TryGetValue($"{m.ChildTable}.{m.ChildColumn}", out var tgtColId))
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
            var isChildUnique = !isComposite && singleColUniqueCols.Contains($"{first.ChildTable}.{first.ChildColumn}");
            relations.Add(new IntrospectionRelation(
                Id: $"rel-my-{++relCounter}",
                Name: string.IsNullOrWhiteSpace(first.ConstraintName) ? $"fk_{first.ChildTable}_{first.ChildColumn}" : first.ConstraintName,
                SourceTableId: sourceTableId,
                SourceColumnId: sourceColIds[0],
                TargetTableId: targetTableId,
                TargetColumnId: targetColIds[0],
                Cardinality: isChildUnique ? "1:1" : "1:N",
                OnDelete: CatalogMappingHelpers.NormalizeReferentialAction(first.DeleteRule),
                OnUpdate: CatalogMappingHelpers.NormalizeReferentialAction(first.UpdateRule),
                SourceColumnIds: isComposite ? sourceColIds : null,
                TargetColumnIds: isComposite ? targetColIds : null));
        }

        return new IntrospectionResult(
            Database: database,
            Engine: "mysql",
            Tables: tables,
            Relations: relations,
            Warnings: warnings);
    }

    private static List<string> ExtractReferencedColumns(
        string tableName,
        string expression,
        Dictionary<string, HashSet<string>> columnsByTableName)
    {
        if (!columnsByTableName.TryGetValue(tableName, out var knownColumns))
        {
            return [];
        }

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in IdentifierRegex().Matches(expression))
        {
            var token = m.Value.Trim('`');
            if (knownColumns.Contains(token))
            {
                matched.Add(token);
            }
        }

        return matched.ToList();
    }

    private static async Task<MySqlCatalogRows> ReadCatalogAsync(
        MySqlConnection connection,
        string database,
        CancellationToken cancellationToken)
    {
        var tables = new List<MySqlTableRow>();
        await using (var cmd = new MySqlCommand("""
            SELECT TABLE_NAME, ENGINE, TABLE_COLLATION, TABLE_COMMENT
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @db AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME
            LIMIT 501
            """, connection))
        {
            cmd.Parameters.AddWithValue("@db", database);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(new MySqlTableRow(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        if (tables.Count == 0)
        {
            return new MySqlCatalogRows([], [], [], [], [], []);
        }

        var columns = new List<MySqlColumnRow>();
        await using (var cmd = new MySqlCommand("""
            SELECT
              TABLE_NAME,
              COLUMN_NAME,
              COLUMN_TYPE,
              IS_NULLABLE = 'YES' AS IS_NULLABLE_BOOL,
              COLUMN_KEY,
              COLUMN_DEFAULT,
              EXTRA,
              COLUMN_COMMENT,
              ORDINAL_POSITION
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @db
            ORDER BY TABLE_NAME, ORDINAL_POSITION
            """, connection))
        {
            cmd.Parameters.AddWithValue("@db", database);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new MySqlColumnRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetValue(5).ToString(),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    Convert.ToInt32(reader.GetValue(8))));
            }
        }

        var keys = new List<MySqlKeyRow>();
        await using (var cmd = new MySqlCommand("""
            SELECT
              tc.TABLE_NAME,
              tc.CONSTRAINT_NAME,
              tc.CONSTRAINT_TYPE,
              kcu.COLUMN_NAME,
              kcu.ORDINAL_POSITION
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.KEY_COLUMN_USAGE kcu
              ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
             AND tc.TABLE_NAME = kcu.TABLE_NAME
             AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
            WHERE tc.TABLE_SCHEMA = @db
              AND tc.CONSTRAINT_TYPE IN ('PRIMARY KEY', 'UNIQUE')
            ORDER BY tc.TABLE_NAME, tc.CONSTRAINT_NAME, kcu.ORDINAL_POSITION
            """, connection))
        {
            cmd.Parameters.AddWithValue("@db", database);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add(new MySqlKeyRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    Convert.ToInt32(reader.GetValue(4))));
            }
        }

        var checks = new List<MySqlCheckRow>();
        try
        {
            await using var cmd = new MySqlCommand("""
                SELECT
                  tc.TABLE_NAME,
                  tc.CONSTRAINT_NAME,
                  cc.CHECK_CLAUSE
                FROM information_schema.TABLE_CONSTRAINTS tc
                JOIN information_schema.CHECK_CONSTRAINTS cc
                  ON tc.CONSTRAINT_SCHEMA = cc.CONSTRAINT_SCHEMA
                 AND tc.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
                WHERE tc.TABLE_SCHEMA = @db
                  AND tc.CONSTRAINT_TYPE = 'CHECK'
                ORDER BY tc.TABLE_NAME, tc.CONSTRAINT_NAME
                """, connection);
            cmd.Parameters.AddWithValue("@db", database);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                checks.Add(new MySqlCheckRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)));
            }
        }
        catch (MySqlException ex) when (ex.ErrorCode is MySqlErrorCode.UnknownTable or MySqlErrorCode.NoSuchTable)
        {
            // MySQL < 8.0.16 does not expose information_schema.CHECK_CONSTRAINTS.
        }

        var indexColumns = new List<MySqlIndexColumnRow>();
        await using (var cmd = new MySqlCommand("""
            SELECT
              TABLE_NAME,
              INDEX_NAME,
              NON_UNIQUE = 0 AS IS_UNIQUE,
              SEQ_IN_INDEX,
              COLUMN_NAME,
              SUB_PART,
              INDEX_TYPE,
              EXPRESSION
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = @db
              AND INDEX_NAME <> 'PRIMARY'
            ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX
            """, connection))
        {
            cmd.Parameters.AddWithValue("@db", database);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                indexColumns.Add(new MySqlIndexColumnRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetBoolean(2),
                    Convert.ToInt32(reader.GetValue(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5)),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        var fkColumns = new List<MySqlForeignKeyColumnRow>();
        await using (var cmd = new MySqlCommand("""
            SELECT
              kcu.CONSTRAINT_NAME,
              kcu.TABLE_NAME AS child_table,
              kcu.COLUMN_NAME AS child_column,
              kcu.REFERENCED_TABLE_NAME AS parent_table,
              kcu.REFERENCED_COLUMN_NAME AS parent_column,
              kcu.ORDINAL_POSITION,
              rc.DELETE_RULE,
              rc.UPDATE_RULE
            FROM information_schema.KEY_COLUMN_USAGE kcu
            JOIN information_schema.REFERENTIAL_CONSTRAINTS rc
              ON kcu.CONSTRAINT_NAME = rc.CONSTRAINT_NAME
             AND kcu.TABLE_SCHEMA = rc.CONSTRAINT_SCHEMA
             AND kcu.TABLE_NAME = rc.TABLE_NAME
            WHERE kcu.TABLE_SCHEMA = @db
              AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY kcu.TABLE_NAME, kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION
            """, connection))
        {
            cmd.Parameters.AddWithValue("@db", database);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                fkColumns.Add(new MySqlForeignKeyColumnRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    Convert.ToInt32(reader.GetValue(5)),
                    reader.GetString(6),
                    reader.GetString(7)));
            }
        }

        return new MySqlCatalogRows(tables, columns, keys, checks, indexColumns, fkColumns);
    }

    internal static DatabaseIntrospectionException ClassifyException(Exception ex, bool isConnectionPhase)
    {
        if (ex is OperationCanceledException or TimeoutException)
        {
            return isConnectionPhase
                ? new DatabaseIntrospectionException("Connection timed out.", 504, "connection_timeout")
                : new DatabaseIntrospectionException("Query timed out.", 504, "query_timeout");
        }

        if (ex is MySqlException myEx)
        {
            return myEx.ErrorCode switch
            {
                MySqlErrorCode.AccessDenied or MySqlErrorCode.DatabaseAccessDenied =>
                    new DatabaseIntrospectionException("Authentication failed.", 422, "authentication_failed"),
                MySqlErrorCode.UnknownDatabase =>
                    new DatabaseIntrospectionException("Database not found.", 422, "database_not_found"),
                MySqlErrorCode.CommandTimeoutExpired =>
                    new DatabaseIntrospectionException("Query timed out.", 504, "query_timeout"),
                MySqlErrorCode.UnableToConnectToHost =>
                    new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable"),
                _ when myEx.InnerException is TimeoutException or OperationCanceledException =>
                    isConnectionPhase
                        ? new DatabaseIntrospectionException("Connection timed out.", 504, "connection_timeout")
                        : new DatabaseIntrospectionException("Query timed out.", 504, "query_timeout"),
                _ when myEx.InnerException is SocketException =>
                    new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable"),
                _ => isConnectionPhase
                    ? new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable")
                    : new DatabaseIntrospectionException("Metadata query failed.", 502, "metadata_query_failed"),
            };
        }

        return isConnectionPhase
            ? new DatabaseIntrospectionException("Database unreachable.", 502, "database_unreachable")
            : new DatabaseIntrospectionException("Metadata query failed.", 502, "metadata_query_failed");
    }

    [GeneratedRegex("`[^`]+`|\\b[a-zA-Z_][a-zA-Z0-9_]*\\b")]
    private static partial Regex IdentifierRegex();
}
