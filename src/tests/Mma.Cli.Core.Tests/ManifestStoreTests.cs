using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Services;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Core.Templates;

namespace Mma.Cli.Core.Tests;

public sealed class ManifestStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public ManifestStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-manifest-{Guid.NewGuid():N}");
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
    public async Task SaveAtomicAsync_And_LoadAsync_RoundTripsV2ManifestWithDeterministicHash()
    {
        var slnDir = Path.Combine(_tempRoot, "V2App");
        Directory.CreateDirectory(slnDir);
        File.WriteAllText(Path.Combine(slnDir, "V2App.slnx"), "<Solution />");

        var lockManager = new SolutionLockManager();
        var locator = new SolutionLocator();
        var store = new ManifestStore(lockManager, locator);

        var fixedUtc = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var manifest = new ProjectManifest
        {
            SchemaVersion = 2,
            SolutionName = "V2App",
            Mapper = "Mapster",
            SolutionFormat = ".slnx",
            Entities =
            [
                new ManifestEntity(
                    "Order",
                    "Guid",
                    HasService: true,
                    HasApi: true,
                    Applied: true,
                    Properties:
                    [
                        new ManifestProperty("Title", "string", Nullable: false, IsForeignKey: false, Applied: true, MaxLength: 255, Required: true)
                    ])
            ],
            Relations =
            [
                new ManifestRelation("Customer_Order_CustomerId", "Customer", "Order", "CustomerId", "Guid", "1:N", "Cascade", Applied: true)
            ],
            UpdatedAtUtc = fixedUtc
        };

        var saved = await store.SaveAtomicAsync(slnDir, manifest);
        Assert.True(saved.IsSuccess);
        Assert.NotNull(saved.Value);
        Assert.False(saved.Value.UpgradedFromV1);
        Assert.StartsWith("sha256:", saved.Value.ContentHash, StringComparison.Ordinal);

        var mmaDir = Path.Combine(slnDir, ".mma");
        Assert.Empty(Directory.GetFiles(mmaDir, "*.tmp"));

        var rawJson = await File.ReadAllTextAsync(Path.Combine(mmaDir, "project.mma"));
        using (var doc = JsonDocument.Parse(rawJson))
        {
            Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("V2App", doc.RootElement.GetProperty("solutionName").GetString());
            Assert.Equal("Mapster", doc.RootElement.GetProperty("mapper").GetString());
            Assert.Equal(".slnx", doc.RootElement.GetProperty("solutionFormat").GetString());
        }

        var loaded = await store.LoadAsync(slnDir);
        Assert.True(loaded.IsSuccess);
        Assert.NotNull(loaded.Value);
        Assert.False(loaded.Value.UpgradedFromV1);
        Assert.Equal(saved.Value.ContentHash, loaded.Value.ContentHash);
        Assert.Equal("V2App", loaded.Value.Manifest.SolutionName);
        Assert.Equal("Mapster", loaded.Value.Manifest.Mapper);
        Assert.Equal(".slnx", loaded.Value.Manifest.SolutionFormat);
        Assert.Single(loaded.Value.Manifest.Entities);
        Assert.Single(loaded.Value.Manifest.Entities[0].Properties);
        Assert.Equal("Title", loaded.Value.Manifest.Entities[0].Properties[0].Name);
        Assert.Equal(255, loaded.Value.Manifest.Entities[0].Properties[0].MaxLength);
        Assert.Single(loaded.Value.Manifest.Relations);
        Assert.Equal(fixedUtc, loaded.Value.Manifest.UpdatedAtUtc);
    }

    [Fact]
    public async Task LoadAsync_WhenManifestMissing_ReturnsManifestInvalid()
    {
        var slnDir = Path.Combine(_tempRoot, "MissingManifest");
        Directory.CreateDirectory(slnDir);

        var store = new ManifestStore();
        var result = await store.LoadAsync(slnDir);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.MANIFEST_INVALID, result.Error?.Code);
    }

    [Fact]
    public async Task LoadAsync_WhenManifestCorruptJson_ReturnsManifestInvalid()
    {
        var slnDir = Path.Combine(_tempRoot, "CorruptManifest");
        var mmaDir = Path.Combine(slnDir, ".mma");
        Directory.CreateDirectory(mmaDir);
        await File.WriteAllTextAsync(Path.Combine(mmaDir, "project.mma"), "{ invalid json");

        var store = new ManifestStore();
        var result = await store.LoadAsync(slnDir);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.MANIFEST_INVALID, result.Error?.Code);
    }

    [Fact]
    public async Task LoadAsync_UpgradesLegacyV1Manifest_IncludingRowsAndChiledTypos()
    {
        var slnDir = Path.Combine(_tempRoot, "LegacyV1App");
        var mmaDir = Path.Combine(slnDir, ".mma");
        Directory.CreateDirectory(mmaDir);
        await File.WriteAllTextAsync(Path.Combine(slnDir, "LegacyV1App.sln"), string.Empty);

        var coreDir = Path.Combine(slnDir, "LegacyV1App", "LegacyV1App.Core");
        Directory.CreateDirectory(coreDir);
        await File.WriteAllTextAsync(Path.Combine(coreDir, "MappingProfile.cs"), string.Empty);

        var legacyJson = """
        {
          "Project": {
            "Id": 1,
            "Name": "LegacyV1App",
            "Path": "C:\\old\\LegacyV1App"
          },
          "Entities": [
            {
              "Id": 10,
              "EntityName": "Category",
              "PkType": "Guid",
              "HasService": true,
              "HasApi": true,
              "Applied": true
            },
            {
              "Id": 11,
              "EntityName": "Product",
              "PkType": "long",
              "HasService": true,
              "HasApi": false,
              "Applied": true
            }
          ],
          "Rows": [
            {
              "Id": 100,
              "EntityId": 10,
              "ColumnName": "Name",
              "DataType": "string",
              "Nullable": false,
              "IsForeignKey": false,
              "Applied": true
            },
            {
              "Id": 101,
              "EntityId": 11,
              "ColumnName": "Price",
              "DataType": "decimal",
              "Nullable": false,
              "IsForeignKey": false,
              "Applied": true
            }
          ],
          "Relations": [
            {
              "Id": 50,
              "Name": "Category_Product_CategoryId",
              "Applied": true,
              "ParentId": 10,
              "ChildId": 11
            },
            {
              "Id": 51,
              "Name": "",
              "Applied": false,
              "ParentEntityName": "Product",
              "ChiledEntityName": "Category"
            }
          ]
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(mmaDir, "project.mma"), legacyJson);

        var store = new ManifestStore();
        var loaded = await store.LoadAsync(slnDir);

        Assert.True(loaded.IsSuccess);
        Assert.NotNull(loaded.Value);
        Assert.True(loaded.Value.UpgradedFromV1);

        var m = loaded.Value.Manifest;
        Assert.Equal(2, m.SchemaVersion);
        Assert.Equal("LegacyV1App", m.SolutionName);
        Assert.Equal("AutoMapper", m.Mapper);
        Assert.Equal(".sln", m.SolutionFormat);
        Assert.Equal(2, m.Entities.Count);

        var category = Assert.Single(m.Entities, e => e.Name == "Category");
        Assert.True(category.HasApi);
        Assert.True(category.Applied);
        var catProp = Assert.Single(category.Properties);
        Assert.Equal("Name", catProp.Name);
        Assert.Equal("string", catProp.DataType);

        var product = Assert.Single(m.Entities, e => e.Name == "Product");
        Assert.Equal("long", product.PkType);
        Assert.False(product.HasApi);
        var prodProp = Assert.Single(product.Properties);
        Assert.Equal("Price", prodProp.Name);
        Assert.Equal("decimal", prodProp.DataType);

        Assert.Equal(2, m.Relations.Count);
        var rel1 = m.Relations[0];
        Assert.Equal("Category", rel1.ParentEntity);
        Assert.Equal("Product", rel1.ChildEntity);
        Assert.Equal("CategoryId", rel1.ForeignKey);
        Assert.True(rel1.Applied);

        var rel2 = m.Relations[1];
        Assert.Equal("Product", rel2.ParentEntity);
        Assert.Equal("Category", rel2.ChildEntity);
        Assert.Equal("ProductId", rel2.ForeignKey);

        // Saving upgraded manifest with ContentHash obtained from LoadAsync must succeed (no conflict)
        var upgradedSave = await store.SaveAtomicAsync(slnDir, m, expectedHash: loaded.Value.ContentHash);
        Assert.True(upgradedSave.IsSuccess);
        Assert.False(upgradedSave.Value!.UpgradedFromV1);
    }

    [Fact]
    public async Task SaveAtomicAsync_WithStaleExpectedHash_ReturnsManifestConflict()
    {
        var slnDir = Path.Combine(_tempRoot, "ConflictApp");
        Directory.CreateDirectory(slnDir);

        var store = new ManifestStore();
        var initial = new ProjectManifest
        {
            SolutionName = "ConflictApp",
            Mapper = "AutoMapper",
            SolutionFormat = ".sln"
        };

        var firstSave = await store.SaveAtomicAsync(slnDir, initial);
        Assert.True(firstSave.IsSuccess);
        var staleHash = firstSave.Value!.ContentHash;

        var secondSave = await store.SaveAtomicAsync(
            slnDir,
            initial with { Entities = [new ManifestEntity("Category")] },
            expectedHash: staleHash);
        Assert.True(secondSave.IsSuccess);

        var conflictSave = await store.SaveAtomicAsync(
            slnDir,
            initial with { Entities = [new ManifestEntity("Product")] },
            expectedHash: staleHash);

        Assert.False(conflictSave.IsSuccess);
        Assert.Equal(ErrorCodes.MANIFEST_CONFLICT, conflictSave.Error?.Code);

        var reloaded = await store.LoadAsync(slnDir);
        Assert.True(reloaded.IsSuccess);
        Assert.Single(reloaded.Value!.Manifest.Entities);
        Assert.Equal("Category", reloaded.Value.Manifest.Entities[0].Name);
    }

    [Fact]
    public async Task CoreServices_CreateEntityPropertyRelation_SynchronizeV2Manifest()
    {
        var slnSvc = new SolutionService();
        var createResult = await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "SyncApi", MapperKind.AutoMapper));
        Assert.True(createResult.IsSuccess);

        var slnDir = Path.Combine(_tempRoot, "SyncApi");
        var store = new ManifestStore();

        var afterCreate = await store.LoadAsync(slnDir);
        Assert.True(afterCreate.IsSuccess);
        Assert.False(afterCreate.Value!.UpgradedFromV1);
        Assert.Equal(2, afterCreate.Value.Manifest.SchemaVersion);
        Assert.Equal("SyncApi", afterCreate.Value.Manifest.SolutionName);
        Assert.Equal("AutoMapper", afterCreate.Value.Manifest.Mapper);
        Assert.Equal(".sln", afterCreate.Value.Manifest.SolutionFormat);
        Assert.Empty(afterCreate.Value.Manifest.Entities);

        var locator = new SolutionLocator();
        var templates = new EmbeddedTemplateProvider();
        var entitySvc = new EntityService(locator, templates);
        var propSvc = new PropertyService(locator);
        var relSvc = new RelationService(locator);

        // DryRun entity must NOT alter manifest
        var dryEntity = await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Category", "Guid", DryRun: true));
        Assert.True(dryEntity.IsSuccess);
        Assert.Empty((await store.LoadAsync(slnDir)).Value!.Manifest.Entities);

        Assert.True((await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Category", "Guid"))).IsSuccess);
        Assert.True((await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Product", "long", GenerateApi: false))).IsSuccess);

        // DryRun property must NOT alter manifest
        Assert.True((await propSvc.ApplyAsync(new AddPropertyRequest(slnDir, "Product", "Title", "string", DryRun: true))).IsSuccess);
        Assert.Empty((await store.LoadAsync(slnDir)).Value!.Manifest.Entities.Single(e => e.Name == "Product").Properties);

        Assert.True((await propSvc.ApplyAsync(new AddPropertyRequest(slnDir, "Product", "Title", "string"))).IsSuccess);
        Assert.True((await propSvc.ApplyAsync(new AddPropertyRequest(slnDir, "Product", "Weight", "decimal", Nullable: true))).IsSuccess);

        // DryRun relation must NOT alter manifest
        Assert.True((await relSvc.ApplyAsync(new AddRelationRequest(slnDir, "Category", "Product", "CategoryId", "Guid", DryRun: true))).IsSuccess);
        Assert.Empty((await store.LoadAsync(slnDir)).Value!.Manifest.Relations);

        Assert.True((await relSvc.ApplyAsync(new AddRelationRequest(slnDir, "Category", "Product", "CategoryId", "Guid"))).IsSuccess);

        var afterAdds = await store.LoadAsync(slnDir);
        Assert.True(afterAdds.IsSuccess);
        var mAdds = afterAdds.Value!.Manifest;
        Assert.Equal(2, mAdds.Entities.Count);

        var prod = Assert.Single(mAdds.Entities, e => e.Name == "Product");
        Assert.Equal("long", prod.PkType);
        Assert.False(prod.HasApi);
        Assert.True(prod.Applied);
        Assert.Contains(prod.Properties, p => p.Name == "Title" && p.DataType == "string" && !p.IsForeignKey && p.Applied);
        Assert.Contains(prod.Properties, p => p.Name == "Weight" && p.DataType == "decimal" && p.Nullable && !p.IsForeignKey);
        Assert.Contains(prod.Properties, p => p.Name == "CategoryId" && p.DataType == "Guid" && p.IsForeignKey && p.Applied);

        var rel = Assert.Single(mAdds.Relations);
        Assert.Equal("Category", rel.ParentEntity);
        Assert.Equal("Product", rel.ChildEntity);
        Assert.Equal("CategoryId", rel.ForeignKey);
        Assert.Equal("Guid", rel.ForeignKeyType);
        Assert.True(rel.Applied);

        // Remove property and relation and verify manifest stays synchronized
        Assert.True((await propSvc.ApplyAsync(new AddPropertyRequest(slnDir, "Product", "Weight", "decimal", Nullable: true, Remove: true))).IsSuccess);
        Assert.True((await relSvc.ApplyAsync(new AddRelationRequest(slnDir, "Category", "Product", "CategoryId", "Guid", Remove: true))).IsSuccess);

        var afterRemovals = await store.LoadAsync(slnDir);
        Assert.True(afterRemovals.IsSuccess);
        var mRemoved = afterRemovals.Value!.Manifest;
        Assert.Empty(mRemoved.Relations);
        var prodAfter = Assert.Single(mRemoved.Entities, e => e.Name == "Product");
        Assert.DoesNotContain(prodAfter.Properties, p => p.Name == "Weight");
        Assert.DoesNotContain(prodAfter.Properties, p => p.Name == "CategoryId");
        Assert.Contains(prodAfter.Properties, p => p.Name == "Title");
    }

    [Fact]
    public async Task EntityService_WhenManifestAbsent_CreatesMinimalV2Manifest()
    {
        var slnSvc = new SolutionService();
        Assert.True((await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "NoManifestApp", MapperKind.Mapster))).IsSuccess);
        var slnDir = Path.Combine(_tempRoot, "NoManifestApp");

        var manifestPath = Path.Combine(slnDir, ".mma", "project.mma");
        File.Delete(manifestPath);

        var entitySvc = new EntityService(new SolutionLocator(), new EmbeddedTemplateProvider());
        var result = await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Item", "Guid"));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(manifestPath));

        var loaded = await new ManifestStore().LoadAsync(slnDir);
        Assert.True(loaded.IsSuccess);
        Assert.False(loaded.Value!.UpgradedFromV1);
        Assert.Equal("NoManifestApp", loaded.Value.Manifest.SolutionName);
        var item = Assert.Single(loaded.Value.Manifest.Entities);
        Assert.Equal("Item", item.Name);
        Assert.True(item.Applied);
    }

    [Fact]
    public void AddMmaCore_ResolvesManifestStoreLockManagerAndProjectValidator()
    {
        var services = new ServiceCollection();
        services.AddMmaCore();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ISolutionLockManager>());
        Assert.NotNull(provider.GetRequiredService<IManifestStore>());
        Assert.NotNull(provider.GetRequiredService<IProjectValidator>());
    }

    [Fact]
    public async Task LoadAsync_WithUtf8BomLegacyV1Manifest_SucceedsAndUpgradesToV2()
    {
        var slnDir = Path.Combine(_tempRoot, "BomLegacyApp");
        var mmaDir = Path.Combine(slnDir, ".mma");
        Directory.CreateDirectory(mmaDir);
        await File.WriteAllTextAsync(Path.Combine(slnDir, "BomLegacyApp.sln"), string.Empty);

        var legacyJson = """
        {
          "Project": { "Name": "BomLegacyApp", "Path": "C:\\temp\\BomLegacyApp" },
          "Entities": [
            { "Id": 1, "EntityName": "Customer", "PkType": "Guid", "HasService": true, "HasApi": true, "Applied": true }
          ],
          "Rows": [
            { "Id": 1, "EntityId": 1, "ColumnName": "Email", "DataType": "string", "Nullable": false, "Applied": true }
          ],
          "Relations": []
        }
        """;

        var bom = Encoding.UTF8.GetPreamble();
        var payload = Encoding.UTF8.GetBytes(legacyJson);
        var bytesWithBom = new byte[bom.Length + payload.Length];
        Buffer.BlockCopy(bom, 0, bytesWithBom, 0, bom.Length);
        Buffer.BlockCopy(payload, 0, bytesWithBom, bom.Length, payload.Length);
        await File.WriteAllBytesAsync(Path.Combine(mmaDir, "project.mma"), bytesWithBom);

        var store = new ManifestStore();
        var loaded = await store.LoadAsync(slnDir);

        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.NotNull(loaded.Value);
        Assert.True(loaded.Value.UpgradedFromV1);
        Assert.Equal("BomLegacyApp", loaded.Value.Manifest.SolutionName);
        var entity = Assert.Single(loaded.Value.Manifest.Entities);
        Assert.Equal("Customer", entity.Name);
        Assert.Single(entity.Properties, p => p.Name == "Email");

        var saved = await store.SaveAtomicAsync(slnDir, loaded.Value.Manifest, expectedHash: loaded.Value.ContentHash);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
    }

    [Fact]
    public async Task LoadAsync_WithUtf8BomV2Manifest_Succeeds()
    {
        var slnDir = Path.Combine(_tempRoot, "BomV2App");
        var mmaDir = Path.Combine(slnDir, ".mma");
        Directory.CreateDirectory(mmaDir);

        var v2Json = """{"schemaVersion":2,"solutionName":"BomV2App","mapper":"Mapster","solutionFormat":".slnx","entities":[],"relations":[]}""";
        var bom = Encoding.UTF8.GetPreamble();
        var payload = Encoding.UTF8.GetBytes(v2Json);
        var bytesWithBom = new byte[bom.Length + payload.Length];
        Buffer.BlockCopy(bom, 0, bytesWithBom, 0, bom.Length);
        Buffer.BlockCopy(payload, 0, bytesWithBom, bom.Length, payload.Length);
        await File.WriteAllBytesAsync(Path.Combine(mmaDir, "project.mma"), bytesWithBom);

        var loaded = await new ManifestStore().LoadAsync(slnDir);

        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.False(loaded.Value!.UpgradedFromV1);
        Assert.Equal("BomV2App", loaded.Value.Manifest.SolutionName);
        Assert.Equal("Mapster", loaded.Value.Manifest.Mapper);
    }

    [Fact]
    public async Task Services_WhenManifestCorrupt_FailFastWithManifestInvalidWithoutMutatingDiskFiles()
    {
        var slnSvc = new SolutionService();
        Assert.True((await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "FailFastApp", MapperKind.AutoMapper))).IsSuccess);
        var slnDir = Path.Combine(_tempRoot, "FailFastApp");

        var locator = new SolutionLocator();
        var templates = new EmbeddedTemplateProvider();
        var entitySvc = new EntityService(locator, templates);
        var propSvc = new PropertyService(locator);
        var relSvc = new RelationService(locator);

        Assert.True((await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Customer", "Guid"))).IsSuccess);
        Assert.True((await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Order", "Guid"))).IsSuccess);

        var projectsPath = Path.Combine(slnDir, "FailFastApp");
        var dbContextPath = Path.Combine(projectsPath, "FailFastApp.EntityFramework", "ApplicationDbContext.cs");
        var customerEntityPath = Path.Combine(projectsPath, "FailFastApp.Core", "Database", "Tables", "Customer.cs");
        var orderEntityPath = Path.Combine(projectsPath, "FailFastApp.Core", "Database", "Tables", "Order.cs");
        var dbContextBefore = await File.ReadAllTextAsync(dbContextPath);
        var customerBefore = await File.ReadAllTextAsync(customerEntityPath);
        var orderBefore = await File.ReadAllTextAsync(orderEntityPath);

        await File.WriteAllTextAsync(Path.Combine(slnDir, ".mma", "project.mma"), "{ corrupt json");

        var entityFail = await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Product", "Guid"));
        Assert.False(entityFail.IsSuccess);
        Assert.Equal(ErrorCodes.MANIFEST_INVALID, entityFail.Error?.Code);
        Assert.False(File.Exists(Path.Combine(projectsPath, "FailFastApp.Core", "Database", "Tables", "Product.cs")));
        Assert.Equal(dbContextBefore, await File.ReadAllTextAsync(dbContextPath));

        var propFail = await propSvc.ApplyAsync(new AddPropertyRequest(slnDir, "Customer", "Email", "string"));
        Assert.False(propFail.IsSuccess);
        Assert.Equal(ErrorCodes.MANIFEST_INVALID, propFail.Error?.Code);
        Assert.Equal(customerBefore, await File.ReadAllTextAsync(customerEntityPath));

        var relFail = await relSvc.ApplyAsync(new AddRelationRequest(slnDir, "Customer", "Order", "CustomerId", "Guid"));
        Assert.False(relFail.IsSuccess);
        Assert.Equal(ErrorCodes.MANIFEST_INVALID, relFail.Error?.Code);
        Assert.Equal(customerBefore, await File.ReadAllTextAsync(customerEntityPath));
        Assert.Equal(orderBefore, await File.ReadAllTextAsync(orderEntityPath));
    }
}
