using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Mma.Cli.Shared.Data;
using Mma.Cli.Shared.Models;

namespace Mma.Cli.Bff.Persistence;

public static class LegacySqliteMigrator
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    public static async Task InitializeAndMigrateAsync(
        CliDbContext targetContext,
        string? legacyDbPath = null,
        CancellationToken cancellationToken = default)
    {
        await InitLock.WaitAsync(cancellationToken);
        try
        {
            await targetContext.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureProjectColumnsAsync(targetContext, cancellationToken);

            if (string.IsNullOrWhiteSpace(legacyDbPath) || !File.Exists(legacyDbPath))
            {
                return;
            }

            var targetDbPath = ExtractDataSourcePath(targetContext.Database.GetConnectionString());
            if (!string.IsNullOrWhiteSpace(targetDbPath) &&
                string.Equals(Path.GetFullPath(targetDbPath), Path.GetFullPath(legacyDbPath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await MigrateLegacyProjectsAsync(targetContext, legacyDbPath, cancellationToken);
        }
        finally
        {
            InitLock.Release();
        }
    }

    private static async Task EnsureProjectColumnsAsync(CliDbContext context, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('Projects');";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingColumns.Add(reader.GetString(1));
                }
            }

            if (!existingColumns.Contains("SchemaJson"))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE Projects ADD COLUMN SchemaJson TEXT NULL;";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!existingColumns.Contains("UpdatedAtUtc"))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE Projects ADD COLUMN UpdatedAtUtc TEXT NULL;";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task MigrateLegacyProjectsAsync(
        CliDbContext targetContext,
        string legacyDbPath,
        CancellationToken cancellationToken)
    {
        var existingKeys = (await targetContext.Projects
            .AsNoTracking()
            .Select(p => new { p.Name, p.Path })
            .ToListAsync(cancellationToken))
            .Select(p => (p.Name, p.Path))
            .ToHashSet();

        var connString = new SqliteConnectionStringBuilder
        {
            DataSource = legacyDbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var legacyConn = new SqliteConnection(connString);
        await legacyConn.OpenAsync(cancellationToken);

        var legacyProjects = new List<(int Id, string Name, string Path, string? ProjectJson)>();
        await using (var cmd = legacyConn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Name, Path, ProjectJson FROM Projects;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                legacyProjects.Add((
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        foreach (var legacyProject in legacyProjects)
        {
            if (existingKeys.Contains((legacyProject.Name, legacyProject.Path)))
            {
                continue;
            }

            var newProject = new ProjectModel
            {
                Name = legacyProject.Name,
                Path = legacyProject.Path,
                ProjectJson = legacyProject.ProjectJson,
                UpdatedAtUtc = DateTime.UtcNow,
            };

            targetContext.Projects.Add(newProject);
            await targetContext.SaveChangesAsync(cancellationToken);

            var entityIdMap = new Dictionary<int, int>();

            await using (var entityCmd = legacyConn.CreateCommand())
            {
                entityCmd.CommandText =
                    "SELECT Id, EntityName, PkType, HasService, HasApi, Applied FROM Entities WHERE ProjectId = $pid;";
                entityCmd.Parameters.AddWithValue("$pid", legacyProject.Id);

                await using var reader = await entityCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var oldEntityId = reader.GetInt32(0);
                    var newEntity = new EntityModel
                    {
                        ProjectId = newProject.Id,
                        EntityName = reader.IsDBNull(1) ? "Entity" : reader.GetString(1),
                        PkType = reader.IsDBNull(2) ? "Guid" : reader.GetString(2),
                        HasService = !reader.IsDBNull(3) && reader.GetBoolean(3),
                        HasApi = !reader.IsDBNull(4) && reader.GetBoolean(4),
                        Applied = !reader.IsDBNull(5) && reader.GetBoolean(5),
                    };

                    targetContext.Entities.Add(newEntity);
                    await targetContext.SaveChangesAsync(cancellationToken);
                    entityIdMap[oldEntityId] = newEntity.Id;
                }
            }

            foreach (var (oldEntityId, newEntityId) in entityIdMap)
            {
                await using var rowCmd = legacyConn.CreateCommand();
                rowCmd.CommandText =
                    "SELECT ColumnName, DataType, Nullable, Applied, IsForeignKey FROM EntityRows WHERE EntityId = $eid;";
                rowCmd.Parameters.AddWithValue("$eid", oldEntityId);

                await using var reader = await rowCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    targetContext.EntityRows.Add(new EntityRowModel
                    {
                        EntityId = newEntityId,
                        ColumnName = reader.IsDBNull(0) ? null : reader.GetString(0),
                        DataType = reader.IsDBNull(1) ? null : reader.GetString(1),
                        Nullable = !reader.IsDBNull(2) && reader.GetBoolean(2),
                        Applied = !reader.IsDBNull(3) && reader.GetBoolean(3),
                        IsForeignKey = !reader.IsDBNull(4) && reader.GetBoolean(4),
                    });
                }
            }

            await targetContext.SaveChangesAsync(cancellationToken);

            await using (var relCmd = legacyConn.CreateCommand())
            {
                relCmd.CommandText = "SELECT Name, Applied, ParentId, ChildId FROM Relations;";
                await using var reader = await relCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.IsDBNull(2) || reader.IsDBNull(3))
                    {
                        continue;
                    }

                    var oldParentId = reader.GetInt32(2);
                    var oldChildId = reader.GetInt32(3);
                    if (!entityIdMap.TryGetValue(oldParentId, out var newParentId) ||
                        !entityIdMap.TryGetValue(oldChildId, out var newChildId))
                    {
                        continue;
                    }

                    targetContext.Relations.Add(new RelationModel
                    {
                        Name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                        Applied = !reader.IsDBNull(1) && reader.GetBoolean(1),
                        ParentId = newParentId,
                        ChildId = newChildId,
                    });
                }
            }

            await targetContext.SaveChangesAsync(cancellationToken);
            existingKeys.Add((legacyProject.Name, legacyProject.Path));
        }
    }

    private static string? ExtractDataSourcePath(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:"
                ? null
                : builder.DataSource;
        }
        catch
        {
            return null;
        }
    }
}
