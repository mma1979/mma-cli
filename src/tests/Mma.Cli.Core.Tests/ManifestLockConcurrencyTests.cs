using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Services;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Core.Templates;

namespace Mma.Cli.Core.Tests;

public sealed class ManifestLockConcurrencyTests : IDisposable
{
    private readonly string _tempRoot;

    public ManifestLockConcurrencyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AcquireAsync_WhenCrossProcessLockFileHeld_ThrowsTimeoutException()
    {
        var slnDir = Path.Combine(_tempRoot, "LockedApp");
        var mmaDir = Path.Combine(slnDir, ".mma");
        Directory.CreateDirectory(mmaDir);
        var lockFile = Path.Combine(mmaDir, ".lock");

        await using var externalLock = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var manager = new SolutionLockManager();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            manager.AcquireAsync(slnDir, timeout: TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task AcquireAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var slnDir = Path.Combine(_tempRoot, "CancelApp");
        Directory.CreateDirectory(slnDir);

        var manager = new SolutionLockManager();
        await using var held = await manager.AcquireAsync(slnDir, timeout: TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.AcquireAsync(slnDir, timeout: TimeSpan.FromSeconds(5), ct: cts.Token));
    }

    [Fact]
    public async Task UpdateAtomicAsync_EightParallelWriters_PreservesAllEightEntitiesWithoutCorruptionOrTempFiles()
    {
        var slnDir = Path.Combine(_tempRoot, "ConcurrentManifestApp");
        Directory.CreateDirectory(slnDir);
        File.WriteAllText(Path.Combine(slnDir, "ConcurrentManifestApp.sln"), string.Empty);

        var lockManager = new SolutionLockManager();
        var store = new ManifestStore(lockManager);

        var initialSave = await store.SaveAtomicAsync(slnDir, new ProjectManifest
        {
            SchemaVersion = 2,
            SolutionName = "ConcurrentManifestApp",
            Mapper = "AutoMapper",
            SolutionFormat = ".sln"
        });
        Assert.True(initialSave.IsSuccess);

        using var barrier = new Barrier(8);
        var tasks = Enumerable.Range(0, 8)
            .Select(i => Task.Run(async () =>
            {
                barrier.SignalAndWait();
                var entityName = $"Entity{i}";
                return await store.UpdateAtomicAsync(slnDir, manifest =>
                {
                    var updatedEntities = new List<ManifestEntity>(manifest.Entities)
                    {
                        new(entityName, "Guid", true, true, Applied: true, Properties: [])
                    };
                    return OperationResult<ProjectManifest>.Success(manifest with
                    {
                        Entities = updatedEntities
                    });
                });
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Message));

        var finalLoaded = await store.LoadAsync(slnDir);
        Assert.True(finalLoaded.IsSuccess);
        Assert.Equal(8, finalLoaded.Value!.Manifest.Entities.Count);

        for (var i = 0; i < 8; i++)
        {
            Assert.Contains(finalLoaded.Value.Manifest.Entities, e => e.Name == $"Entity{i}");
        }

        var mmaDir = Path.Combine(slnDir, ".mma");
        Assert.Empty(Directory.GetFiles(mmaDir, "*.tmp"));
    }

    [Fact]
    public async Task EntityService_EightParallelGenerations_NoDeadlockAndAllEntitiesTracked()
    {
        var lockManager = new SolutionLockManager();
        var locator = new SolutionLocator();
        var templates = new EmbeddedTemplateProvider();
        var store = new ManifestStore(lockManager, locator);
        var slnSvc = new SolutionService(lockManager: lockManager, manifestStore: store);

        Assert.True((await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "ParallelGenApp", MapperKind.Mapster))).IsSuccess);
        var slnDir = Path.Combine(_tempRoot, "ParallelGenApp");

        var entitySvc = new EntityService(locator, templates, lockManager: lockManager, manifestStore: store);

        using var barrier = new Barrier(8);
        var tasks = Enumerable.Range(0, 8)
            .Select(i => Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, $"Widget{i}", "Guid"));
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Message));

        var loaded = await store.LoadAsync(slnDir);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(8, loaded.Value!.Manifest.Entities.Count);

        var dbContext = await File.ReadAllTextAsync(
            Path.Combine(slnDir, "ParallelGenApp", "ParallelGenApp.EntityFramework", "ApplicationDbContext.cs"));
        for (var i = 0; i < 8; i++)
        {
            Assert.Contains(loaded.Value.Manifest.Entities, e => e.Name == $"Widget{i}");
            Assert.Contains($"DbSet<Widget{i}>", dbContext, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(slnDir, ".mma"), "*.tmp"));
    }
}
