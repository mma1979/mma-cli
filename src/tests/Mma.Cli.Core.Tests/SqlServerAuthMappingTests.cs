using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Results;
using Mma.Cli.Database.Introspection;
using MySqlConnector;
using Npgsql;

namespace Mma.Cli.Core.Tests;

public sealed class SqlServerAuthMappingTests
{
    private readonly SchemaNamingService _naming = new();

    [Fact]
    public void SqlServerBuildConnectionString_WindowsIntegrated_SetsIntegratedSecurityAndDefaultTlsFalse()
    {
        var introspector = new SqlServerIntrospector(_naming);

        var result = introspector.BuildConnectionString(new DatabaseConnectionParameters(
            ServerOrPath: @"localhost\SQLEXPRESS",
            Database: "AppDb",
            SqlServerAuth: SqlServerAuthKind.WindowsIntegrated));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var parsed = new SqlConnectionStringBuilder(result.Value!);
        Assert.Equal(@"localhost\SQLEXPRESS", parsed.DataSource);
        Assert.Equal("AppDb", parsed.InitialCatalog);
        Assert.True(parsed.IntegratedSecurity);
        Assert.NotEqual(SqlAuthenticationMethod.ActiveDirectoryIntegrated, parsed.Authentication);
        Assert.False(parsed.TrustServerCertificate);
    }

    [Fact]
    public void SqlServerBuildConnectionString_SqlPassword_SetsSqlPasswordCredentialsAndDefaultTlsFalse()
    {
        var introspector = new SqlServerIntrospector(_naming);

        var result = introspector.BuildConnectionString(new DatabaseConnectionParameters(
            ServerOrPath: "db.internal",
            Database: "SalesDb",
            Username: "sa_reader",
            Password: "MySecretPassword!42",
            Port: 14330,
            SqlServerAuth: SqlServerAuthKind.SqlPassword));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var parsed = new SqlConnectionStringBuilder(result.Value!);
        Assert.Equal("db.internal,14330", parsed.DataSource);
        Assert.Equal("SalesDb", parsed.InitialCatalog);
        Assert.Equal(SqlAuthenticationMethod.SqlPassword, parsed.Authentication);
        Assert.Equal("sa_reader", parsed.UserID);
        Assert.Equal("MySecretPassword!42", parsed.Password);
        Assert.False(parsed.IntegratedSecurity);
        Assert.False(parsed.TrustServerCertificate);
    }

    [Fact]
    public void SqlServerBuildConnectionString_ActiveDirectoryAndExplicitTrustServerCertificate_MapCorrectly()
    {
        var introspector = new SqlServerIntrospector(_naming);

        var result = introspector.BuildConnectionString(new DatabaseConnectionParameters(
            ServerOrPath: "azuresql.database.windows.net",
            Database: "ProdDb",
            SqlServerAuth: SqlServerAuthKind.ActiveDirectoryIntegrated,
            TrustServerCertificate: true));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var parsed = new SqlConnectionStringBuilder(result.Value!);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryIntegrated, parsed.Authentication);
        Assert.True(parsed.TrustServerCertificate);
    }

    [Theory]
    [InlineData("", "AppDb", SqlServerAuthKind.WindowsIntegrated, null, null)]
    [InlineData("localhost", "", SqlServerAuthKind.WindowsIntegrated, "u", "SuperSecret!999")]
    [InlineData("localhost", "AppDb", SqlServerAuthKind.SqlPassword, "", "SuperSecret!999")]
    [InlineData("localhost", "AppDb", SqlServerAuthKind.SqlPassword, "sa", "")]
    public void SqlServerBuildConnectionString_MissingRequiredFields_ReturnsValidationErrorWithoutLeakingPassword(
        string server,
        string database,
        SqlServerAuthKind auth,
        string? username,
        string? password)
    {
        var introspector = new SqlServerIntrospector(_naming);

        var result = introspector.BuildConnectionString(new DatabaseConnectionParameters(
            ServerOrPath: server,
            Database: database,
            Username: username,
            Password: password,
            SqlServerAuth: auth));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, result.Error?.Code);
        if (!string.IsNullOrEmpty(password))
        {
            Assert.DoesNotContain(password, result.Error?.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(password, result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SqlServerColumnsSqlQuery_JoinsSysForeignKeyColumnsOnParentColumnId()
    {
        Assert.Contains("sys.foreign_key_columns", SqlServerIntrospector.ColumnsSqlQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fkc.parent_column_id = col.column_id", SqlServerIntrospector.ColumnsSqlQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_primary_key", SqlServerIntrospector.ColumnsSqlQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgresBuildConnectionString_ValidAndInvalidParameters_BehavesSafely()
    {
        var introspector = new PostgresIntrospector(_naming);

        var ok = introspector.BuildConnectionString(new DatabaseConnectionParameters(
            ServerOrPath: "pg.internal",
            Database: "appdb",
            Username: "pguser",
            Password: "PgSecret!Password"));
        Assert.True(ok.IsSuccess, ok.Error?.Message);
        var parsed = new NpgsqlConnectionStringBuilder(ok.Value!);
        Assert.Equal("pg.internal", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("appdb", parsed.Database);
        Assert.Equal("pguser", parsed.Username);
        Assert.Equal("PgSecret!Password", parsed.Password);

        var bad = introspector.BuildConnectionString(new DatabaseConnectionParameters(
            ServerOrPath: "",
            Database: "appdb",
            Password: "PgSecret!Password"));
        Assert.False(bad.IsSuccess);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, bad.Error?.Code);
        Assert.DoesNotContain("PgSecret!Password", bad.Error?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("PgSecret!Password", bad.Error?.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void MySqlBuildConnectionString_ValidAndInvalidParameters_BehavesSafely()
    {
        var introspector = new MySqlIntrospector(_naming);

        var ok = introspector.BuildConnectionString(new DatabaseConnectionParameters(
            ServerOrPath: "mysql.internal",
            Database: "shopdb",
            Username: "myuser",
            Password: "MySqlSecret!Password"));
        Assert.True(ok.IsSuccess, ok.Error?.Message);
        var parsed = new MySqlConnectionStringBuilder(ok.Value!);
        Assert.Equal("mysql.internal", parsed.Server);
        Assert.Equal(3306u, parsed.Port);
        Assert.Equal("shopdb", parsed.Database);
        Assert.Equal("myuser", parsed.UserID);
        Assert.Equal("MySqlSecret!Password", parsed.Password);
        Assert.False(parsed.AllowUserVariables);

        var bad = introspector.BuildConnectionString(new DatabaseConnectionParameters(
            ServerOrPath: "mysql.internal",
            Database: "",
            Password: "MySqlSecret!Password"));
        Assert.False(bad.IsSuccess);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, bad.Error?.Code);
        Assert.DoesNotContain("MySqlSecret!Password", bad.Error?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("MySqlSecret!Password", bad.Error?.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidConnectionString_NeverLeaksPasswordInError()
    {
        const string secret = "UltraSecretP@ssw0rd!";
        var sqlServer = new SqlServerIntrospector(_naming);
        var res = await sqlServer.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.SqlServer,
            ConnectionString: $"Server=tcp:invalid;Password={secret};UnknownKeyword=BrokenValue"));

        Assert.False(res.IsSuccess);
        Assert.DoesNotContain(secret, res.Error?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, res.Error?.Details ?? string.Empty, StringComparison.Ordinal);

        var postgres = new PostgresIntrospector(_naming);
        var pgRes = await postgres.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.PostgreSql,
            ConnectionString: $"Host=invalid;Password={secret};UnknownKeyword=BrokenValue"));
        Assert.False(pgRes.IsSuccess);
        Assert.DoesNotContain(secret, pgRes.Error?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, pgRes.Error?.Details ?? string.Empty, StringComparison.Ordinal);

        var mysql = new MySqlIntrospector(_naming);
        var myRes = await mysql.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.MySql,
            ConnectionString: $"Server=invalid;Password={secret};UnknownKeyword=BrokenValue"));
        Assert.False(myRes.IsSuccess);
        Assert.DoesNotContain(secret, myRes.Error?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, myRes.Error?.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseConnectionParameters_DefaultsToWindowsIntegratedAndTrustServerCertificateFalse()
    {
        var defaults = new DatabaseConnectionParameters("localhost");
        Assert.Equal(SqlServerAuthKind.WindowsIntegrated, defaults.SqlServerAuth);
        Assert.False(defaults.TrustServerCertificate);
    }

    [Theory]
    [InlineData("Categories", "Category")]
    [InlineData("Entities", "Entity")]
    [InlineData("Companies", "Company")]
    [InlineData("Addresses", "Address")]
    [InlineData("Statuses", "Status")]
    [InlineData("Boxes", "Box")]
    [InlineData("Branches", "Branch")]
    [InlineData("Buses", "Bus")]
    [InlineData("Analyses", "Analysis")]
    [InlineData("order_items", "OrderItem")]
    [InlineData("People", "Person")]
    [InlineData("Children", "Child")]
    [InlineData("Men", "Man")]
    [InlineData("Women", "Woman")]
    [InlineData("Access", "Access")]
    [InlineData("Address", "Address")]
    [InlineData("Status", "Status")]
    [InlineData("Analysis", "Analysis")]
    [InlineData("Orders", "Order")]
    [InlineData("Databases", "Database")]
    [InlineData("Courses", "Course")]
    [InlineData("123_orders", "_123Order")]
    public void SchemaNamingService_ToEntityName_SingularizesAndNormalizesCleanly(string rawTable, string expectedEntity)
    {
        Assert.Equal(expectedEntity, _naming.ToEntityName(rawTable));
    }

    [Fact]
    public void SchemaNamingService_ToEntityName_RespectsOverridesCaseInsensitively()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tbl_legacy_cust"] = "Customer"
        };

        Assert.Equal("Customer", _naming.ToEntityName("TBL_LEGACY_CUST", overrides));
    }

    [Theory]
    [InlineData("first_name", "FirstName")]
    [InlineData("category_id", "CategoryId")]
    [InlineData("Id", "Id")]
    [InlineData("CategoryId", "CategoryId")]
    [InlineData("OrderDate", "OrderDate")]
    [InlineData("unitPrice", "UnitPrice")]
    [InlineData("FIRST_NAME", "FirstName")]
    [InlineData("class", "Class")]
    public void SchemaNamingService_ToPropertyName_NormalizesIdentifiers(string rawColumn, string expectedProperty)
    {
        Assert.Equal(expectedProperty, _naming.ToPropertyName(rawColumn));
    }

    [Theory]
    [InlineData(DatabaseProviderKind.SqlServer, "uniqueidentifier", "Guid")]
    [InlineData(DatabaseProviderKind.PostgreSql, "uuid", "Guid")]
    [InlineData(DatabaseProviderKind.SqlServer, "int", "int")]
    [InlineData(DatabaseProviderKind.PostgreSql, "int4", "int")]
    [InlineData(DatabaseProviderKind.PostgreSql, "smallint", "int")]
    [InlineData(DatabaseProviderKind.MySql, "mediumint", "int")]
    [InlineData(DatabaseProviderKind.MySql, "int unsigned", "int")]
    [InlineData(DatabaseProviderKind.MySql, "tinyint(1)", "bool")]
    [InlineData(DatabaseProviderKind.MySql, "tinyint(4)", "int")]
    [InlineData(DatabaseProviderKind.SqlServer, "bigint", "long")]
    [InlineData(DatabaseProviderKind.PostgreSql, "int8", "long")]
    [InlineData(DatabaseProviderKind.MySql, "bigint unsigned", "long")]
    [InlineData(DatabaseProviderKind.SqlServer, "bit", "bool")]
    [InlineData(DatabaseProviderKind.PostgreSql, "boolean", "bool")]
    [InlineData(DatabaseProviderKind.Sqlite, "bool", "bool")]
    [InlineData(DatabaseProviderKind.SqlServer, "decimal(18, 2)", "decimal")]
    [InlineData(DatabaseProviderKind.PostgreSql, "numeric(12,4)", "decimal")]
    [InlineData(DatabaseProviderKind.SqlServer, "money", "decimal")]
    [InlineData(DatabaseProviderKind.PostgreSql, "double precision", "decimal")]
    [InlineData(DatabaseProviderKind.PostgreSql, "float8", "decimal")]
    [InlineData(DatabaseProviderKind.SqlServer, "real", "float")]
    [InlineData(DatabaseProviderKind.PostgreSql, "float4", "float")]
    [InlineData(DatabaseProviderKind.Sqlite, "float", "float")]
    [InlineData(DatabaseProviderKind.SqlServer, "datetime2(7)", "DateTime")]
    [InlineData(DatabaseProviderKind.SqlServer, "datetimeoffset", "DateTime")]
    [InlineData(DatabaseProviderKind.PostgreSql, "timestamp with time zone", "DateTime")]
    [InlineData(DatabaseProviderKind.PostgreSql, "timestamp(6) with time zone", "DateTime")]
    [InlineData(DatabaseProviderKind.MySql, "date", "DateTime")]
    [InlineData(DatabaseProviderKind.SqlServer, "time", "TimeSpan")]
    [InlineData(DatabaseProviderKind.PostgreSql, "interval", "TimeSpan")]
    [InlineData(DatabaseProviderKind.SqlServer, "varbinary(max)", "byte[]")]
    [InlineData(DatabaseProviderKind.PostgreSql, "bytea", "byte[]")]
    [InlineData(DatabaseProviderKind.Sqlite, "blob", "byte[]")]
    [InlineData(DatabaseProviderKind.MySql, "longblob", "byte[]")]
    [InlineData(DatabaseProviderKind.SqlServer, "nvarchar(255)", "string")]
    [InlineData(DatabaseProviderKind.PostgreSql, "character varying(100)", "string")]
    [InlineData(DatabaseProviderKind.PostgreSql, "jsonb", "string")]
    [InlineData(DatabaseProviderKind.Sqlite, "custom_unknown_type", "string")]
    public void SchemaNamingService_MapSqlType_MapsAllProvidersToSupportedDataTypes(
        DatabaseProviderKind provider,
        string rawSqlType,
        string expectedClrType)
    {
        var mapped = _naming.MapSqlType(provider, rawSqlType);
        Assert.Equal(expectedClrType, mapped);
        Assert.True(DataTypes.IsSupported(mapped), $"Mapped type '{mapped}' is not in DataTypes.All.");
    }

    [Theory]
    [InlineData("__EFMigrationsHistory", new[] { "__*" }, true)]
    [InlineData("sqlite_stat1", new[] { "sqlite_stat*" }, true)]
    [InlineData("ORDER_ITEMS", new[] { "order_*" }, true)]
    [InlineData("Order", new[] { "Orde?" }, true)]
    [InlineData("Orders", new[] { "Orde?" }, false)]
    [InlineData("Products", new[] { "__*", "AppUsers" }, false)]
    public void SchemaNamingService_IsExcluded_SupportsCaseInsensitiveGlobs(
        string candidate,
        string[] patterns,
        bool expected)
    {
        Assert.Equal(expected, _naming.IsExcluded(candidate, patterns));
    }

    [Fact]
    public async Task Introspectors_WhenConnectionFails_NeverPassRawExceptionOrPasswordToLogger()
    {
        const string secret = "LoggerSecret!987";
        var logger = new RecordingLogger<PostgresIntrospector>();
        var postgres = new PostgresIntrospector(_naming, logger);

        // Unreachable local closed port + short timeout fails via DbException
        var result = await postgres.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.PostgreSql,
            ConnectionString: $"Host=127.0.0.1;Port=1;Database=nope;Username=u;Password={secret};Timeout=1"));

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(secret, result.Error?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
        });
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, exception, formatter(state, exception)));
        }
    }
}
