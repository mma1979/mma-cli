using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Database.Introspection;

public sealed class SqlServerIntrospector : IDatabaseIntrospector
{
    public const string ColumnsSqlQuery = """
        SELECT
            sch.name AS schema_name,
            tab.name AS table_name,
            col.column_id,
            col.name AS column_name,
            ISNULL(t.name, N'') AS data_type,
            CASE
                WHEN col.max_length <= 0 THEN NULL
                WHEN t.name IN (N'nvarchar', N'nchar') THEN col.max_length / 2
                WHEN t.name IN (N'varchar', N'char', N'varbinary', N'binary') THEN col.max_length
                ELSE isc.CHARACTER_MAXIMUM_LENGTH
            END AS character_maximum_length,
            col.is_nullable,
            tab.is_ms_shipped,
            CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM sys.indexes AS idx
                    INNER JOIN sys.index_columns AS ic
                        ON ic.object_id = idx.object_id
                       AND ic.index_id = idx.index_id
                    WHERE idx.object_id = col.object_id
                      AND idx.is_primary_key = 1
                      AND ic.column_id = col.column_id
                ) THEN CONVERT(bit, 1)
                ELSE CONVERT(bit, 0)
            END AS is_primary_key,
            CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys AS fk
                    INNER JOIN sys.foreign_key_columns AS fkc
                        ON fkc.constraint_object_id = fk.object_id
                       AND fkc.parent_object_id = col.object_id
                       AND fkc.parent_column_id = col.column_id
                ) THEN CONVERT(bit, 1)
                ELSE CONVERT(bit, 0)
            END AS is_foreign_key
        FROM sys.tables AS tab
        INNER JOIN sys.schemas AS sch
            ON tab.schema_id = sch.schema_id
        INNER JOIN sys.columns AS col
            ON tab.object_id = col.object_id
        LEFT JOIN sys.types AS t
            ON col.user_type_id = t.user_type_id
        LEFT JOIN INFORMATION_SCHEMA.COLUMNS AS isc
            ON isc.TABLE_SCHEMA = sch.name
           AND isc.TABLE_NAME = tab.name
           AND isc.COLUMN_NAME = col.name
        ORDER BY sch.name, tab.name, col.column_id;
        """;

    public const string RelationsSqlQuery = """
        SELECT
            fk.name AS constraint_name,
            SCHEMA_NAME(tr.schema_id) AS parent_schema,
            tr.name AS parent_table,
            SCHEMA_NAME(tp.schema_id) AS child_schema,
            tp.name AS child_table,
            cp.name AS foreign_key_column,
            ISNULL(t.name, N'') AS data_type,
            fk.delete_referential_action_desc AS delete_action
        FROM sys.foreign_keys AS fk
        INNER JOIN sys.foreign_key_columns AS fkc
            ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.tables AS tp
            ON fkc.parent_object_id = tp.object_id
        INNER JOIN sys.columns AS cp
            ON fkc.parent_object_id = cp.object_id
           AND fkc.parent_column_id = cp.column_id
        INNER JOIN sys.tables AS tr
            ON fkc.referenced_object_id = tr.object_id
        LEFT JOIN sys.types AS t
            ON cp.user_type_id = t.user_type_id
        ORDER BY child_schema, child_table, fk.name, fkc.constraint_column_id;
        """;

    private readonly SchemaNamingService _naming;
    private readonly ILogger<SqlServerIntrospector>? _logger;

    public SqlServerIntrospector(
        SchemaNamingService naming,
        ILogger<SqlServerIntrospector>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(naming);
        _naming = naming;
        _logger = logger;
    }

    public DatabaseProviderKind Provider => DatabaseProviderKind.SqlServer;

    public OperationResult<string> BuildConnectionString(DatabaseConnectionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(parameters.ServerOrPath))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "SQL Server host or instance name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(parameters.Database))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "SQL Server database name cannot be empty.");
        }

        if (parameters.Port is <= 0 or > 65535)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "SQL Server port must be between 1 and 65535.");
        }

        var server = parameters.Port.HasValue
            ? $"{parameters.ServerOrPath.Trim()},{parameters.Port.Value}"
            : parameters.ServerOrPath.Trim();

        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = parameters.Database.Trim(),
                TrustServerCertificate = parameters.TrustServerCertificate
            };

            switch (parameters.SqlServerAuth)
            {
                case SqlServerAuthKind.WindowsIntegrated:
                    builder.IntegratedSecurity = true;
                    break;

                case SqlServerAuthKind.SqlPassword:
                    if (string.IsNullOrWhiteSpace(parameters.Username))
                    {
                        return OperationResult<string>.Failure(
                            ErrorCodes.VALIDATION_ERROR,
                            "Username is required for SQL Password authentication.");
                    }

                    if (string.IsNullOrWhiteSpace(parameters.Password))
                    {
                        return OperationResult<string>.Failure(
                            ErrorCodes.VALIDATION_ERROR,
                            "Password is required for SQL Password authentication.");
                    }

                    builder.Authentication = SqlAuthenticationMethod.SqlPassword;
                    builder.UserID = parameters.Username.Trim();
                    builder.Password = parameters.Password;
                    break;

                case SqlServerAuthKind.ActiveDirectoryIntegrated:
                    builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryIntegrated;
                    break;

                default:
                    return OperationResult<string>.Failure(
                        ErrorCodes.VALIDATION_ERROR,
                        $"Unsupported SQL Server authentication kind '{parameters.SqlServerAuth}'.");
            }

            return OperationResult<string>.Success(builder.ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid SQL Server connection parameters: {DatabaseIntrospectionHelper.RedactSecrets(ex.Message, parameters.Password)}",
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
            return OperationResult.Failure(ErrorCodes.CANCELLED, "SQL Server connection test was cancelled.");
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
                connResult.Error ?? new GenerationError(ErrorCodes.VALIDATION_ERROR, "Invalid SQL Server connection request."));
        }

        try
        {
            await using var connection = new SqlConnection(connResult.Value);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            _ = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult.Failure(ErrorCodes.CANCELLED, "SQL Server connection test was cancelled.");
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            var redactedMessage = DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets);
            _logger?.LogWarning("SQL Server connection test failed: {Reason}", redactedMessage);
            return OperationResult.Failure(
                ErrorCodes.IO_ERROR,
                $"SQL Server connection failed: {redactedMessage}",
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
                "SQL Server schema introspection was cancelled.");
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
                connResult.Error ?? new GenerationError(ErrorCodes.VALIDATION_ERROR, "Invalid SQL Server connection request."));
        }

        try
        {
            await using var connection = new SqlConnection(connResult.Value);
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
                    var columnName = reader.GetString(3);
                    var dataType = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                    int? maxLength = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5));
                    var isNullable = !reader.IsDBNull(6) && reader.GetBoolean(6);
                    var isSystemTable = !reader.IsDBNull(7) && reader.GetBoolean(7);
                    var isPrimaryKey = !reader.IsDBNull(8) && reader.GetBoolean(8);
                    var isForeignKey = !reader.IsDBNull(9) && reader.GetBoolean(9);

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
                "SQL Server schema introspection was cancelled.");
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            var redactedMessage = DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets);
            _logger?.LogWarning("SQL Server schema introspection failed: {Reason}", redactedMessage);
            return OperationResult<DatabaseSchemaSnapshot>.Failure(
                ErrorCodes.IO_ERROR,
                $"SQL Server introspection failed: {redactedMessage}",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }

    private static OperationResult<string> ValidateRawConnectionString(
        string rawConnectionString,
        IReadOnlyList<string> secrets)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(rawConnectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                return OperationResult<string>.Failure(
                    ErrorCodes.VALIDATION_ERROR,
                    "SQL Server connection string must specify a Server / Data Source.");
            }

            return OperationResult<string>.Success(builder.ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid SQL Server connection string: {DatabaseIntrospectionHelper.RedactSecrets(ex.Message, secrets)}",
                DatabaseIntrospectionHelper.FormatRedactedDetails(ex, secrets));
        }
    }
}
