using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Mma.Cli.Bff.Persistence;
using Mma.Cli.Shared.Data;

using Xunit;

namespace Mma.Cli.Bff.Tests;

public sealed class LegacySqliteMigratorTests : IDisposable
{
    private readonly string _tempRoot;

    public LegacySqliteMigratorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-legacy-migrate-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task InitializeAndMigrateAsync_MigratesLegacySchemaIdempotently()
    {
        var legacyDbPath = Path.Combine(_tempRoot, "legacy_mma_cli.db");
        var targetDbPath = Path.Combine(_tempRoot, "user_mma_cli.db");

        // Create a legacy SQLite DB without SchemaJson or UpdatedAtUtc columns.
        await using (var conn = new SqliteConnection($"Data Source={legacyDbPath}"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Projects (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Path TEXT NOT NULL,
                    ProjectJson TEXT NULL
                );
                CREATE TABLE Entities (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    EntityName TEXT NOT NULL,
                    PkType TEXT NOT NULL,
                    HasService INTEGER NOT NULL,
                    HasApi INTEGER NOT NULL DEFAULT 1,
                    ProjectId INTEGER NULL,
                    Applied INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE EntityRows (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ColumnName TEXT NULL,
                    DataType TEXT NULL,
                    Nullable INTEGER NOT NULL,
                    Applied INTEGER NOT NULL,
                    IsForeignKey INTEGER NOT NULL,
                    EntityId INTEGER NULL
                );
                CREATE TABLE Relations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Applied INTEGER NOT NULL,
                    ParentId INTEGER NULL,
                    ChildId INTEGER NULL
                );

                INSERT INTO Projects (Id, Name, Path) VALUES (1, 'LegacyApp', 'C:\work\LegacyApp');
                INSERT INTO Entities (Id, EntityName, PkType, HasService, HasApi, ProjectId, Applied)
                    VALUES (10, 'Customer', 'Guid', 1, 1, 1, 1),
                           (11, 'Order', 'long', 1, 1, 1, 0);
                INSERT INTO EntityRows (ColumnName, DataType, Nullable, Applied, IsForeignKey, EntityId)
                    VALUES ('Email', 'string', 0, 1, 0, 10),
                           ('CustomerId', 'Guid', 0, 0, 1, 11);
                INSERT INTO Relations (Name, Applied, ParentId, ChildId)
                    VALUES ('Customer_Order_CustomerId', 0, 10, 11);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<CliDbContext>()
            .UseSqlite(UserDataPathResolver.BuildConnectionString(targetDbPath))
            .Options;

        await using (var ctx = new CliDbContext(options))
        {
            await LegacySqliteMigrator.InitializeAndMigrateAsync(ctx, legacyDbPath);
            // Second run must be idempotent (no duplicate projects/entities/relations).
            await LegacySqliteMigrator.InitializeAndMigrateAsync(ctx, legacyDbPath);
        }

        await using (var verifyCtx = new CliDbContext(options))
        {
            var projects = await verifyCtx.Projects.ToListAsync();
            var project = Assert.Single(projects);
            Assert.Equal("LegacyApp", project.Name);
            Assert.Equal(@"C:\work\LegacyApp", project.Path);

            var entities = await verifyCtx.Entities
                .Include(e => e.Rows)
                .OrderBy(e => e.EntityName)
                .ToListAsync();
            Assert.Equal(2, entities.Count);
            Assert.Equal("Customer", entities[0].EntityName);
            Assert.True(entities[0].Applied);
            Assert.Single(entities[0].Rows);
            Assert.Equal("Order", entities[1].EntityName);
            Assert.False(entities[1].Applied);

            var relation = Assert.Single(await verifyCtx.Relations.ToListAsync());
            Assert.Equal("Customer_Order_CustomerId", relation.Name);
            Assert.Equal(entities[0].Id, relation.ParentId);
            Assert.Equal(entities[1].Id, relation.ChildId);
        }
    }
}
