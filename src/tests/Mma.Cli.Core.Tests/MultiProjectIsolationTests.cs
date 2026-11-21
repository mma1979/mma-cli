using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Database;

namespace Mma.Cli.Core.Tests;

public sealed class MultiProjectIsolationTests : IDisposable
{
    private readonly string _tempRoot;

    public MultiProjectIsolationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-db-{Guid.NewGuid():N}");
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

    [Fact]
    public void WorkspaceDbPathResolver_RespectsOverrideThenMmaDataDirThenLocalAppData()
    {
        var explicitPath = Path.Combine(_tempRoot, "custom", "my.db");
        Assert.Equal(Path.GetFullPath(explicitPath), WorkspaceDbPathResolver.Resolve(explicitPath));

        var previous = Environment.GetEnvironmentVariable("MMA_DATA_DIR");
        try
        {
            var envDir = Path.Combine(_tempRoot, "env-data");
            Environment.SetEnvironmentVariable("MMA_DATA_DIR", envDir);
            Assert.Equal(Path.Combine(envDir, "workspace.db"), WorkspaceDbPathResolver.Resolve());

            Environment.SetEnvironmentVariable("MMA_DATA_DIR", null);
            var defaultPath = WorkspaceDbPathResolver.Resolve();
            Assert.EndsWith(Path.Combine("mma-cli", "workspace.db"), defaultPath, StringComparison.Ordinal);
            Assert.DoesNotContain("wwwroot", defaultPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MMA_DATA_DIR", previous);
        }
    }

    [Fact]
    public async Task SqliteWorkspaceRegistry_StrictlyIsolatesIdenticalEntityNamesAcrossWorkspaces()
    {
        var dbPath = Path.Combine(_tempRoot, "nested", "workspace.db");
        var services = new ServiceCollection();
        services.AddMmaDatabase(dbPath);
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
        await registry.EnsureInitializedAsync();

        var pathA = Path.Combine(_tempRoot, "SolutionA");
        var pathB = Path.Combine(_tempRoot, "SolutionB");

        var wsA = await registry.RegisterAsync("SolutionA", pathA, "AutoMapper");
        await Task.Delay(20);
        var wsB = await registry.RegisterAsync("SolutionB", pathB, "Mapster");

        var list = await registry.ListAsync();
        Assert.Equal(2, list.Count);
        Assert.Equal(wsB.Id, list[0].Id);
        Assert.Equal(wsA.Id, list[1].Id);

        var manifestA = new ProjectManifest
        {
            SolutionName = "SolutionA",
            Mapper = "AutoMapper",
            SolutionFormat = ".sln",
            Entities =
            [
                new ManifestEntity("Order", "Guid", HasApi: true, Applied: true),
                new ManifestEntity("OnlyInA", "int", HasApi: false, Applied: false)
            ]
        };

        var manifestB = new ProjectManifest
        {
            SolutionName = "SolutionB",
            Mapper = "Mapster",
            SolutionFormat = ".slnx",
            Entities =
            [
                new ManifestEntity("Order", "long", HasApi: false, Applied: false),
                new ManifestEntity("OnlyInB", "Guid", HasApi: true, Applied: true)
            ]
        };

        var syncA = await registry.SyncFromManifestAsync(wsA.Id, manifestA);
        var syncB = await registry.SyncFromManifestAsync(wsB.Id, manifestB);
        Assert.True(syncA.IsSuccess);
        Assert.True(syncB.IsSuccess);

        var orderA = await registry.FindEntityAsync(wsA.Id, "Order");
        var orderB = await registry.FindEntityAsync(wsB.Id, "Order");
        Assert.NotNull(orderA);
        Assert.NotNull(orderB);
        Assert.Equal(wsA.Id, orderA.WorkspaceId);
        Assert.Equal("Guid", orderA.PkType);
        Assert.True(orderA.HasApi);
        Assert.True(orderA.Applied);

        Assert.Equal(wsB.Id, orderB.WorkspaceId);
        Assert.Equal("long", orderB.PkType);
        Assert.False(orderB.HasApi);
        Assert.False(orderB.Applied);

        Assert.Null(await registry.FindEntityAsync(wsA.Id, "OnlyInB"));
        Assert.Null(await registry.FindEntityAsync(wsB.Id, "OnlyInA"));

        // Removing Order from Workspace A must NOT affect Workspace B's Order
        var updatedManifestA = manifestA with { Entities = [new ManifestEntity("OnlyInA", "int")] };
        Assert.True((await registry.SyncFromManifestAsync(wsA.Id, updatedManifestA)).IsSuccess);
        Assert.Null(await registry.FindEntityAsync(wsA.Id, "Order"));
        Assert.NotNull(await registry.FindEntityAsync(wsB.Id, "Order"));

        // Re-registering existing canonical path upserts without creating duplicate
        var upsertedA = await registry.RegisterAsync("SolutionA_Renamed", pathA + Path.DirectorySeparatorChar, "Mapster");
        Assert.Equal(wsA.Id, upsertedA.Id);
        Assert.Equal("SolutionA_Renamed", upsertedA.Name);
        Assert.Equal("Mapster", upsertedA.Mapper);
        Assert.Equal(2, (await registry.ListAsync()).Count);

        // Removing Workspace A cascades to its entities only
        Assert.True(await registry.RemoveAsync(wsA.Id));
        Assert.False(await registry.RemoveAsync(wsA.Id));
        Assert.Null(await registry.GetByIdAsync(wsA.Id));
        Assert.Null(await registry.FindEntityAsync(wsA.Id, "OnlyInA"));

        var remainingB = await registry.GetByPathAsync(pathB);
        Assert.NotNull(remainingB);
        Assert.Equal(wsB.Id, remainingB.Id);
        Assert.NotNull(await registry.FindEntityAsync(wsB.Id, "Order"));
        Assert.NotNull(await registry.FindEntityAsync(wsB.Id, "OnlyInB"));
    }

    [Fact]
    public async Task WorkspaceDbContext_EnforcesUniqueSolutionPathAndWorkspaceEntityCompositeKey()
    {
        var dbPath = Path.Combine(_tempRoot, "constraints.db");
        var services = new ServiceCollection();
        services.AddMmaDatabase(dbPath);
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
        await registry.EnsureInitializedAsync();

        var db = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();
        Assert.Contains("Data Source=", db.Database.GetConnectionString(), StringComparison.OrdinalIgnoreCase);

        var ws = await registry.RegisterAsync("App", Path.Combine(_tempRoot, "App"), "AutoMapper");
        db.Entities.Add(new Database.Entities.WorkspaceEntityRecord { WorkspaceId = ws.Id, EntityName = "Dup", PkType = "Guid" });
        await db.SaveChangesAsync();

        db.Entities.Add(new Database.Entities.WorkspaceEntityRecord { WorkspaceId = ws.Id, EntityName = "Dup", PkType = "long" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SqliteWorkspaceRegistry_StoresUtcTimestampsAndHandlesConcurrentRegister()
    {
        var dbPath = Path.Combine(_tempRoot, "concurrent.db");
        var services = new ServiceCollection();
        services.AddMmaDatabase(dbPath);
        await using var provider = services.BuildServiceProvider();

        await using (var initScope = provider.CreateAsyncScope())
        {
            await initScope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>().EnsureInitializedAsync();
        }

        var slnPath = Path.Combine(_tempRoot, "SharedApp");
        using var barrier = new Barrier(4);
        var tasks = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
            barrier.SignalAndWait();
            return await registry.RegisterAsync($"SharedApp_{i}", slnPath, "AutoMapper");
        })).ToArray();

        var records = await Task.WhenAll(tasks);
        Assert.All(records, r =>
        {
            Assert.Equal(TimeSpan.Zero, r.CreatedAtUtc.Offset);
            Assert.Equal(TimeSpan.Zero, r.LastOpenedAtUtc.Offset);
        });

        await using var verifyScope = provider.CreateAsyncScope();
        var list = await verifyScope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>().ListAsync();
        Assert.Single(list);
    }

    [Fact]
    public async Task SqliteWorkspaceRegistry_SyncFromManifestAsync_UpdatesEntityNameOnCaseOnlyRename()
    {
        var dbPath = Path.Combine(_tempRoot, "case-rename.db");
        var services = new ServiceCollection();
        services.AddMmaDatabase(dbPath);
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
        await registry.EnsureInitializedAsync();

        var slnPath = Path.Combine(_tempRoot, "CaseApp");
        var ws = await registry.RegisterAsync("CaseApp", slnPath, "AutoMapper");

        var initialManifest = new ProjectManifest
        {
            SolutionName = "CaseApp",
            Mapper = "AutoMapper",
            SolutionFormat = ".sln",
            Entities = [new ManifestEntity("order", "Guid", HasApi: true, Applied: true)]
        };

        Assert.True((await registry.SyncFromManifestAsync(ws.Id, initialManifest)).IsSuccess);
        var beforeRename = await registry.FindEntityAsync(ws.Id, "order");
        Assert.NotNull(beforeRename);
        Assert.Equal("order", beforeRename.EntityName);

        var renamedManifest = initialManifest with
        {
            Entities = [new ManifestEntity("Order", "Guid", HasApi: true, Applied: true)]
        };

        var syncResult = await registry.SyncFromManifestAsync(ws.Id, renamedManifest);
        Assert.True(syncResult.IsSuccess);

        var afterRename = await registry.FindEntityAsync(ws.Id, "Order");
        Assert.NotNull(afterRename);
        Assert.Equal("Order", afterRename.EntityName);
        Assert.Null(await registry.FindEntityAsync(ws.Id, "order"));
    }
}
