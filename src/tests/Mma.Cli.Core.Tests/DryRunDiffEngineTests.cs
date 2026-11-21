using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.Core.CodeGen;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Services;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Core.Templates;

namespace Mma.Cli.Core.Tests;

public sealed class DryRunDiffEngineTests : IDisposable
{
    private readonly string _tempRoot;

    public DryRunDiffEngineTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-diff-engine-{Guid.NewGuid():N}");
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
    public async Task ComputePlanAsync_CreateModifyDelete_GeneratesUnifiedDiffsDeterministicPlanHashAndLeavesDiskUntouched()
    {
        var slnDir = Path.Combine(_tempRoot, "DiffSample");
        Directory.CreateDirectory(slnDir);

        var modifyPath = Path.Combine(slnDir, "Src", "Existing.cs");
        var deletePath = Path.Combine(slnDir, "Src", "Obsolete.cs");
        var createPath = Path.Combine(slnDir, "Src", "Nested", "NewFile.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(modifyPath)!);

        var originalModify = string.Join('\n',
            "line 1",
            "line 2",
            "line 3",
            "line 4",
            "line 5 old",
            "line 6",
            "line 7",
            "line 8",
            "line 9");
        var updatedModify = string.Join('\n',
            "line 1",
            "line 2",
            "line 3",
            "line 4",
            "line 5 new",
            "line 6",
            "line 7",
            "line 8",
            "line 9");
        var originalDelete = "public class Obsolete {}\n";
        var newCreate = "public class NewFile {}\n";

        await File.WriteAllTextAsync(modifyPath, originalModify, new UTF8Encoding(false));
        await File.WriteAllTextAsync(deletePath, originalDelete, new UTF8Encoding(false));

        var engine = new DryRunDiffEngine();
        var changes = new[]
        {
            new StagedFileChange(createPath, FileChangeKind.Create, newCreate),
            new StagedFileChange(modifyPath, FileChangeKind.Modify, updatedModify),
            new StagedFileChange(deletePath, FileChangeKind.Delete)
        };

        var planResult = await engine.ComputePlanAsync(slnDir, changes);
        Assert.True(planResult.IsSuccess, planResult.Error?.Message);
        var plan = planResult.Value!;

        Assert.StartsWith("sha256:", plan.PlanHash, StringComparison.Ordinal);
        Assert.Equal(3, plan.FileChanges.Count);

        // Verify disk was NOT mutated
        Assert.False(File.Exists(createPath));
        Assert.Equal(originalModify, await File.ReadAllTextAsync(modifyPath));
        Assert.True(File.Exists(deletePath));

        var createDiff = Assert.Single(plan.FileChanges, c => c.Kind == FileChangeKind.Create);
        Assert.Equal("Src/Nested/NewFile.cs", createDiff.RelativePath);
        Assert.Null(createDiff.OriginalHash);
        Assert.StartsWith("sha256:", createDiff.NewHash, StringComparison.Ordinal);
        Assert.Contains("--- /dev/null", createDiff.UnifiedDiff);
        Assert.Contains("+++ b/Src/Nested/NewFile.cs", createDiff.UnifiedDiff);
        Assert.Contains("+public class NewFile {}", createDiff.UnifiedDiff);

        var modifyDiff = Assert.Single(plan.FileChanges, c => c.Kind == FileChangeKind.Modify);
        Assert.Equal("Src/Existing.cs", modifyDiff.RelativePath);
        Assert.StartsWith("sha256:", modifyDiff.OriginalHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", modifyDiff.NewHash, StringComparison.Ordinal);
        Assert.Contains("--- a/Src/Existing.cs", modifyDiff.UnifiedDiff);
        Assert.Contains("+++ b/Src/Existing.cs", modifyDiff.UnifiedDiff);
        Assert.Contains("@@ ", modifyDiff.UnifiedDiff);
        Assert.Contains("-line 5 old", modifyDiff.UnifiedDiff);
        Assert.Contains("+line 5 new", modifyDiff.UnifiedDiff);
        Assert.DoesNotContain("line 1", modifyDiff.UnifiedDiff); // outside 3-line context

        var deleteDiff = Assert.Single(plan.FileChanges, c => c.Kind == FileChangeKind.Delete);
        Assert.Equal("Src/Obsolete.cs", deleteDiff.RelativePath);
        Assert.StartsWith("sha256:", deleteDiff.OriginalHash, StringComparison.Ordinal);
        Assert.Null(deleteDiff.NewHash);
        Assert.Contains("--- a/Src/Obsolete.cs", deleteDiff.UnifiedDiff);
        Assert.Contains("+++ /dev/null", deleteDiff.UnifiedDiff);
        Assert.Contains("-public class Obsolete {}", deleteDiff.UnifiedDiff);

        // Deterministic PlanHash across calls & ordering
        var reversedPlanResult = await engine.ComputePlanAsync(slnDir, changes.Reverse().ToArray());
        Assert.True(reversedPlanResult.IsSuccess);
        Assert.Equal(plan.PlanHash, reversedPlanResult.Value!.PlanHash);
    }

    [Theory]
    [InlineData(FileChangeKind.Create)]
    [InlineData(FileChangeKind.Modify)]
    public async Task ComputePlanAsync_CreateOrModifyWithNullContent_ReturnsValidationError(FileChangeKind kind)
    {
        var path = Path.Combine(_tempRoot, "File.cs");
        if (kind == FileChangeKind.Modify)
        {
            await File.WriteAllTextAsync(path, "initial");
        }

        var engine = new DryRunDiffEngine();
        var result = await engine.ComputePlanAsync(_tempRoot, [new StagedFileChange(path, kind, null)]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, result.Error?.Code);
    }

    [Fact]
    public async Task CommitAsync_HappyPath_AppliesCreatesModifiesAndDeletes()
    {
        var slnDir = Path.Combine(_tempRoot, "CommitSample");
        Directory.CreateDirectory(slnDir);
        var modifyPath = Path.Combine(slnDir, "Mod.cs");
        var deletePath = Path.Combine(slnDir, "Del.cs");
        var createPath = Path.Combine(slnDir, "Sub", "Add.cs");

        await File.WriteAllTextAsync(modifyPath, "old", new UTF8Encoding(false));
        await File.WriteAllTextAsync(deletePath, "bye", new UTF8Encoding(false));

        var engine = new DryRunDiffEngine();
        var plan = (await engine.ComputePlanAsync(slnDir,
        [
            new StagedFileChange(createPath, FileChangeKind.Create, "created"),
            new StagedFileChange(modifyPath, FileChangeKind.Modify, "new"),
            new StagedFileChange(deletePath, FileChangeKind.Delete)
        ])).Value!;

        var commit = await engine.CommitAsync(plan);

        Assert.True(commit.IsSuccess, commit.Error?.Message);
        Assert.False(commit.Value!.DryRun);
        Assert.Equal(plan, commit.Value.Plan);
        Assert.Equal([createPath], commit.Value.CreatedFiles);
        Assert.Equal([modifyPath], commit.Value.ModifiedFiles);
        Assert.Equal([deletePath], commit.Value.DeletedFiles);

        Assert.Equal("created", await File.ReadAllTextAsync(createPath));
        Assert.Equal("new", await File.ReadAllTextAsync(modifyPath));
        Assert.False(File.Exists(deletePath));
    }

    [Fact]
    public async Task CommitAsync_WhenDiskDriftsAfterPlan_ReturnsManifestConflictWithoutMutatingDisk()
    {
        var slnDir = Path.Combine(_tempRoot, "DriftSample");
        Directory.CreateDirectory(slnDir);
        var modifyPath = Path.Combine(slnDir, "Mod.cs");
        var createPath = Path.Combine(slnDir, "New.cs");
        var deletePath = Path.Combine(slnDir, "Del.cs");

        await File.WriteAllTextAsync(modifyPath, "v1", new UTF8Encoding(false));
        await File.WriteAllTextAsync(deletePath, "del1", new UTF8Encoding(false));

        var engine = new DryRunDiffEngine();
        var plan = (await engine.ComputePlanAsync(slnDir,
        [
            new StagedFileChange(createPath, FileChangeKind.Create, "new-content"),
            new StagedFileChange(modifyPath, FileChangeKind.Modify, "v2"),
            new StagedFileChange(deletePath, FileChangeKind.Delete)
        ])).Value!;

        // Simulate external modification to modifyPath
        await File.WriteAllTextAsync(modifyPath, "external-edit", new UTF8Encoding(false));

        var commitModifyDrift = await engine.CommitAsync(plan);
        Assert.False(commitModifyDrift.IsSuccess);
        Assert.Equal(ErrorCodes.MANIFEST_CONFLICT, commitModifyDrift.Error?.Code);
        Assert.False(File.Exists(createPath));
        Assert.Equal("external-edit", await File.ReadAllTextAsync(modifyPath));
        Assert.True(File.Exists(deletePath));

        // Reset modifyPath, simulate external creation of createPath
        await File.WriteAllTextAsync(modifyPath, "v1", new UTF8Encoding(false));
        await File.WriteAllTextAsync(createPath, "external-create", new UTF8Encoding(false));

        var commitCreateDrift = await engine.CommitAsync(plan);
        Assert.False(commitCreateDrift.IsSuccess);
        Assert.Equal(ErrorCodes.MANIFEST_CONFLICT, commitCreateDrift.Error?.Code);
        Assert.Equal("v1", await File.ReadAllTextAsync(modifyPath));
        Assert.True(File.Exists(deletePath));

        // Remove createPath, simulate external deletion of deletePath
        File.Delete(createPath);
        File.Delete(deletePath);

        var commitDeleteDrift = await engine.CommitAsync(plan);
        Assert.False(commitDeleteDrift.IsSuccess);
        Assert.Equal(ErrorCodes.MANIFEST_CONFLICT, commitDeleteDrift.Error?.Code);
        Assert.False(File.Exists(createPath));
        Assert.Equal("v1", await File.ReadAllTextAsync(modifyPath));
    }

    [Fact]
    public async Task CommitAsync_WhenWriteFailsMidBatch_RollsBackAllPriorChangesByteForByteAndReturnsIoError()
    {
        var slnDir = Path.Combine(_tempRoot, "RollbackSample");
        Directory.CreateDirectory(slnDir);

        var existingModifyPath = Path.Combine(slnDir, "Existing.cs");
        var existingDeletePath = Path.Combine(slnDir, "ToDelete.cs");
        var newCreatePath = Path.Combine(slnDir, "Created.cs");
        var failingCreatePath = Path.Combine(slnDir, "FailsHere.cs");

        // Include UTF-8 BOM + CRLF in original bytes to prove exact byte-for-byte restoration
        var originalModifyBytes = Encoding.UTF8.GetPreamble().Concat("original\r\ncontent"u8.ToArray()).ToArray();
        var originalDeleteBytes = "keep me\r\nbyte-exact"u8.ToArray();

        await File.WriteAllBytesAsync(existingModifyPath, originalModifyBytes);
        await File.WriteAllBytesAsync(existingDeletePath, originalDeleteBytes);

        var engine = new FailingOnNthWriteDiffEngine(failOnWriteCallNumber: 3);
        var plan = (await engine.ComputePlanAsync(slnDir,
        [
            new StagedFileChange(existingModifyPath, FileChangeKind.Modify, "modified"),
            new StagedFileChange(newCreatePath, FileChangeKind.Create, "temporary"),
            new StagedFileChange(existingDeletePath, FileChangeKind.Delete),
            new StagedFileChange(failingCreatePath, FileChangeKind.Create, "boom")
        ])).Value!;

        var commit = await engine.CommitAsync(plan);

        Assert.False(commit.IsSuccess);
        Assert.Equal(ErrorCodes.IO_ERROR, commit.Error?.Code);

        // Full rollback checks
        Assert.Equal(originalModifyBytes, await File.ReadAllBytesAsync(existingModifyPath));
        Assert.Equal(originalDeleteBytes, await File.ReadAllBytesAsync(existingDeletePath));
        Assert.False(File.Exists(newCreatePath));
        Assert.False(File.Exists(failingCreatePath));
    }

    [Fact]
    public async Task EntityService_DryRunAndCommit_PopulatesExecutionPlanAndHandlesTemplateErrors()
    {
        var slnSvc = new SolutionService();
        Assert.True((await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "PlanEntityApi", MapperKind.AutoMapper))).IsSuccess);
        var slnDir = Path.Combine(_tempRoot, "PlanEntityApi");

        var services = new ServiceCollection();
        services.AddMmaCore();
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ITemplateRenderer>());
        Assert.NotNull(provider.GetRequiredService<IDryRunDiffEngine>());

        var entitySvc = provider.GetRequiredService<IEntityService>();

        var dryRun = await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Invoice", "long", DryRun: true));
        Assert.True(dryRun.IsSuccess, dryRun.Error?.Message);
        Assert.True(dryRun.Value!.DryRun);
        Assert.NotNull(dryRun.Value.Plan);
        Assert.StartsWith("sha256:", dryRun.Value.Plan.PlanHash, StringComparison.Ordinal);
        Assert.Equal(9, dryRun.Value.Plan.FileChanges.Count); // 7 created + 2 modified
        Assert.All(dryRun.Value.Plan.FileChanges, diff => Assert.False(string.IsNullOrWhiteSpace(diff.UnifiedDiff)));

        var invoicePath = Path.Combine(slnDir, "PlanEntityApi", "PlanEntityApi.Core", "Database", "Tables", "Invoice.cs");
        Assert.False(File.Exists(invoicePath));

        var commit = await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Invoice", "long", DryRun: false));
        Assert.True(commit.IsSuccess, commit.Error?.Message);
        Assert.False(commit.Value!.DryRun);
        Assert.NotNull(commit.Value.Plan);
        Assert.Equal(dryRun.Value.Plan.PlanHash, commit.Value.Plan.PlanHash);
        Assert.True(File.Exists(invoicePath));

        // Malformed custom template fails cleanly with TEMPLATE_ERROR and zero writes
        var overrideDir = Path.Combine(slnDir, ".mma", "templates", "AutoMapper");
        Directory.CreateDirectory(overrideDir);
        await File.WriteAllTextAsync(Path.Combine(overrideDir, "Entity.scriban"), "{{ if true }}broken");

        var brokenResult = await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Order", "Guid", DryRun: false));
        Assert.False(brokenResult.IsSuccess);
        Assert.Equal(ErrorCodes.TEMPLATE_ERROR, brokenResult.Error?.Code);
        Assert.False(File.Exists(Path.Combine(slnDir, "PlanEntityApi", "PlanEntityApi.Core", "Database", "Tables", "Order.cs")));
    }

    [Fact]
    public async Task ComputePlanAsync_ModifyWithIdenticalContent_ProducesEmptyUnifiedDiff()
    {
        var slnDir = Path.Combine(_tempRoot, "NoOpSample");
        Directory.CreateDirectory(slnDir);
        var path = Path.Combine(slnDir, "Same.cs");
        await File.WriteAllTextAsync(path, "public class Same {}\n", new UTF8Encoding(false));

        var engine = new DryRunDiffEngine();
        var result = await engine.ComputePlanAsync(slnDir, [new StagedFileChange(path, FileChangeKind.Modify, "public class Same {}\n")]);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var diff = Assert.Single(result.Value!.FileChanges);
        Assert.Equal(string.Empty, diff.UnifiedDiff);
        Assert.Equal(diff.OriginalHash, diff.NewHash);
    }

    [Fact]
    public async Task CommitAsync_WhenCancelledMidBatch_RollsBackPriorChangesAndReturnsCancelled()
    {
        var slnDir = Path.Combine(_tempRoot, "CancelSample");
        Directory.CreateDirectory(slnDir);
        var existingPath = Path.Combine(slnDir, "Existing.cs");
        var createdPath = Path.Combine(slnDir, "Created.cs");
        var secondPath = Path.Combine(slnDir, "Second.cs");
        await File.WriteAllTextAsync(existingPath, "original", new UTF8Encoding(false));

        using var cts = new CancellationTokenSource();
        var engine = new CancellingOnNthWriteDiffEngine(cancelOnWriteCallNumber: 2, cts);
        var plan = (await engine.ComputePlanAsync(slnDir,
        [
            new StagedFileChange(existingPath, FileChangeKind.Modify, "changed"),
            new StagedFileChange(createdPath, FileChangeKind.Create, "new"),
            new StagedFileChange(secondPath, FileChangeKind.Create, "never")
        ])).Value!;

        var commit = await engine.CommitAsync(plan, cts.Token);

        Assert.False(commit.IsSuccess);
        Assert.Equal(ErrorCodes.CANCELLED, commit.Error?.Code);
        Assert.Equal("original", await File.ReadAllTextAsync(existingPath));
        Assert.False(File.Exists(createdPath));
        Assert.False(File.Exists(secondPath));
    }

    private sealed class FailingOnNthWriteDiffEngine(int failOnWriteCallNumber) : DryRunDiffEngine
    {
        private int _writeCalls;

        protected override Task WriteBytesAsync(string fullPath, byte[] bytes, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _writeCalls) == failOnWriteCallNumber)
            {
                throw new IOException($"Simulated write failure on call #{failOnWriteCallNumber} for '{fullPath}'.");
            }

            return base.WriteBytesAsync(fullPath, bytes, ct);
        }
    }

    private sealed class CancellingOnNthWriteDiffEngine(int cancelOnWriteCallNumber, CancellationTokenSource cts) : DryRunDiffEngine
    {
        private int _writeCalls;

        protected override async Task WriteBytesAsync(string fullPath, byte[] bytes, CancellationToken ct)
        {
            await base.WriteBytesAsync(fullPath, bytes, ct);
            if (Interlocked.Increment(ref _writeCalls) == cancelOnWriteCallNumber)
            {
                cts.Cancel();
            }
        }
    }
}
