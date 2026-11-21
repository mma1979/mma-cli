using System.Data.Common;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Results;
using MySqlConnector;

namespace Mma.Cli.Database.Introspection;

public sealed class MySqlIntrospector : IDatabaseIntrospector
{
    public const string ColumnsSqlQuery = """
        SELECT
            c.TABLE_SCHEMA,
            c.TABLE_NAME,
            c.COLUMN_NAME,
            CASE
                WHEN LOWER(c.COLUMN_TYPE) LIKE 'tinyint(1)%' OR LOWER(c.COLUMN_TYPE) LIKE 'bit(1)%' THEN c.COLUMN_TYPE
                ELSE c.DATA_TYPE
            END AS data_type,
            c.CHARACTER_MAXIMUM_LENGTH,
            (c.IS_NULLABLE = 'YES') AS is_nullable,
            EXISTS (
                SELECT 1
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS kcu
                    ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
                   AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                   AND tc.TABLE_NAME = kcu.TABLE_NAME
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                  AND tc.TABLE_SCHEMA = c.TABLE_SCHEMA
                  AND tc.TABLE_NAME = c.TABLE_NAME
                  AND kcu.COLUMN_NAME = c.COLUMN_NAME
            ) AS is_primary_key,
            EXISTS (
                SELECT 1
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS kcu
                WHERE kcu.TABLE_SCHEMA = c.TABLE_SCHEMA
                  AND kcu.TABLE_NAME = c.TABLE_NAME
                  AND kcu.COLUMN_NAME = c.COLUMN_NAME
                  AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
            ) AS is_foreign_key
        FROM INFORMATION_SCHEMA.COLUMNS AS c
        INNER JOIN INFORMATION_SCHEMA.TABLES AS t
            ON t.TABLE_SCHEMA = c.TABLE_SCHEMA
           AND t.TABLE_NAME = c.TABLE_NAME
           AND t.TABLE_TYPE = 'BASE TABLE'
        WHERE c.TABLE_SCHEMA = DATABASE()
        ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION;
        """;

    public const string RelationsSqlQuery = """
        SELECT
            kcu.CONSTRAINT_NAME,
            COALESCE(kcu.REFERENCED_TABLE_SCHEMA, kcu.TABLE_SCHEMA) AS parent_schema,
            kcu.REFERENCED_TABLE_NAME AS parent_table,
            kcu.TABLE_SCHEMA AS child_schema,
            kcu.TABLE_NAME AS child_table,
            kcu.COLUMN_NAME AS foreign_key_column,
            CASE
                WHEN LOWER(col.COLUMN_TYPE) LIKE 'tinyint(1)%' OR LOWER(col.COLUMN_TYPE) LIKE 'bit(1)%' THEN col.COLUMN_TYPE
                ELSE col.DATA_TYPE
            END AS data_type,
            rc.DELETE_RULE AS delete_action
        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS kcu
        INNER JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS AS rc
            ON rc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
           AND rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
           AND rc.TABLE_NAME = kcu.TABLE_NAME
        INNER JOIN INFORMATION_SCHEMA.COLUMNS AS col
            ON col.TABLE_SCHEMA = kcu.TABLE_SCHEMA
           AND col.TABLE_NAME = kcu.TABLE_NAME
           AND col.COLUMN_NAME = kcu.COLUMN_NAME
        WHERE kcu.TABLE_SCHEMA = DATABASE()
          AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
        ORDER BY kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION;
        """;

    private readonly SchemaNamingService _naming;
    private readonly ILogger<MySqlIntrospector>? _logger;

    public MySqlIntrospector(
        SchemaNamingService naming,
        ILogger<MySqlIntrospector>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(naming);
        _naming = naming;
        _logger = logger;
    }

    public DatabaseProviderKind Provider => DatabaseProviderKind.MySql;

    public OperationResult<string> BuildConnectionString(DatabaseConnectionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(parameters.ServerOrPath))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "MySQL server host cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(parameters.Database))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "MySQL database name cannot be empty.");
        }

        if (parameters.Port is <= 0 or > 65535)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "MySQL port must be between 1 and 65535.");
        }

        try
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = parameters.ServerOrPath.Trim(),
                Port = (uint)(parameters.Port ?? 3306),
                Database = parameters.Database.Trim(),
                AllowUserVariables = false
            };

            if (!string.IsNullOrWhiteSpace(parameters.Username))
            {
                builder.UserID = parameters.Username.Trim();
            }

            if (!string.IsNullOrEmpty(parameters.Password))
            {
                builder.Password = parameters.Password;
            }

            builder.SslMode = parameters.TrustServerCertificate
                ? MySqlSslMode.Preferred
                : MySqlSslMode.VerifyFull;

            return OperationResult<string>.Success(builder.ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid MySQL connection parameters: {DatabaseIntrospectionHelper.RedactSecrets(ex.Message, parameters.Password)}",
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
            return OperationResult.Failure(ErrorCodes.CANCELLED, "MySQL connection test was cancelled.");
        }

        var connResult = DatabaseIntrospectionHelper.ResolveConnectionString(
            Provider,
            request,
            BuildConnectionString,
            ValidateRawConnectionString,
            out var secrets);

        if (!connResult.IsSuccess || connResult.Value is null)
        {
            return OperationResult.Failure(
                connResult.Error ?? new GenerationError(ErrorCodes.VALIDATION_ERROR, "Invalid MySQL connection request."));
        }

        try
        {
            await using var connection = new MySqlConnection(connResult.Value);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            _ = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult.Failure(ErrorCodes.CANCELLED, "MySQL connection test was cancelled.");
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            var redactedMessage = DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets);
            _logger?.LogWarning("MySQL connection test failed: {Reason}", redactedMessage);
            return OperationResult.Failure(
                ErrorCodes.IO_ERROR,
                $"MySQL connection failed: {redactedMessage}",
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
                "MySQL schema introspection was cancelled.");
        }

        var connResult = DatabaseIntrospectionHelper.ResolveConnectionString(
            Provider,
            request,
            BuildConnectionString,
            ValidateRawConnectionString,
            out var secrets);

        if (!connResult.IsSuccess || connResult.Value is null)
        {
            return OperationResult<DatabaseSchemaSnapshot>.Failure(
                connResult.Error ?? new GenerationError(ErrorCodes.VALIDATION_ERROR, "Invalid MySQL connection request."));
        }

        try
        {
            await using var connection = new MySqlConnection(connResult.Value);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            var rawColumns = new List<RawColumnMetadata>();
            await using (var colCmd = connection.CreateCommand())
            {
                colCmd.CommandText = ColumnsSqlQuery;
                await using var reader = await colCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var schemaName = reader.GetString(0);
                    var tableName = reader.GetString(1);
                    var columnName = reader.GetString(2);
                    var dataType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                    int? maxLength = null;
                    if (!reader.IsDBNull(4))
                    {
                        var len = Convert.ToInt64(reader.GetValue(4));
                        if (len is > 0 and <= int.MaxValue)
                        {
                            maxLength = (int)len;
                        }
                    }

                    var isNullable = !reader.IsDBNull(5) && Convert.ToBoolean(reader.GetValue(5));
                    var isPrimaryKey = !reader.IsDBNull(6) && Convert.ToBoolean(reader.GetValue(6));
                    var isForeignKey = !reader.IsDBNull(7) && Convert.ToBoolean(reader.GetValue(7));

                    rawColumns.Add(new RawColumnMetadata(
                        SchemaName: schemaName,
                        TableName: tableName,
                        ColumnName: columnName,
                        RawDataType: dataType,
                        IsPrimaryKey: isPrimaryKey,
                        IsNullable: isNullable,
                        IsForeignKey: isForeignKey,
                        MaxLength: maxLength,
                        IsSystemTable: false));
                }
            }

            var rawRelations = new List<RawRelationMetadata>();
            await using (var relCmd = connection.CreateCommand())
            {
                relCmd.CommandText = RelationsSqlQuery;
                await using var reader = await relCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    rawRelations.Add(new RawRelationMetadata(
                        ConstraintName: reader.GetString(0),
                        ParentSchema: reader.GetString(1),
                        ParentTable: reader.GetString(2),
                        ChildSchema: reader.GetString(3),
                        ChildTable: reader.GetString(4),
                        ForeignKeyColumn: reader.GetString(5),
                        RawDataType: reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                        DeleteAction: reader.IsDBNull(7) ? "CASCADE" : reader.GetString(7)));
                }
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
                "MySQL schema introspection was cancelled.");
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            var redactedMessage = DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets);
            _logger?.LogWarning("MySQL schema introspection failed: {Reason}", redactedMessage);
            return OperationResult<DatabaseSchemaSnapshot>.Failure(
                ErrorCodes.IO_ERROR,
                $"MySQL introspection failed: {redactedMessage}",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }

    private static OperationResult<string> ValidateRawConnectionString(
        string rawConnectionString,
        IReadOnlyList<string> secrets)
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(rawConnectionString);
            if (string.IsNullOrWhiteSpace(builder.Server))
            {
                return OperationResult<string>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    "MySQL connection string must specify a Server.");
            }

            if (string.IsNullOrWhiteSpace(builder.Database))
            {
                return OperationResult<string>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    "MySQL connection string must specify a Database.");
            }

            builder.AllowUserVariables = false;
            return OperationResult<string>.Success(builder.ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid MySQL connection string: {DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets)}",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }
}
