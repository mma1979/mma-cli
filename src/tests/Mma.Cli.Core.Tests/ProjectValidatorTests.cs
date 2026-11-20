using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Services;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Core.Templates;

namespace Mma.Cli.Core.Tests;

public sealed class ProjectValidatorTests : IDisposable
{
    private readonly string _tempRoot;

    public ProjectValidatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-val-{Guid.NewGuid():N}");
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
    public async Task ValidateAsync_ValidSolutionWithAppliedEntity_ReturnsValidReportWithNoIssues()
    {
        var slnSvc = new SolutionService();
        Assert.True((await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "CleanApp", MapperKind.AutoMapper))).IsSuccess);
        var slnDir = Path.Combine(_tempRoot, "CleanApp");

        var entitySvc = new EntityService(new SolutionLocator(), new EmbeddedTemplateProvider());
        Assert.True((await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Customer", "Guid"))).IsSuccess);

        var validator = new ProjectValidator(new SolutionLocator(), new ManifestStore());
        var result = await validator.ValidateAsync(slnDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.IsValid);
        Assert.Equal("CleanApp", result.Value.SolutionName);
        Assert.Empty(result.Value.Issues);
    }

    [Fact]
    public async Task ValidateAsync_MissingSolution_ReturnsSolutionNotFound()
    {
        var validator = new ProjectValidator(new SolutionLocator(), new ManifestStore());
        var result = await validator.ValidateAsync(Path.Combine(_tempRoot, "DoesNotExist"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SOLUTION_NOT_FOUND, result.Error?.Code);
    }

    [Fact]
    public async Task ValidateAsync_AppliedEntityMissingFileOnDisk_ReportsDriftMissingEntityFileError()
    {
        var slnSvc = new SolutionService();
        Assert.True((await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "DriftApp", MapperKind.AutoMapper))).IsSuccess);
        var slnDir = Path.Combine(_tempRoot, "DriftApp");

        var entitySvc = new EntityService(new SolutionLocator(), new EmbeddedTemplateProvider());
        Assert.True((await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Order", "Guid"))).IsSuccess);

        var entityFile = Path.Combine(slnDir, "DriftApp", "DriftApp.Core", "Database", "Tables", "Order.cs");
        File.Delete(entityFile);

        var validator = new ProjectValidator(new SolutionLocator(), new ManifestStore());
        var result = await validator.ValidateAsync(slnDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.IsValid);
        Assert.Contains(result.Value.Issues, i =>
            i.Severity == "Error"
            && i.Code == "DRIFT_MISSING_ENTITY_FILE"
            && i.Target == "Order");
    }

    [Fact]
    public async Task ValidateAsync_UnappliedEntity_ReportsWarningWhileKeepingIsValidTrue()
    {
        var slnDir = Path.Combine(_tempRoot, "UnappliedApp");
        Directory.CreateDirectory(slnDir);
        await File.WriteAllTextAsync(Path.Combine(slnDir, "UnappliedApp.slnx"), "<Solution />");

        var store = new ManifestStore();
        await store.SaveAtomicAsync(slnDir, new ProjectManifest
        {
            SchemaVersion = 2,
            SolutionName = "UnappliedApp",
            Mapper = "Mapster",
            SolutionFormat = ".slnx",
            Entities = [new ManifestEntity("PendingOrder", "Guid", Applied: false)]
        });

        var validator = new ProjectValidator(new SolutionLocator(), store);
        var result = await validator.ValidateAsync(slnDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.IsValid);
        Assert.Contains(result.Value.Issues, i =>
            i.Severity == "Warning"
            && i.Code == "UNAPPLIED_ENTITY"
            && i.Target == "PendingOrder");
    }

    [Fact]
    public async Task ValidateAsync_InvalidIdentifiersTypesDuplicatesAndBrokenRelations_ReportsAllErrors()
    {
        var slnDir = Path.Combine(_tempRoot, "InvalidApp");
        Directory.CreateDirectory(slnDir);
        await File.WriteAllTextAsync(Path.Combine(slnDir, "InvalidApp.sln"), string.Empty);

        var store = new ManifestStore();
        await store.SaveAtomicAsync(slnDir, new ProjectManifest
        {
            SchemaVersion = 2,
            SolutionName = "InvalidApp",
            Mapper = "AutoMapper",
            SolutionFormat = ".sln",
            Entities =
            [
                new ManifestEntity(
                    "Order",
                    "double",
                    Applied: false,
                    Properties:
                    [
                        new ManifestProperty("Title", "string"),
                        new ManifestProperty("title", "object"),
                        new ManifestProperty("class", "int")
                    ]),
                new ManifestEntity("order", "Guid", Applied: false),
                new ManifestEntity("123Bad", "Guid", Applied: false)
            ],
            Relations =
            [
                new ManifestRelation("BrokenRel", "MissingParent", "MissingChild", "ParentId", "Guid", Applied: false)
            ]
        });

        var validator = new ProjectValidator(new SolutionLocator(), store);
        var result = await validator.ValidateAsync(slnDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.IsValid);
        Assert.True(result.Value.Issues.Count >= 6);
        Assert.All(result.Value.Issues.Where(i => i.Code != "UNAPPLIED_ENTITY"), i => Assert.Equal("Error", i.Severity));
    }

    [Fact]
    public async Task ValidateAsync_DuplicateRelationName_ReportsDuplicateRelationError()
    {
        var slnDir = Path.Combine(_tempRoot, "DupRelApp");
        Directory.CreateDirectory(slnDir);
        await File.WriteAllTextAsync(Path.Combine(slnDir, "DupRelApp.sln"), string.Empty);

        var store = new ManifestStore();
        await store.SaveAtomicAsync(slnDir, new ProjectManifest
        {
            SchemaVersion = 2,
            SolutionName = "DupRelApp",
            Mapper = "AutoMapper",
            SolutionFormat = ".sln",
            Entities =
            [
                new ManifestEntity("Category", "Guid", Applied: false),
                new ManifestEntity("Product", "Guid", Applied: false)
            ],
            Relations =
            [
                new ManifestRelation("Category_Product_CategoryId", "Category", "Product", "CategoryId"),
                new ManifestRelation("category_product_categoryid", "Category", "Product", "SecondaryCategoryId")
            ]
        });

        var validator = new ProjectValidator(new SolutionLocator(), store);
        var result = await validator.ValidateAsync(slnDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.IsValid);
        Assert.Contains(result.Value.Issues, i =>
            i.Severity == "Error"
            && i.Code == "DUPLICATE_RELATION");
    }

    [Fact]
    public async Task ValidateAsync_SolutionNameAndMapperMismatch_ReportsWarnings()
    {
        var slnDir = Path.Combine(_tempRoot, "MismatchApp");
        Directory.CreateDirectory(slnDir);
        await File.WriteAllTextAsync(Path.Combine(slnDir, "MismatchApp.sln"), string.Empty);

        var store = new ManifestStore();
        await store.SaveAtomicAsync(slnDir, new ProjectManifest
        {
            SchemaVersion = 2,
            SolutionName = "RenamedApp",
            Mapper = "UnknownMapper",
            SolutionFormat = ".sln"
        });

        var validator = new ProjectValidator(new SolutionLocator(), store);
        var result = await validator.ValidateAsync(slnDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.IsValid);
        Assert.Contains(result.Value.Issues, i => i.Severity == "Warning" && i.Code == "SOLUTION_NAME_MISMATCH");
        Assert.Contains(result.Value.Issues, i => i.Severity == "Warning" && i.Code == "MAPPER_MISMATCH");
    }
}
