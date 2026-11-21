using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.Core;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Database;
using Mma.Cli.Database.Introspection;

namespace Mma.Cli.Core.Tests;

public sealed class SqliteIntrospectorTests : IDisposable
{
    private readonly string _tempRoot;

    public SqliteIntrospectorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-sqlite-intro-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string CreateSampleDatabase(string fileName = "sample.sqlite", bool includeInvoiceToExcludedAppUser = false)
    {
        var dbPath = Path.Combine(_tempRoot, fileName);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false
        };

        using var conn = new SqliteConnection(builder.ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE __EFMigrationsHistory (
                MigrationId TEXT PRIMARY KEY,
                ProductVersion TEXT NOT NULL
            );

            CREATE TABLE AppUsers (
                Id TEXT PRIMARY KEY,
                UserName TEXT NOT NULL
            );

            CREATE TABLE Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description VARCHAR(500),
                CreatedBy TEXT,
                CreatedDate TEXT,
                ModifiedBy TEXT,
                ModifiedDate TEXT,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                DeletedBy TEXT,
                DeletedDate TEXT
            );

            CREATE TABLE Products (
                Id INTEGER PRIMARY KEY,
                CategoryId INTEGER NOT NULL REFERENCES Categories(Id) ON DELETE CASCADE,
                Title NVARCHAR(200) NOT NULL,
                Description TEXT,
                UnitPrice DECIMAL(18, 2) NOT NULL,
                IsActive BOOLEAN NOT NULL,
                CreatedBy TEXT,
                CreatedDate TEXT,
                ModifiedBy TEXT,
                ModifiedDate TEXT,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                DeletedBy TEXT,
                DeletedDate TEXT
            );

            CREATE TABLE Orders (
                Id TEXT PRIMARY KEY,
                OrderNumber VARCHAR(50) NOT NULL,
                OrderDate DATETIME NOT NULL,
                CreatedDate TEXT
            );

            CREATE TABLE order_items (
                Id INTEGER PRIMARY KEY,
                OrderId TEXT NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
                ProductId INTEGER NOT NULL REFERENCES Products(Id) ON DELETE RESTRICT,
                Quantity INTEGER NOT NULL,
                LineTotal NUMERIC(12, 2) NOT NULL
            );

            CREATE TABLE Addresses (
                Id INTEGER PRIMARY KEY,
                StreetLine1 NVARCHAR(150) NOT NULL,
                PostalCode VARCHAR(20)
            );
            """;
        cmd.ExecuteNonQuery();

        if (includeInvoiceToExcludedAppUser)
        {
            using var extraCmd = conn.CreateCommand();
            extraCmd.CommandText = """
                CREATE TABLE Invoices (
                    Id INTEGER PRIMARY KEY,
                    AppUserId TEXT NOT NULL REFERENCES AppUsers(Id),
                    InvoiceNumber VARCHAR(32) NOT NULL
                );
                """;
            extraCmd.ExecuteNonQuery();
        }

        return dbPath;
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccessForValidDb_AndIoErrorWithoutCreatingFileForMissingDb()
    {
        var dbPath = CreateSampleDatabase();
        var missingPath1 = Path.Combine(_tempRoot, "missing-by-param.sqlite");
        var missingPath2 = Path.Combine(_tempRoot, "missing-by-connstr.sqlite");
        var introspector = new SqliteIntrospector(new SchemaNamingService());

        var okByParam = await introspector.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            Parameters: new DatabaseConnectionParameters(dbPath)));
        Assert.True(okByParam.IsSuccess, okByParam.Error?.Message);

        var okByConnStr = await introspector.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            ConnectionString: $"Data Source={dbPath}"));
        Assert.True(okByConnStr.IsSuccess, okByConnStr.Error?.Message);

        var missingByParam = await introspector.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            Parameters: new DatabaseConnectionParameters(missingPath1)));
        Assert.False(missingByParam.IsSuccess);
        Assert.Equal(ErrorCodes.IO_ERROR, missingByParam.Error?.Code);
        Assert.False(File.Exists(missingPath1));

        var missingByConnStr = await introspector.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            ConnectionString: $"Data Source={missingPath2}"));
        Assert.False(missingByConnStr.IsSuccess);
        Assert.Equal(ErrorCodes.IO_ERROR, missingByConnStr.Error?.Code);
        Assert.False(File.Exists(missingPath2));

        var emptyReq = await introspector.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            Parameters: new DatabaseConnectionParameters("   ")));
        Assert.False(emptyReq.IsSuccess);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, emptyReq.Error?.Code);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenFileIsNotValidSqliteDatabase_ReturnsIoError()
    {
        var corruptPath = Path.Combine(_tempRoot, "not-a-db.sqlite");
        await File.WriteAllTextAsync(corruptPath, "this is plain text, not a sqlite database");
        var introspector = new SqliteIntrospector(new SchemaNamingService());

        var result = await introspector.TestConnectionAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            Parameters: new DatabaseConnectionParameters(corruptPath)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.IO_ERROR, result.Error?.Code);
    }

    [Fact]
    public async Task IntrospectAsync_MapsEntitiesColumnsForeignKeysAndRelations_AndExcludesDefaultNoise()
    {
        var dbPath = CreateSampleDatabase();
        var introspector = new SqliteIntrospector(new SchemaNamingService());

        var result = await introspector.IntrospectAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            Parameters: new DatabaseConnectionParameters(dbPath)));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var snapshot = result.Value!;
        Assert.Equal(DatabaseProviderKind.Sqlite, snapshot.Provider);

        var entityNames = snapshot.Tables.Select(t => t.EntityName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["Address", "Category", "Order", "OrderItem", "Product"], entityNames);

        Assert.DoesNotContain(snapshot.Tables, t => t.TableName.Equals("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.Tables, t => t.TableName.Equals("AppUsers", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.Tables, t => t.TableName.Equals("sqlite_sequence", StringComparison.OrdinalIgnoreCase));

        var product = Assert.Single(snapshot.Tables, t => t.EntityName == "Product");
        Assert.Equal("int", product.PkType);

        // Audit columns excluded
        foreach (var auditCol in SchemaFilterOptions.DefaultExcludedColumns)
        {
            Assert.DoesNotContain(product.Columns, c => c.RawName.Equals(auditCol, StringComparison.OrdinalIgnoreCase));
        }

        // PK and FK detection must be exact (no cross-column bleed)
        var prodId = Assert.Single(product.Columns, c => c.PropertyName == "Id");
        Assert.True(prodId.IsPrimaryKey);
        Assert.False(prodId.IsForeignKey);

        var prodCatId = Assert.Single(product.Columns, c => c.PropertyName == "CategoryId");
        Assert.False(prodCatId.IsPrimaryKey);
        Assert.True(prodCatId.IsForeignKey);
        Assert.False(prodCatId.IsNullable);
        Assert.Equal("int", prodCatId.ClrType);

        var prodTitle = Assert.Single(product.Columns, c => c.PropertyName == "Title");
        Assert.False(prodTitle.IsPrimaryKey);
        Assert.False(prodTitle.IsForeignKey);
        Assert.False(prodTitle.IsNullable);
        Assert.Equal("string", prodTitle.ClrType);
        Assert.Equal(200, prodTitle.MaxLength);

        var prodDesc = Assert.Single(product.Columns, c => c.PropertyName == "Description");
        Assert.False(prodDesc.IsForeignKey);
        Assert.True(prodDesc.IsNullable);
        Assert.Equal("string", prodDesc.ClrType);

        var prodPrice = Assert.Single(product.Columns, c => c.PropertyName == "UnitPrice");
        Assert.False(prodPrice.IsForeignKey);
        Assert.False(prodPrice.IsNullable);
        Assert.Equal("decimal", prodPrice.ClrType);

        var prodActive = Assert.Single(product.Columns, c => c.PropertyName == "IsActive");
        Assert.False(prodActive.IsForeignKey);
        Assert.False(prodActive.IsNullable);
        Assert.Equal("bool", prodActive.ClrType);

        var order = Assert.Single(snapshot.Tables, t => t.EntityName == "Order");
        Assert.Equal("string", order.PkType);

        var orderItem = Assert.Single(snapshot.Tables, t => t.EntityName == "OrderItem");
        Assert.Equal("int", orderItem.PkType);
        var oiOrderId = Assert.Single(orderItem.Columns, c => c.PropertyName == "OrderId");
        Assert.True(oiOrderId.IsForeignKey);
        Assert.Equal("string", oiOrderId.ClrType);
        var oiProductId = Assert.Single(orderItem.Columns, c => c.PropertyName == "ProductId");
        Assert.True(oiProductId.IsForeignKey);
        Assert.Equal("int", oiProductId.ClrType);

        // Relations
        Assert.Equal(3, snapshot.Relations.Count);
        Assert.Contains(snapshot.Relations, r =>
            r.ParentEntity == "Category"
            && r.ChildEntity == "Product"
            && r.ForeignKey == "CategoryId"
            && r.ForeignKeyType == "int"
            && r.DeleteBehavior == "Cascade");
        Assert.Contains(snapshot.Relations, r =>
            r.ParentEntity == "Order"
            && r.ChildEntity == "OrderItem"
            && r.ForeignKey == "OrderId"
            && r.ForeignKeyType == "string"
            && r.DeleteBehavior == "Cascade");
        Assert.Contains(snapshot.Relations, r =>
            r.ParentEntity == "Product"
            && r.ChildEntity == "OrderItem"
            && r.ForeignKey == "ProductId"
            && r.ForeignKeyType == "int"
            && r.DeleteBehavior == "Restrict");
    }

    [Fact]
    public async Task IntrospectAsync_AppliesCustomWildcardFiltersColumnExclusionsAndOverrides()
    {
        var dbPath = CreateSampleDatabase("custom-filters.sqlite");
        var introspector = new SqliteIntrospector(new SchemaNamingService());

        var filters = new SchemaFilterOptions
        {
            ExcludedTables = ["__*", "order_*", "AppUsers", "sqlite_sequence"],
            ExcludedColumns = ["Description"],
            TableNameOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["addresses"] = "PostalAddress"
            }
        };

        var result = await introspector.IntrospectAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            Parameters: new DatabaseConnectionParameters(dbPath),
            Filters: filters));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var snapshot = result.Value!;

        var entityNames = snapshot.Tables.Select(t => t.EntityName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["Category", "Order", "PostalAddress", "Product"], entityNames);

        // Custom ExcludedColumns overrides default audit exclusion, while excluding Description
        var product = Assert.Single(snapshot.Tables, t => t.EntityName == "Product");
        Assert.DoesNotContain(product.Columns, c => c.PropertyName == "Description");
        Assert.Contains(product.Columns, c => c.PropertyName == "CreatedBy");

        // Relations referencing excluded order_items must be dropped
        var rel = Assert.Single(snapshot.Relations);
        Assert.Equal("Category", rel.ParentEntity);
        Assert.Equal("Product", rel.ChildEntity);
    }

    [Fact]
    public async Task IntrospectAsync_ExcludesRelationsPointingToExcludedParentTables()
    {
        var dbPath = CreateSampleDatabase("excluded-parent.sqlite", includeInvoiceToExcludedAppUser: true);
        var introspector = new SqliteIntrospector(new SchemaNamingService());

        var result = await introspector.IntrospectAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            Parameters: new DatabaseConnectionParameters(dbPath)));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var invoice = Assert.Single(result.Value!.Tables, t => t.EntityName == "Invoice");
        var fkCol = Assert.Single(invoice.Columns, c => c.PropertyName == "AppUserId");
        Assert.True(fkCol.IsForeignKey);

        Assert.DoesNotContain(result.Value.Relations, r =>
            r.ParentEntity == "AppUser" || r.ChildEntity == "Invoice");
    }

    [Fact]
    public async Task ToProjectManifest_ProducesValidV2Manifest_AndRoundTripsWithManifestStore()
    {
        var dbPath = CreateSampleDatabase("manifest-roundtrip.sqlite");
        var introspector = new SqliteIntrospector(new SchemaNamingService());

        var introspected = await introspector.IntrospectAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            Parameters: new DatabaseConnectionParameters(dbPath)));
        Assert.True(introspected.IsSuccess, introspected.Error?.Message);

        var manifest = introspected.Value!.ToProjectManifest("SampleApi", MapperKind.AutoMapper, ".slnx");
        Assert.Equal(2, manifest.SchemaVersion);
        Assert.Equal("SampleApi", manifest.SolutionName);
        Assert.Equal("AutoMapper", manifest.Mapper);
        Assert.Equal(".slnx", manifest.SolutionFormat);
        Assert.Equal(5, manifest.Entities.Count);
        Assert.Equal(3, manifest.Relations.Count);
        Assert.All(manifest.Entities, e => Assert.False(e.Applied));
        Assert.All(manifest.Relations, r => Assert.False(r.Applied));

        var productEntity = Assert.Single(manifest.Entities, e => e.Name == "Product");
        Assert.DoesNotContain(productEntity.Properties, p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(productEntity.Properties, p => p.Name == "CategoryId" && p.IsForeignKey && !p.Applied);
        Assert.Contains(productEntity.Properties, p => p.Name == "Title" && !p.IsForeignKey && p.MaxLength == 200);

        var slnDir = Path.Combine(_tempRoot, "SampleApiSln");
        Directory.CreateDirectory(slnDir);
        await File.WriteAllTextAsync(Path.Combine(slnDir, "SampleApi.slnx"), "<Solution />");
        var coreDir = Path.Combine(slnDir, "SampleApi", "SampleApi.Core");
        Directory.CreateDirectory(coreDir);
        await File.WriteAllTextAsync(Path.Combine(coreDir, "MappingProfile.cs"), string.Empty);

        var store = new ManifestStore();
        var saved = await store.SaveAtomicAsync(slnDir, manifest);
        Assert.True(saved.IsSuccess, saved.Error?.Message);

        var reloaded = await store.LoadAsync(slnDir);
        Assert.True(reloaded.IsSuccess, reloaded.Error?.Message);
        Assert.Equal(5, reloaded.Value!.Manifest.Entities.Count);
        Assert.Equal(3, reloaded.Value.Manifest.Relations.Count);

        var validator = new ProjectValidator(new Core.Solutions.SolutionLocator(), store);
        var validation = await validator.ValidateAsync(slnDir);
        Assert.True(validation.IsSuccess, validation.Error?.Message);
        Assert.True(validation.Value!.IsValid);
        Assert.DoesNotContain(validation.Value.Issues, i => i.Severity == "Error");
    }

    [Fact]
    public async Task AddMmaCoreAndDatabase_ResolvesAllFourProvidersViaFactory()
    {
        var registryDb = Path.Combine(_tempRoot, "workspace.db");
        var services = new ServiceCollection();
        services.AddMmaCore();
        services.AddMmaDatabase(registryDb);

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDatabaseIntrospectorFactory>();

        Assert.IsType<SqliteIntrospector>(factory.GetIntrospector(DatabaseProviderKind.Sqlite));
        Assert.IsType<SqlServerIntrospector>(factory.GetIntrospector(DatabaseProviderKind.SqlServer));
        Assert.IsType<PostgresIntrospector>(factory.GetIntrospector(DatabaseProviderKind.PostgreSql));
        Assert.IsType<MySqlIntrospector>(factory.GetIntrospector(DatabaseProviderKind.MySql));
        Assert.Throws<NotSupportedException>(() => factory.GetIntrospector((DatabaseProviderKind)999));
    }

    [Fact]
    public async Task IntrospectAsync_WhenCancelled_ReturnsCancelledFailure()
    {
        var dbPath = CreateSampleDatabase("cancel.sqlite");
        var introspector = new SqliteIntrospector(new SchemaNamingService());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await introspector.IntrospectAsync(
            new DatabaseIntrospectRequest(
                DatabaseProviderKind.Sqlite,
                Parameters: new DatabaseConnectionParameters(dbPath)),
            cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CANCELLED, result.Error?.Code);
    }

    [Fact]
    public async Task IntrospectAsync_HandlesSelfReferencingForeignKeysAndCompositePrimaryKeys()
    {
        var dbPath = Path.Combine(_tempRoot, "self-ref-composite.sqlite");
        var connBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false
        };

        await using (var conn = new SqliteConnection(connBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                PRAGMA foreign_keys = ON;

                CREATE TABLE Categories (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    ParentCategoryId INTEGER NULL REFERENCES Categories(Id) ON DELETE SET NULL
                );

                CREATE TABLE Orders (
                    Id TEXT PRIMARY KEY,
                    OrderNumber VARCHAR(30) NOT NULL
                );

                CREATE TABLE Tags (
                    Id INTEGER PRIMARY KEY,
                    Label VARCHAR(50) NOT NULL
                );

                CREATE TABLE OrderTags (
                    OrderId TEXT NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
                    TagId INTEGER NOT NULL REFERENCES Tags(Id) ON DELETE CASCADE,
                    PRIMARY KEY (OrderId, TagId)
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var introspector = new SqliteIntrospector(new SchemaNamingService());
        var result = await introspector.IntrospectAsync(new DatabaseIntrospectRequest(
            DatabaseProviderKind.Sqlite,
            Parameters: new DatabaseConnectionParameters(dbPath)));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var snapshot = result.Value!;

        var category = Assert.Single(snapshot.Tables, t => t.EntityName == "Category");
        var parentCol = Assert.Single(category.Columns, c => c.PropertyName == "ParentCategoryId");
        Assert.True(parentCol.IsForeignKey);
        Assert.False(parentCol.IsPrimaryKey);
        Assert.True(parentCol.IsNullable);
        Assert.Equal("int", parentCol.ClrType);

        Assert.Contains(snapshot.Relations, r =>
            r.ParentEntity == "Category"
            && r.ChildEntity == "Category"
            && r.ForeignKey == "ParentCategoryId"
            && r.ForeignKeyType == "int"
            && r.DeleteBehavior == "SetNull");

        var orderTag = Assert.Single(snapshot.Tables, t => t.EntityName == "OrderTag");
        var orderIdCol = Assert.Single(orderTag.Columns, c => c.PropertyName == "OrderId");
        var tagIdCol = Assert.Single(orderTag.Columns, c => c.PropertyName == "TagId");
        Assert.True(orderIdCol.IsPrimaryKey);
        Assert.True(orderIdCol.IsForeignKey);
        Assert.False(orderIdCol.IsNullable);
        Assert.Equal("string", orderIdCol.ClrType);

        Assert.True(tagIdCol.IsPrimaryKey);
        Assert.True(tagIdCol.IsForeignKey);
        Assert.False(tagIdCol.IsNullable);
        Assert.Equal("int", tagIdCol.ClrType);

        Assert.Contains(snapshot.Relations, r =>
            r.ParentEntity == "Order"
            && r.ChildEntity == "OrderTag"
            && r.ForeignKey == "OrderId"
            && r.DeleteBehavior == "Cascade");
        Assert.Contains(snapshot.Relations, r =>
            r.ParentEntity == "Tag"
            && r.ChildEntity == "OrderTag"
            && r.ForeignKey == "TagId"
            && r.DeleteBehavior == "Cascade");

        var manifest = snapshot.ToProjectManifest("CompositeApi");
        var orderTagEntity = Assert.Single(manifest.Entities, e => e.Name == "OrderTag");
        Assert.Contains(orderTagEntity.Properties, p => p.Name == "OrderId" && p.IsForeignKey && !p.Nullable);
        Assert.Contains(orderTagEntity.Properties, p => p.Name == "TagId" && p.IsForeignKey && !p.Nullable);
        Assert.Contains(manifest.Relations, r =>
            r.ParentEntity == "Category"
            && r.ChildEntity == "Category"
            && r.ForeignKey == "ParentCategoryId");
    }
}
