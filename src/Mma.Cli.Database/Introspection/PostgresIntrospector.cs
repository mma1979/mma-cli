using System.Data.Common;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Results;
using Npgsql;

namespace Mma.Cli.Database.Introspection;

public sealed class PostgresIntrospector : IDatabaseIntrospector
{
    public const string ColumnsSqlQuery = """
        SELECT
            c.table_schema,
            c.table_name,
            c.column_name,
            CASE
                WHEN c.data_type IN ('USER-DEFINED', 'ARRAY') THEN c.udt_name
                ELSE c.data_type
            END AS data_type,
            c.character_maximum_length,
            (c.is_nullable = 'YES') AS is_nullable,
            (c.table_schema IN ('pg_catalog', 'information_schema') OR c.table_schema LIKE 'pg_toast%') AS is_system_table,
            EXISTS (
                SELECT 1
                FROM information_schema.table_constraints AS tc
                INNER JOIN information_schema.key_column_usage AS kcu
                    ON tc.constraint_catalog = kcu.constraint_catalog
                   AND tc.constraint_schema = kcu.constraint_schema
                   AND tc.constraint_name = kcu.constraint_name
                   AND tc.table_schema = kcu.table_schema
                   AND tc.table_name = kcu.table_name
                WHERE tc.constraint_type = 'PRIMARY KEY'
                  AND tc.table_schema = c.table_schema
                  AND tc.table_name = c.table_name
                  AND kcu.column_name = c.column_name
            ) AS is_primary_key,
            EXISTS (
                SELECT 1
                FROM information_schema.table_constraints AS tc
                INNER JOIN information_schema.key_column_usage AS kcu
                    ON tc.constraint_catalog = kcu.constraint_catalog
                   AND tc.constraint_schema = kcu.constraint_schema
                   AND tc.constraint_name = kcu.constraint_name
                   AND tc.table_schema = kcu.table_schema
                   AND tc.table_name = kcu.table_name
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND tc.table_schema = c.table_schema
                  AND tc.table_name = c.table_name
                  AND kcu.column_name = c.column_name
            ) AS is_foreign_key
        FROM information_schema.columns AS c
        INNER JOIN information_schema.tables AS t
            ON t.table_schema = c.table_schema
           AND t.table_name = c.table_name
           AND t.table_type = 'BASE TABLE'
        ORDER BY c.table_schema, c.table_name, c.ordinal_position;
        """;

    public const string RelationsSqlQuery = """
        SELECT
            tc.constraint_name,
            pk_kcu.table_schema AS parent_schema,
            pk_kcu.table_name AS parent_table,
            kcu.table_schema AS child_schema,
            kcu.table_name AS child_table,
            kcu.column_name AS foreign_key_column,
            CASE
                WHEN col.data_type IN ('USER-DEFINED', 'ARRAY') THEN col.udt_name
                ELSE col.data_type
            END AS data_type,
            rc.delete_rule AS delete_action
        FROM information_schema.table_constraints AS tc
        INNER JOIN information_schema.key_column_usage AS kcu
            ON tc.constraint_catalog = kcu.constraint_catalog
           AND tc.constraint_schema = kcu.constraint_schema
           AND tc.constraint_name = kcu.constraint_name
           AND tc.table_schema = kcu.table_schema
           AND tc.table_name = kcu.table_name
        INNER JOIN information_schema.referential_constraints AS rc
            ON tc.constraint_catalog = rc.constraint_catalog
           AND tc.constraint_schema = rc.constraint_schema
           AND tc.constraint_name = rc.constraint_name
        INNER JOIN information_schema.key_column_usage AS pk_kcu
            ON rc.unique_constraint_catalog = pk_kcu.constraint_catalog
           AND rc.unique_constraint_schema = pk_kcu.constraint_schema
           AND rc.unique_constraint_name = pk_kcu.constraint_name
           AND pk_kcu.ordinal_position = kcu.position_in_unique_constraint
        INNER JOIN information_schema.columns AS col
            ON col.table_schema = kcu.table_schema
           AND col.table_name = kcu.table_name
           AND col.column_name = kcu.column_name
        WHERE tc.constraint_type = 'FOREIGN KEY'
        ORDER BY kcu.table_schema, kcu.table_name, tc.constraint_name, kcu.ordinal_position;
        """;

    private readonly SchemaNamingService _naming;
    private readonly ILogger<PostgresIntrospector>? _logger;

    public PostgresIntrospector(
        SchemaNamingService naming,
        ILogger<PostgresIntrospector>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(naming);
        _naming = naming;
        _logger = logger;
    }

    public DatabaseProviderKind Provider => DatabaseProviderKind.PostgreSql;

    public OperationResult<string> BuildConnectionString(DatabaseConnectionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(parameters.ServerOrPath))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "PostgreSQL host name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(parameters.Database))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "PostgreSQL database name cannot be empty.");
        }

        if (parameters.Port is <= 0 or > 65535)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "PostgreSQL port must be between 1 and 65535.");
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = parameters.ServerOrPath.Trim(),
                Port = parameters.Port ?? 5432,
                Database = parameters.Database.Trim()
            };

            if (!string.IsNullOrWhiteSpace(parameters.Username))
            {
                builder.Username = parameters.Username.Trim();
            }

            if (!string.IsNullOrEmpty(parameters.Password))
            {
                builder.Password = parameters.Password;
            }

            builder.SslMode = parameters.TrustServerCertificate
                ? SslMode.Prefer
                : SslMode.VerifyFull;

            return OperationResult<string>.Success(builder.ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid PostgreSQL connection parameters: {DatabaseIntrospectionHelper.RedactSecrets(ex.Message, parameters.Password)}",
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
            return OperationResult.Failure(ErrorCodes.CANCELLED, "PostgreSQL connection test was cancelled.");
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
                connResult.Error ?? new GenerationError(ErrorCodes.VALIDATION_ERROR, "Invalid PostgreSQL connection request."));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connResult.Value);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            _ = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult.Failure(ErrorCodes.CANCELLED, "PostgreSQL connection test was cancelled.");
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            var redactedMessage = DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets);
            _logger?.LogWarning("PostgreSQL connection test failed: {Reason}", redactedMessage);
            return OperationResult.Failure(
                ErrorCodes.IO_ERROR,
                $"PostgreSQL connection failed: {redactedMessage}",
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
                "PostgreSQL schema introspection was cancelled.");
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
                connResult.Error ?? new GenerationError(ErrorCodes.VALIDATION_ERROR, "Invalid PostgreSQL connection request."));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connResult.Value);
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
                    int? maxLength = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4));
                    var isNullable = !reader.IsDBNull(5) && reader.GetBoolean(5);
                    var isSystemTable = !reader.IsDBNull(6) && reader.GetBoolean(6);
                    var isPrimaryKey = !reader.IsDBNull(7) && reader.GetBoolean(7);
                    var isForeignKey = !reader.IsDBNull(8) && reader.GetBoolean(8);

                    rawColumns.Add(new RawColumnMetadata(
                        SchemaName: schemaName,
                        TableName: tableName,
                        ColumnName: columnName,
                        RawDataType: dataType,
                        IsPrimaryKey: isPrimaryKey,
                        IsNullable: isNullable,
                        IsForeignKey: isForeignKey,
                        MaxLength: maxLength is > 0 ? maxLength : null,
                        IsSystemTable: isSystemTable));
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
                "PostgreSQL schema introspection was cancelled.");
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            var redactedMessage = DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets);
            _logger?.LogWarning("PostgreSQL schema introspection failed: {Reason}", redactedMessage);
            return OperationResult<DatabaseSchemaSnapshot>.Failure(
                ErrorCodes.IO_ERROR,
                $"PostgreSQL introspection failed: {redactedMessage}",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }

    private static OperationResult<string> ValidateRawConnectionString(
        string rawConnectionString,
        IReadOnlyList<string> secrets)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(rawConnectionString);
            if (string.IsNullOrWhiteSpace(builder.Host))
            {
                return OperationResult<string>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    "PostgreSQL connection string must specify a Host.");
            }

            return OperationResult<string>.Success(builder.ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid PostgreSQL connection string: {DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets)}",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }
}
