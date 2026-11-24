using Microsoft.EntityFrameworkCore;

using Mma.Cli.Shared.Data;
using Mma.Cli.Shared.Models;

using Newtonsoft.Json;

using Xunit;

namespace Mma.Cli.Shared.Tests;

public sealed class ProjectDumperTests : IDisposable
{
    private readonly string _tempRoot;

    public ProjectDumperTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-cli-dump-tests-{Guid.NewGuid():N}");
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
            // Best-effort cleanup of temp directory.
        }
    }

    [Fact]
    public void DumpProject_WritesValidProjectMmaJson_WithEntitiesRowsAndRelations()
    {
        var dbPath = Path.Combine(_tempRoot, "test.db");
        var solutionDir = Path.Combine(_tempRoot, "MySolution");
        Directory.CreateDirectory(solutionDir);

        var options = new DbContextOptionsBuilder<CliDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        int projectId;
        using (var ctx = new CliDbContext(options))
        {
            ctx.Database.EnsureCreated();

            var project = new ProjectModel { Name = "MySolution", Path = solutionDir };
            ctx.Projects.Add(project);
            ctx.SaveChanges();
            projectId = project.Id;

            var customer = new EntityModel
            {
                EntityName = "Customer",
                PkType = "Guid",
                HasApi = true,
                HasService = true,
                ProjectId = projectId,
                Rows =
                [
                    new EntityRowModel { ColumnName = "FullName", DataType = "string", Nullable = false }
                ]
            };

            var order = new EntityModel
            {
                EntityName = "Order",
                PkType = "long",
                HasApi = true,
                HasService = true,
                ProjectId = projectId,
                Rows =
                [
                    new EntityRowModel { ColumnName = "Total", DataType = "decimal", Nullable = false }
                ]
            };

            ctx.Entities.AddRange(customer, order);
            ctx.SaveChanges();

            ctx.Relations.Add(new RelationModel
            {
                Name = "Customer_Order_CustomerId",
                ParentId = customer.Id,
                ChildId = order.Id,
                Applied = false
            });
            ctx.SaveChanges();

            ctx.DumpProject(projectId, solutionDir);
        }

        var dumpPath = Path.Combine(solutionDir, ".mma", "project.mma");
        Assert.True(File.Exists(dumpPath));

        var dump = JsonConvert.DeserializeObject<ProjectDumpModel>(File.ReadAllText(dumpPath));
        Assert.NotNull(dump);
        Assert.NotNull(dump.Project);
        Assert.NotNull(dump.Entities);
        Assert.NotNull(dump.Rows);
        Assert.NotNull(dump.Relations);
        Assert.Equal("MySolution", dump.Project.Name);
        Assert.Equal(2, dump.Entities.Count);
        Assert.Equal(2, dump.Rows.Count);
        Assert.Single(dump.Relations);
    }
}
