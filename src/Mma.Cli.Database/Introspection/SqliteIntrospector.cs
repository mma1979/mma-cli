using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Database.Introspection;

public sealed class SqliteIntrospector : IDatabaseIntrospector
{
    private const string MainSchema = "main";

    private readonly SchemaNamingService _naming;
    private readonly ILogger<SqliteIntrospector>? _logger;

    public SqliteIntrospector(
        SchemaNamingService naming,
        ILogger<SqliteIntrospector>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(naming);
        _naming = naming;
        _logger = logger;
    }

    public DatabaseProviderKind Provider => DatabaseProviderKind.Sqlite;

    public OperationResult<string> BuildConnectionString(DatabaseConnectionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(parameters.ServerOrPath))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "SQLite database file path cannot be empty.");
        }

        var trimmed = parameters.ServerOrPath.Trim();
        if (trimmed.IndexOf('\0') >= 0)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "SQLite database path contains invalid characters.");
        }

        if (IsInMemoryDataSource(trimmed))
        {
            var memBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:"
            };
            return OperationResult<string>.Success(memBuilder.ConnectionString);
        }

        try
        {
            var fullPath = Path.GetFullPath(trimmed);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            return OperationResult<string>.Success(builder.ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "Invalid SQLite database path.",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, parameters.Password));
        }
    }

    public async Task<OperationResult> TestConnectionAsync(
        DatabaseIntrospectRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ct.IsCancellationRequested)
        {
            return OperationResult.Failure(ErrorCodes.CANCELLED, "SQLite connection test was cancelled.");
        }

        var openResult = await OpenValidatedConnectionAsync(request, ct).ConfigureAwait(false);
        if (!openResult.IsSuccess || openResult.Value is null)
        {
            return OperationResult.Failure(
                openResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to open SQLite database."));
        }

        var secrets = DatabaseIntrospectionHelper.ExtractSecrets(request);
        try
        {
            await using var connection = openResult.Value;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master LIMIT 1;";
            _ = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult.Failure(ErrorCodes.CANCELLED, "SQLite connection test was cancelled.");
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            var redactedMessage = DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets);
            _logger?.LogWarning("SQLite connection test failed: {Reason}", redactedMessage);
            return OperationResult.Failure(
                ErrorCodes.IO_ERROR,
                $"SQLite connection failed: {redactedMessage}",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }

    public async Task<OperationResult<DatabaseSchemaSnapshot>> IntrospectAsync(
        DatabaseIntrospectRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<DatabaseSchemaSnapshot>.Failure(
                ErrorCodes.CANCELLED,
                "SQLite schema introspection was cancelled.");
        }

        var openResult = await OpenValidatedConnectionAsync(request, ct).ConfigureAwait(false);
        if (!openResult.IsSuccess || openResult.Value is null)
        {
            return OperationResult<DatabaseSchemaSnapshot>.Failure(
                openResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to open SQLite database."));
        }

        var secrets = DatabaseIntrospectionHelper.ExtractSecrets(request);
        try
        {
            await using var connection = openResult.Value;
            var tableNames = await ReadTableNamesAsync(connection, ct).ConfigureAwait(false);

            var rawColumns = new List<RawColumnMetadata>();
            var rawRelations = new List<RawRelationMetadata>();

            foreach (var tableName in tableNames)
            {
                ct.ThrowIfCancellationRequested();

                if (!TryEscapeSqliteIdentifier(tableName, out var escapedTable))
                {
                    return OperationResult<DatabaseSchemaSnapshot>.Failure(
                        ErrorCodes.VALIDATION_ERROR,
                        $"SQLite table name '{tableName}' contains unsupported characters.");
                }

                var isSystemTable = tableName.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase);

                var foreignKeys = await ReadForeignKeysAsync(connection, tableName, escapedTable, ct)
                    .ConfigureAwait(false);
                var fkColumnNames = foreignKeys
                    .Select(fk => fk.ForeignKeyColumn)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var cols = await ReadTableColumnsAsync(connection, tableName, escapedTable, isSystemTable, fkColumnNames, ct)
                    .ConfigureAwait(false);

                rawColumns.AddRange(cols);
                rawRelations.AddRange(foreignKeys);
            }

            var snapshot = DatabaseIntrospectionHelper.BuildSnapshot(
                Provider,
                rawColumns,
                rawRelations,
                request.Filters,
                _naming);

            return OperationResult<DatabaseSchemaSnapshot>.Success(snapshot);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult<DatabaseSchemaSnapshot>.Failure(
                ErrorCodes.CANCELLED,
                "SQLite schema introspection was cancelled.");
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            var redactedMessage = DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets);
            _logger?.LogWarning("SQLite schema introspection failed: {Reason}", redactedMessage);
            return OperationResult<DatabaseSchemaSnapshot>.Failure(
                ErrorCodes.IO_ERROR,
                $"SQLite introspection failed: {redactedMessage}",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }

    private async Task<OperationResult<SqliteConnection>> OpenValidatedConnectionAsync(
        DatabaseIntrospectRequest request,
        CancellationToken ct)
    {
        var connResult = DatabaseIntrospectionHelper.ResolveConnectionString(
            Provider,
            request,
            BuildConnectionString,
            ValidateRawConnectionString,
            out var secrets);

        if (!connResult.IsSuccess || connResult.Value is null)
        {
            return OperationResult<SqliteConnection>.Failure(
                connResult.Error ?? new GenerationError(ErrorCodes.VALIDATION_ERROR, "Invalid SQLite connection request."));
        }

        SqliteConnectionStringBuilder builder;
        try
        {
            builder = new SqliteConnectionStringBuilder(connResult.Value);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return OperationResult<SqliteConnection>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "Invalid SQLite connection string.",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }

        if (!IsInMemoryBuilder(builder))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(builder.DataSource);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return OperationResult<SqliteConnection>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    "Invalid SQLite database file path.",
                    DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
            }

            if (!File.Exists(fullPath))
            {
                return OperationResult<SqliteConnection>.Failure(
                    ErrorCodes.IO_ERROR,
                    $"SQLite database file not found: '{fullPath}'.");
            }

            builder.DataSource = fullPath;
            builder.Mode = SqliteOpenMode.ReadOnly;
            builder.Pooling = false;
        }

        var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return OperationResult<SqliteConnection>.Success(connection);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return OperationResult<SqliteConnection>.Failure(
                ErrorCodes.CANCELLED,
                "SQLite connection was cancelled.");
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            var redactedMessage = DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets);
            _logger?.LogWarning("Failed to open SQLite database: {Reason}", redactedMessage);
            return OperationResult<SqliteConnection>.Failure(
                ErrorCodes.IO_ERROR,
                $"Unable to open SQLite database: {redactedMessage}",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }

    private static OperationResult<string> ValidateRawConnectionString(
        string rawConnectionString,
        IReadOnlyList<string> secrets)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(rawConnectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                return OperationResult<string>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    "SQLite connection string must specify a Data Source.");
            }

            return OperationResult<string>.Success(builder.ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "Invalid SQLite connection string.",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }

    private static async Task<List<string>> ReadTableNamesAsync(SqliteConnection connection, CancellationToken ct)
    {
        var names = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                names.Add(reader.GetString(0));
            }
        }

        return names;
    }

    private static async Task<List<RawColumnMetadata>> ReadTableColumnsAsync(
        SqliteConnection connection,
        string tableName,
        string escapedTable,
        bool isSystemTable,
        HashSet<string> fkColumnNames,
        CancellationToken ct)
    {
        var columns = new List<RawColumnMetadata>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({escapedTable});";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var columnName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (string.IsNullOrWhiteSpace(columnName))
            {
                continue;
            }

            var rawType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var notNull = !reader.IsDBNull(3) && reader.GetInt32(3) != 0;
            var isPrimaryKey = !reader.IsDBNull(5) && reader.GetInt32(5) > 0;
            var isNullable = !isPrimaryKey && !notNull;
            var isForeignKey = fkColumnNames.Contains(columnName);

            columns.Add(new RawColumnMetadata(
                SchemaName: MainSchema,
                TableName: tableName,
                ColumnName: columnName,
                RawDataType: rawType,
                IsPrimaryKey: isPrimaryKey,
                IsNullable: isNullable,
                IsForeignKey: isForeignKey,
                MaxLength: null,
                IsSystemTable: isSystemTable));
        }

        return columns;
    }

    private static async Task<List<RawRelationMetadata>> ReadForeignKeysAsync(
        SqliteConnection connection,
        string childTableName,
        string escapedChildTable,
        CancellationToken ct)
    {
        var relations = new List<RawRelationMetadata>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list({escapedChildTable});";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var fkId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var parentTable = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var fromColumn = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var onDelete = reader.IsDBNull(6) ? "NO ACTION" : reader.GetString(6);

            if (string.IsNullOrWhiteSpace(parentTable) || string.IsNullOrWhiteSpace(fromColumn))
            {
                continue;
            }

            relations.Add(new RawRelationMetadata(
                ConstraintName: $"FK_{childTableName}_{parentTable}_{fkId}",
                ParentSchema: MainSchema,
                ParentTable: parentTable,
                ChildSchema: MainSchema,
                ChildTable: childTableName,
                ForeignKeyColumn: fromColumn,
                RawDataType: string.Empty,
                DeleteAction: onDelete));
        }

        return relations;
    }

    private static bool TryEscapeSqliteIdentifier(string identifier, out string escaped)
    {
        if (string.IsNullOrEmpty(identifier) || identifier.IndexOf('\0') >= 0)
        {
            escaped = string.Empty;
            return false;
        }

        escaped = $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return true;
    }

    private static bool IsInMemoryDataSource(string dataSource) =>
        string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase);

    private static bool IsInMemoryBuilder(SqliteConnectionStringBuilder builder) =>
        IsInMemoryDataSource(builder.DataSource) || builder.Mode == SqliteOpenMode.Memory;
}
