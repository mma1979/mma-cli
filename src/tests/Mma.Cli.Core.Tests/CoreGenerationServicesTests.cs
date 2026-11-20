using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.Core.Config;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Export;
using Mma.Cli.Core.Process;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Services;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Core.Templates;

namespace Mma.Cli.Core.Tests;

public sealed class CoreGenerationServicesTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SolutionLocator _locator = new();
    private readonly EmbeddedTemplateProvider _templates = new();

    public CoreGenerationServicesTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-core-svc-{Guid.NewGuid():N}");
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
    public async Task SolutionService_AutoMapper_ScaffoldsExpectedLayoutAndManifest()
    {
        var svc = new SolutionService();
        var events = new List<GenerationEvent>();
        var progress = new SynchronousProgress<GenerationEvent>(events.Add);

        var result = await svc.CreateAsync(
            new CreateSolutionRequest(_tempRoot, "SampleAutoApi", MapperKind.AutoMapper),
            progress);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.DryRun);
        Assert.NotEmpty(result.Value.CreatedFiles);
        Assert.Contains(events, e => e.Stage == GenerationStage.Completed);

        var slnDir = Path.Combine(_tempRoot, "SampleAutoApi");
        var slnPath = Path.Combine(slnDir, "SampleAutoApi.sln");
        var manifestPath = Path.Combine(slnDir, ".mma", "project.mma");
        var mappingProfilePath = Path.Combine(slnDir, "SampleAutoApi", "SampleAutoApi.Core", "MappingProfile.cs");
        var dbContextPath = Path.Combine(slnDir, "SampleAutoApi", "SampleAutoApi.EntityFramework", "ApplicationDbContext.cs");

        Assert.True(File.Exists(slnPath));
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(mappingProfilePath));
        Assert.True(File.Exists(dbContextPath));
        Assert.Contains("\"AutoMapper\"", File.ReadAllText(manifestPath));

        foreach (var file in new[] { slnPath, manifestPath, mappingProfilePath, dbContextPath })
        {
            var content = File.ReadAllText(file);
            Assert.Contains("SampleAutoApi", content);
            Assert.DoesNotContain("MySolutionName", content);
        }
    }

    [Fact]
    public async Task SolutionService_Mapster_ScaffoldsWithoutLeftoverPlaceholders()
    {
        var svc = new SolutionService();

        var result = await svc.CreateAsync(
            new CreateSolutionRequest(_tempRoot, "MyCompany.MapApi", MapperKind.Mapster));

        Assert.True(result.IsSuccess);
        var slnDir = Path.Combine(_tempRoot, "MyCompany.MapApi");
        Assert.True(File.Exists(Path.Combine(slnDir, "MyCompany.MapApi.sln")));
        Assert.Contains("\"Mapster\"", File.ReadAllText(Path.Combine(slnDir, ".mma", "project.mma")));
        Assert.Empty(Directory.GetFiles(slnDir, "MappingProfile.cs", SearchOption.AllDirectories));

        var checkedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".json", ".csproj" };
        foreach (var file in Directory.GetFiles(slnDir, "*.*", SearchOption.AllDirectories)
                     .Where(f => checkedExtensions.Contains(Path.GetExtension(f))))
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("AxialSystem.Covaluse", content);
            Assert.DoesNotContain("axialsystem-covaluse", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("axialaystem-covaluse", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task SolutionService_DryRun_LeavesDiskUntouched()
    {
        var svc = new SolutionService();

        var result = await svc.CreateAsync(
            new CreateSolutionRequest(_tempRoot, "DryRunApi", MapperKind.AutoMapper, DryRun: true));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.DryRun);
        Assert.Contains(result.Value.CreatedFiles, f => f.EndsWith("DryRunApi.sln", StringComparison.Ordinal));
        Assert.Contains(result.Value.CreatedFiles, f => f.EndsWith("project.mma", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "DryRunApi")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../Evil")]
    [InlineData("Bad/Name")]
    [InlineData("class")]
    [InlineData("123Invalid")]
    [InlineData("MyCompany..App")]
    public async Task SolutionService_InvalidSolutionName_ReturnsValidationError(string invalidName)
    {
        var svc = new SolutionService();
        var result = await svc.CreateAsync(new CreateSolutionRequest(_tempRoot, invalidName));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, result.Error?.Code);
    }

    [Fact]
    public async Task EntityService_AutoMapper_GeneratesFilesAndDryRunLeavesDiskUntouched()
    {
        var slnSvc = new SolutionService();
        await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "SampleAutoApi", MapperKind.AutoMapper));
        var slnDir = Path.Combine(_tempRoot, "SampleAutoApi");

        var entitySvc = new EntityService(_locator, _templates);

        var dryRun = await entitySvc.GenerateAsync(
            new AddEntityRequest(slnDir, "Customer", "long", DryRun: true));

        Assert.True(dryRun.IsSuccess);
        Assert.True(dryRun.Value!.DryRun);
        Assert.Equal(7, dryRun.Value.CreatedFiles.Count);
        Assert.Equal(2, dryRun.Value.ModifiedFiles.Count);
        Assert.False(File.Exists(Path.Combine(slnDir, "SampleAutoApi", "SampleAutoApi.Core", "Database", "Tables", "Customer.cs")));

        var real = await entitySvc.GenerateAsync(
            new AddEntityRequest(slnDir, "Customer", "long", DryRun: false));

        Assert.True(real.IsSuccess);
        Assert.False(real.Value!.DryRun);
        Assert.Equal(dryRun.Value.CreatedFiles.Count, real.Value.CreatedFiles.Count);

        var projectsPath = Path.Combine(slnDir, "SampleAutoApi");
        Assert.True(File.Exists(Path.Combine(projectsPath, "SampleAutoApi.Core", "Database", "Tables", "Customer.cs")));
        Assert.True(File.Exists(Path.Combine(projectsPath, "SampleAutoApi.Core", "Models", "CustomerModifyModel.cs")));
        Assert.True(File.Exists(Path.Combine(projectsPath, "SampleAutoApi.Core", "Models", "CustomerReadModel.cs")));
        Assert.True(File.Exists(Path.Combine(projectsPath, "SampleAutoApi.Core", "Validations", "CustomerValidator.cs")));
        Assert.True(File.Exists(Path.Combine(projectsPath, "SampleAutoApi.EntityFramework", "EntityConfigurations", "CustomerConfig.cs")));
        Assert.True(File.Exists(Path.Combine(projectsPath, "SampleAutoApi.Services", "CustomerService.cs")));
        Assert.True(File.Exists(Path.Combine(projectsPath, "SampleAutoApi.AppApi", "Controllers", "v1", "CustomersController.cs")));
        Assert.Contains("DbSet<Customer>", File.ReadAllText(Path.Combine(projectsPath, "SampleAutoApi.EntityFramework", "ApplicationDbContext.cs")));
        Assert.Contains("CustomerReadModel", File.ReadAllText(Path.Combine(projectsPath, "SampleAutoApi.Core", "MappingProfile.cs")));

        var duplicate = await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Customer", "long"));
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorCodes.ENTITY_EXISTS, duplicate.Error?.Code);
    }

    [Fact]
    public async Task EntityService_Mapster_GeneratesDotGCsAndSkipsControllerWhenNoApi()
    {
        var slnSvc = new SolutionService();
        await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "SampleMapApi", MapperKind.Mapster));
        var slnDir = Path.Combine(_tempRoot, "SampleMapApi");

        var entitySvc = new EntityService(_locator, _templates);
        var result = await entitySvc.GenerateAsync(
            new AddEntityRequest(slnDir, "Product", "Guid", GenerateApi: false));

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value!.CreatedFiles.Count);
        Assert.Single(result.Value.ModifiedFiles);

        var projectsPath = Path.Combine(slnDir, "SampleMapApi");
        Assert.True(File.Exists(Path.Combine(projectsPath, "SampleMapApi.Core", "Models", "ProductModifyModel.g.cs")));
        Assert.True(File.Exists(Path.Combine(projectsPath, "SampleMapApi.Core", "Models", "ProductReadModel.g.cs")));
        Assert.False(File.Exists(Path.Combine(projectsPath, "SampleMapApi.AppApi", "Controllers", "v1", "ProductsController.cs")));
    }

    [Fact]
    public async Task EntityService_InvalidIdentifierOrType_ReturnsValidationError()
    {
        var entitySvc = new EntityService(_locator, _templates);

        var badName = await entitySvc.GenerateAsync(new AddEntityRequest(_tempRoot, "../Bad", "Guid"));
        Assert.False(badName.IsSuccess);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, badName.Error?.Code);

        var badPk = await entitySvc.GenerateAsync(new AddEntityRequest(_tempRoot, "Order", "double"));
        Assert.False(badPk.IsSuccess);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, badPk.Error?.Code);
    }

    [Fact]
    public async Task PropertyService_AutoMapperAndMapster_AddRemoveAndDryRunAndIntHeaderPreserved()
    {
        var slnSvc = new SolutionService();
        await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "PropAutoApi", MapperKind.AutoMapper));
        var autoDir = Path.Combine(_tempRoot, "PropAutoApi");

        var entitySvc = new EntityService(_locator, _templates);
        await entitySvc.GenerateAsync(new AddEntityRequest(autoDir, "Order", "Guid"));

        var propSvc = new PropertyService(_locator);
        var projectsPath = Path.Combine(autoDir, "PropAutoApi");
        var entityPath = Path.Combine(projectsPath, "PropAutoApi.Core", "Database", "Tables", "Order.cs");
        var modifyPath = Path.Combine(projectsPath, "PropAutoApi.Core", "Models", "OrderModifyModel.cs");
        var readPath = Path.Combine(projectsPath, "PropAutoApi.Core", "Models", "OrderReadModel.cs");
        var configPath = Path.Combine(projectsPath, "PropAutoApi.EntityFramework", "EntityConfigurations", "OrderConfig.cs");

        var entityBefore = File.ReadAllText(entityPath);
        var dryAdd = await propSvc.ApplyAsync(new AddPropertyRequest(autoDir, "Order", "Title", "string", DryRun: true));
        Assert.True(dryAdd.IsSuccess);
        Assert.True(dryAdd.Value!.DryRun);
        Assert.Equal(4, dryAdd.Value.ModifiedFiles.Count);
        Assert.Equal(entityBefore, File.ReadAllText(entityPath));

        var addStr = await propSvc.ApplyAsync(new AddPropertyRequest(autoDir, "Order", "Title", "string"));
        Assert.True(addStr.IsSuccess);
        Assert.Contains("public string Title {get; set;}", File.ReadAllText(modifyPath));
        Assert.Contains("public string Title {get; set;}", File.ReadAllText(readPath));
        Assert.Contains("public string Title {get; private set;}", File.ReadAllText(entityPath));
        Assert.Contains("Title = dto.Title;", File.ReadAllText(entityPath));
        Assert.Contains("builder.Property(e => e.Title).HasMaxLength(255);", File.ReadAllText(configPath));
        AssertDeletedDateFluentChainIntact(configPath);

        var duplicateProp = await propSvc.ApplyAsync(new AddPropertyRequest(autoDir, "Order", "Title", "string"));
        Assert.False(duplicateProp.IsSuccess);
        Assert.Equal(ErrorCodes.PROPERTY_EXISTS, duplicateProp.Error?.Code);

        var configBeforeInt = File.ReadAllText(configPath);
        var addInt = await propSvc.ApplyAsync(new AddPropertyRequest(autoDir, "Order", "Quantity", "int"));
        Assert.True(addInt.IsSuccess);
        Assert.DoesNotContain(configPath, addInt.Value!.ModifiedFiles);
        Assert.Equal(configBeforeInt, File.ReadAllText(configPath));

        var removeInt = await propSvc.ApplyAsync(new AddPropertyRequest(autoDir, "Order", "Quantity", "int", Remove: true));
        Assert.True(removeInt.IsSuccess);
        Assert.StartsWith("using Microsoft.EntityFrameworkCore;", File.ReadAllLines(configPath)[0]);
        Assert.Equal(configBeforeInt, File.ReadAllText(configPath));

        var removeStr = await propSvc.ApplyAsync(new AddPropertyRequest(autoDir, "Order", "Title", "string", Remove: true));
        Assert.True(removeStr.IsSuccess);
        Assert.DoesNotContain("public string Title {get; set;}", File.ReadAllText(modifyPath));
        Assert.DoesNotContain("Title = dto.Title;", File.ReadAllText(entityPath));
        Assert.DoesNotContain("builder.Property(e => e.Title)", File.ReadAllText(configPath));

        var missingRemove = await propSvc.ApplyAsync(new AddPropertyRequest(autoDir, "Order", "Title", "string", Remove: true));
        Assert.False(missingRemove.IsSuccess);
        Assert.Equal(ErrorCodes.PROPERTY_NOT_FOUND, missingRemove.Error?.Code);

        await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "PropMapApi", MapperKind.Mapster));
        var mapDir = Path.Combine(_tempRoot, "PropMapApi");
        await entitySvc.GenerateAsync(new AddEntityRequest(mapDir, "Product", "Guid"));

        var mapAdd = await propSvc.ApplyAsync(new AddPropertyRequest(mapDir, "Product", "Price", "decimal"));
        Assert.True(mapAdd.IsSuccess);
        var mapModifyPath = Path.Combine(mapDir, "PropMapApi", "PropMapApi.Core", "Models", "ProductModifyModel.g.cs");
        Assert.Contains("public decimal Price {get; set;}", File.ReadAllText(mapModifyPath));
    }

    [Fact]
    public async Task RelationService_AutoMapperAndMapster_AddRemoveDryRunAndPreserveParentReadModel()
    {
        var slnSvc = new SolutionService();
        await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "RelMapApi", MapperKind.Mapster));
        var mapDir = Path.Combine(_tempRoot, "RelMapApi");

        var entitySvc = new EntityService(_locator, _templates);
        await entitySvc.GenerateAsync(new AddEntityRequest(mapDir, "Category", "Guid"));
        await entitySvc.GenerateAsync(new AddEntityRequest(mapDir, "Product", "Guid"));

        var projectsPath = Path.Combine(mapDir, "RelMapApi");
        var categoryModifyPath = Path.Combine(projectsPath, "RelMapApi.Core", "Models", "CategoryModifyModel.g.cs");
        var categoryReadPath = Path.Combine(projectsPath, "RelMapApi.Core", "Models", "CategoryReadModel.g.cs");
        var productModifyPath = Path.Combine(projectsPath, "RelMapApi.Core", "Models", "ProductModifyModel.g.cs");
        var productReadPath = Path.Combine(projectsPath, "RelMapApi.Core", "Models", "ProductReadModel.g.cs");
        var categoryEntityPath = Path.Combine(projectsPath, "RelMapApi.Core", "Database", "Tables", "Category.cs");
        var productEntityPath = Path.Combine(projectsPath, "RelMapApi.Core", "Database", "Tables", "Product.cs");
        var categoryConfigPath = Path.Combine(projectsPath, "RelMapApi.EntityFramework", "EntityConfigurations", "CategoryConfig.cs");

        var categoryReadBefore = File.ReadAllText(categoryReadPath);
        var categoryModifyBefore = File.ReadAllText(categoryModifyPath);

        var relSvc = new RelationService(_locator);
        var dry = await relSvc.ApplyAsync(new AddRelationRequest(mapDir, "Category", "Product", "CategoryId", "Guid", DryRun: true));
        Assert.True(dry.IsSuccess);
        Assert.True(dry.Value!.DryRun);
        Assert.Equal(6, dry.Value.ModifiedFiles.Count);
        Assert.Equal(categoryModifyBefore, File.ReadAllText(categoryModifyPath));

        var add = await relSvc.ApplyAsync(new AddRelationRequest(mapDir, "Category", "Product", "CategoryId", "Guid"));
        Assert.True(add.IsSuccess);
        Assert.Contains("List<Product>", File.ReadAllText(categoryModifyPath));
        Assert.Contains("CategoryId", File.ReadAllText(productModifyPath));
        Assert.Contains("Category? Category", File.ReadAllText(productModifyPath));
        Assert.Contains("CategoryId", File.ReadAllText(productReadPath));
        Assert.Contains("ICollection<Product>", File.ReadAllText(categoryEntityPath));
        Assert.Contains("CategoryId", File.ReadAllText(productEntityPath));
        Assert.Contains("HasMany(e => e.Products).WithOne(e => e.Category)", File.ReadAllText(categoryConfigPath));
        AssertDeletedDateFluentChainIntact(categoryConfigPath);
        Assert.Equal(categoryReadBefore, File.ReadAllText(categoryReadPath));

        var remove = await relSvc.ApplyAsync(new AddRelationRequest(mapDir, "Category", "Product", "CategoryId", "Guid", Remove: true));
        Assert.True(remove.IsSuccess);
        Assert.DoesNotContain("List<Product>", File.ReadAllText(categoryModifyPath));
        Assert.DoesNotContain("CategoryId", File.ReadAllText(productModifyPath));
        Assert.DoesNotContain("CategoryId", File.ReadAllText(productReadPath));
        Assert.DoesNotContain("ICollection<Product>", File.ReadAllText(categoryEntityPath));
        Assert.DoesNotContain("CategoryId", File.ReadAllText(productEntityPath));
        Assert.DoesNotContain("HasMany(e => e.Products).WithOne(e => e.Category)", File.ReadAllText(categoryConfigPath));
        Assert.Equal(categoryReadBefore, File.ReadAllText(categoryReadPath));

        await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "RelAutoApi", MapperKind.AutoMapper));
        var autoDir = Path.Combine(_tempRoot, "RelAutoApi");
        await entitySvc.GenerateAsync(new AddEntityRequest(autoDir, "Order", "Guid"));
        await entitySvc.GenerateAsync(new AddEntityRequest(autoDir, "OrderItem", "Guid"));

        var autoProjects = Path.Combine(autoDir, "RelAutoApi");
        var orderReadBefore = File.ReadAllText(Path.Combine(autoProjects, "RelAutoApi.Core", "Models", "OrderReadModel.cs"));
        var autoAdd = await relSvc.ApplyAsync(new AddRelationRequest(autoDir, "Order", "OrderItem", "OrderId", "Guid"));
        Assert.True(autoAdd.IsSuccess);
        Assert.Contains("List<OrderItem>", File.ReadAllText(Path.Combine(autoProjects, "RelAutoApi.Core", "Models", "OrderModifyModel.cs")));
        Assert.Equal(orderReadBefore, File.ReadAllText(Path.Combine(autoProjects, "RelAutoApi.Core", "Models", "OrderReadModel.cs")));

        var autoRemove = await relSvc.ApplyAsync(new AddRelationRequest(autoDir, "Order", "OrderItem", "OrderId", "Guid", Remove: true));
        Assert.True(autoRemove.IsSuccess);
        Assert.DoesNotContain("List<OrderItem>", File.ReadAllText(Path.Combine(autoProjects, "RelAutoApi.Core", "Models", "OrderModifyModel.cs")));
    }

    [Fact]
    public async Task RelationService_WhenForeignKeyPropertyAlreadyExists_ReturnsPropertyExists()
    {
        var slnSvc = new SolutionService();
        await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "DupRelApi", MapperKind.AutoMapper));
        var slnDir = Path.Combine(_tempRoot, "DupRelApi");

        var entitySvc = new EntityService(_locator, _templates);
        await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Category", "Guid"));
        await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Product", "Guid"));

        var propSvc = new PropertyService(_locator);
        await propSvc.ApplyAsync(new AddPropertyRequest(slnDir, "Product", "CategoryId", "int"));

        var relSvc = new RelationService(_locator);
        var result = await relSvc.ApplyAsync(new AddRelationRequest(slnDir, "Category", "Product", "CategoryId", "Guid"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PROPERTY_EXISTS, result.Error?.Code);
    }

    [Fact]
    public async Task RelationService_RemovesLegacyDoubleSpacedRelation_EvenWithMismatchedFkType()
    {
        var slnSvc = new SolutionService();
        await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "LegacyRelApi", MapperKind.AutoMapper));
        var slnDir = Path.Combine(_tempRoot, "LegacyRelApi");

        var entitySvc = new EntityService(_locator, _templates);
        await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Order", "Guid"));
        await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "OrderItem", "Guid"));

        var projects = Path.Combine(slnDir, "LegacyRelApi");
        var orderModify = Path.Combine(projects, "LegacyRelApi.Core", "Models", "OrderModifyModel.cs");
        var itemModify = Path.Combine(projects, "LegacyRelApi.Core", "Models", "OrderItemModifyModel.cs");
        var itemRead = Path.Combine(projects, "LegacyRelApi.Core", "Models", "OrderItemReadModel.cs");
        var orderEntity = Path.Combine(projects, "LegacyRelApi.Core", "Database", "Tables", "Order.cs");
        var itemEntity = Path.Combine(projects, "LegacyRelApi.Core", "Database", "Tables", "OrderItem.cs");
        var orderConfig = Path.Combine(projects, "LegacyRelApi.EntityFramework", "EntityConfigurations", "OrderConfig.cs");

        InsertBeforeClosingBraces(orderModify, "public virtual  List<OrderItem>? OrderItems {get; set;}");
        InsertBeforeClosingBraces(itemModify, "public  long? OrderId {get; set;}");
        InsertBeforeClosingBraces(itemModify, "public virtual  Order? Order {get; set;}");
        InsertBeforeClosingBraces(itemRead, "public  long? OrderId {get; set;}");

        var orderLines = File.ReadAllLines(orderEntity).ToList();
        orderLines.Insert(orderLines.FindIndex(l => l.Contains("OrderValidator _Validator;")) - 1, "public virtual  ICollection<OrderItem>? OrderItems {get; set;}");
        File.WriteAllText(orderEntity, string.Join('\n', orderLines));

        var itemLines = File.ReadAllLines(itemEntity).ToList();
        var itemValIdx = itemLines.FindIndex(l => l.Contains("OrderItemValidator _Validator;")) - 1;
        itemLines.Insert(itemValIdx, "public virtual  Order? Order {get; private set;}");
        itemLines.Insert(itemValIdx, "public  long? OrderId {get; private set;}");
        itemLines.Insert(itemLines.FindIndex(l => l.Contains("public OrderItem(")) + 10, "OrderId = dto.OrderId;");
        itemLines.Insert(itemLines.FindIndex(l => l.Contains("public OrderItem Update(")) + 10, "OrderId = dto.OrderId;");
        File.WriteAllText(itemEntity, string.Join('\n', itemLines));

        var cfgLines = File.ReadAllLines(orderConfig).ToList();
        cfgLines.Insert(cfgLines.FindIndex(l => l.Contains(".HasColumnType(\"datetime\");")) + 1,
            "builder.HasMany(e => e.OrderItems).WithOne(e => e.Order).HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);");
        File.WriteAllText(orderConfig, string.Join('\n', cfgLines));

        var relSvc = new RelationService(_locator);
        var removeResult = await relSvc.ApplyAsync(
            new AddRelationRequest(slnDir, "Order", "OrderItem", "OrderId", "Guid", Remove: true));

        Assert.True(removeResult.IsSuccess);
        Assert.DoesNotContain("OrderItems", File.ReadAllText(orderModify));
        Assert.DoesNotContain("OrderId", File.ReadAllText(itemModify));
        Assert.DoesNotContain("Order? Order", File.ReadAllText(itemModify));
        Assert.DoesNotContain("OrderId", File.ReadAllText(itemRead));
        Assert.DoesNotContain("OrderItems", File.ReadAllText(orderEntity));
        Assert.DoesNotContain("OrderId", File.ReadAllText(itemEntity));
        Assert.DoesNotContain("Order? Order", File.ReadAllText(itemEntity));
        Assert.DoesNotContain("HasMany(e => e.OrderItems)", File.ReadAllText(orderConfig));
    }

    [Fact]
    public async Task DotnetRunner_Version_SucceedsAndEmitsOutput()
    {
        var runner = new DotnetRunner();
        var events = new List<GenerationEvent>();
        var progress = new SynchronousProgress<GenerationEvent>(events.Add);

        var result = await runner.RunAsync(_tempRoot, ["--version"], progress);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(0, result.Value.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.StandardOutput));
        Assert.Contains(events, e => e.Stage == GenerationStage.ProcessOutput);
    }

    [Fact]
    public async Task DotnetRunner_WhenCancelled_ReturnsCancelled()
    {
        var runner = new DotnetRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await runner.RunAsync(_tempRoot, ["--version"], ct: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CANCELLED, result.Error?.Code);
    }

    [Fact]
    public async Task DotnetRunner_NonZeroExitCode_ReturnsProcessFailed()
    {
        var runner = new DotnetRunner();
        var result = await runner.RunAsync(_tempRoot, ["build", "NonExistentProject.csproj"]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PROCESS_FAILED, result.Error?.Code);
    }

    [Fact]
    public async Task DotnetRunner_MissingWorkingDirectory_ReturnsIoError()
    {
        var runner = new DotnetRunner();
        var result = await runner.RunAsync(Path.Combine(_tempRoot, "missing-dir"), ["--version"]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.IO_ERROR, result.Error?.Code);
    }

    [Fact]
    public void ServiceCollectionExtensions_AddMmaCore_ResolvesAllServices()
    {
        var services = new ServiceCollection();
        services.AddMmaCore();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ISolutionLocator>());
        Assert.NotNull(provider.GetRequiredService<IConfigStore>());
        Assert.NotNull(provider.GetRequiredService<ITemplateProvider>());
        Assert.NotNull(provider.GetRequiredService<IDotnetRunner>());
        Assert.NotNull(provider.GetRequiredService<ISolutionExporter>());
        Assert.NotNull(provider.GetRequiredService<ISolutionService>());
        Assert.NotNull(provider.GetRequiredService<IEntityService>());
        Assert.NotNull(provider.GetRequiredService<IPropertyService>());
        Assert.NotNull(provider.GetRequiredService<IRelationService>());
    }

    [Theory]
    [InlineData("User", "Users")]
    [InlineData("Category", "Categories")]
    [InlineData("Address", "Addresses")]
    [InlineData("Entity", "Entities")]
    [InlineData("Bus", "Buses")]
    public void NamingHelper_Pluralize_MatchesLegacyBehavior(string singular, string expected)
    {
        Assert.Equal(expected, NamingHelper.Pluralize(singular));
    }

    [Theory]
    [InlineData("Order\n")]
    [InlineData("Order\r\n")]
    [InlineData("123Order")]
    [InlineData("class")]
    public void NamingHelper_IsValidIdentifier_RejectsTrailingNewlinesAndInvalidNames(string candidate)
    {
        Assert.False(NamingHelper.IsValidIdentifier(candidate));
    }

    [Theory]
    [InlineData("My.App\n")]
    [InlineData("My..App")]
    [InlineData("My.class")]
    public void NamingHelper_IsValidSolutionName_RejectsTrailingNewlinesAndInvalidNames(string candidate)
    {
        Assert.False(NamingHelper.IsValidSolutionName(candidate));
    }

    private static void AssertDeletedDateFluentChainIntact(string configPath)
    {
        var lines = File.ReadAllLines(configPath);
        var deletedDateIdx = Array.FindIndex(lines, l => l.Contains("builder.Property(e => e.DeletedDate)", StringComparison.Ordinal));
        Assert.True(deletedDateIdx >= 0, $"DeletedDate configuration not found in {configPath}");
        Assert.True(deletedDateIdx + 1 < lines.Length);
        Assert.Equal(".HasColumnType(\"datetime\");", lines[deletedDateIdx + 1].Trim());
    }

    private static void InsertBeforeClosingBraces(string path, string line)
    {
        var lines = File.ReadAllLines(path).ToList();
        lines.Insert(Math.Max(0, lines.Count - 2), line);
        File.WriteAllText(path, string.Join('\n', lines));
    }

    [Theory]
    [InlineData("guid", "Guid")]
    [InlineData("datetime", "DateTime")]
    [InlineData("timespan", "TimeSpan")]
    [InlineData("byte[]", "byte[]")]
    [InlineData("INT", "int")]
    public void DataTypes_Normalize_MapsSupportedTypes(string raw, string expected)
    {
        Assert.True(DataTypes.IsSupported(raw));
        Assert.Equal(expected, DataTypes.Normalize(raw));
    }

    [Fact]
    public void EmbeddedTemplateProvider_UsesExternalOverrideWhenPresent()
    {
        var provider = new EmbeddedTemplateProvider();
        var defaultTemplate = provider.GetTemplate(_tempRoot, MapperKind.AutoMapper, "Entity");
        Assert.Contains("BaseEntity", defaultTemplate);

        var overrideDir = Path.Combine(_tempRoot, ".mma", "templates", "AutoMapper");
        Directory.CreateDirectory(overrideDir);
        File.WriteAllText(Path.Combine(overrideDir, "Entity.cs"), "// custom override");

        var overridden = provider.GetTemplate(_tempRoot, MapperKind.AutoMapper, "Entity");
        Assert.Equal("// custom override", overridden);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
