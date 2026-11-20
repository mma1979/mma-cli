using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;
using Xunit;

namespace Mma.Cli.Tests;

[CollectionDefinition("CurrentDirectory", DisableParallelization = true)]
public class CurrentDirectoryCollection
{
}

[Collection("CurrentDirectory")]
public class LegacyBuilderCharacterizationTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mma-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void RestoreAndDelete(string originalCwd, string tempRoot)
    {
        Directory.SetCurrentDirectory(originalCwd);
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static SolutionBuilder ScaffoldSolution(string rootDir, string solutionName, string mapper)
    {
        Directory.SetCurrentDirectory(rootDir);
        var builder = SolutionBuilder.New(new[] { "new", solutionName, Flags.MapperFlag, mapper })
            .CreateSolutionDirectory()
            .ExtractSolution()
            .RootRenameAndReplace()
            .RenameProjects()
            .RenameCsprojFiles()
            .ReplaceNamespaces()
            .CreateMmaFolder();

        Directory.SetCurrentDirectory(builder.SolutionPath);
        return builder;
    }

    private static EntityBuilder ScaffoldEntity(string entityName, string pkType, string? mapper = null, bool noApi = false)
    {
        var args = new List<string> { "g", "e", entityName, pkType };
        if (!string.IsNullOrEmpty(mapper))
        {
            args.Add(Flags.MapperFlag);
            args.Add(mapper);
        }
        if (noApi)
        {
            args.Add(Flags.ApiFlag);
        }

        var argsArray = args.ToArray();
        return EntityBuilder.New(argsArray)
            .GenerateModels()
            .GenerateValidator()
            .GenerateEntity()
            .GenerateEntityConfig()
            .DbContextMapping()
            .GenerateService()
            .GenerateController(!argsArray.Contains(Flags.ApiFlag));
    }

    [Fact]
    public void SolutionBuilder_AutoMapper_ScaffoldsExpectedLayoutAndMmaManifest()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = CreateTempDirectory();
        try
        {
            var builder = ScaffoldSolution(tempRoot, "SampleAutoApi", Mappers.AutoMapper);

            Assert.Equal(Mappers.AutoMapper, builder.Mapper);

            var slnPath = Path.Combine(builder.SolutionPath, "SampleAutoApi.sln");
            var mmaManifestPath = Path.Combine(builder.SolutionPath, ".mma", "project.mma");
            var mappingProfilePath = Path.Combine(builder.ProjectsPath, "SampleAutoApi.Core", "MappingProfile.cs");
            var dbContextPath = Path.Combine(builder.ProjectsPath, "SampleAutoApi.EntityFramework", "ApplicationDbContext.cs");

            Assert.True(File.Exists(slnPath));
            Assert.True(File.Exists(mmaManifestPath));
            Assert.True(File.Exists(mappingProfilePath));
            Assert.True(File.Exists(dbContextPath));

            foreach (var filePath in new[] { slnPath, mmaManifestPath, mappingProfilePath, dbContextPath })
            {
                var content = File.ReadAllText(filePath);
                Assert.Contains("SampleAutoApi", content);
                Assert.DoesNotContain("MySolutionName", content);
            }
        }
        finally
        {
            RestoreAndDelete(originalCwd, tempRoot);
        }
    }

    [Fact]
    public void SolutionBuilder_Mapster_ScaffoldsExpectedLayoutWithoutLeftoverPlaceholders()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = CreateTempDirectory();
        try
        {
            var builder = ScaffoldSolution(tempRoot, "SampleMapApi", Mappers.Mapster);

            Assert.Equal(Mappers.Mapster, builder.Mapper);

            var slnPath = Path.Combine(builder.SolutionPath, "SampleMapApi.sln");
            var mmaManifestPath = Path.Combine(builder.SolutionPath, ".mma", "project.mma");

            Assert.True(File.Exists(slnPath));
            Assert.True(File.Exists(mmaManifestPath));
            Assert.Empty(Directory.GetFiles(builder.SolutionPath, "MappingProfile.cs", SearchOption.AllDirectories));

            var checkedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".json", ".csproj" };
            var scaffoldedFiles = Directory.GetFiles(builder.SolutionPath, "*.*", SearchOption.AllDirectories)
                .Where(f => checkedExtensions.Contains(Path.GetExtension(f)))
                .ToArray();

            Assert.NotEmpty(scaffoldedFiles);

            foreach (var filePath in scaffoldedFiles)
            {
                var content = File.ReadAllText(filePath);
                Assert.DoesNotContain("AxialSystem.Covaluse", content);
                Assert.DoesNotContain("axialsystem-covaluse", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("axialaystem-covaluse", content, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            RestoreAndDelete(originalCwd, tempRoot);
        }
    }

    [Fact]
    public void SolutionBuilder_WithoutMapperFlag_DefaultsToAutoMapper()
    {
        var builder = SolutionBuilder.New(new[] { "new", "DefaultMapperApi" });

        Assert.Equal("DefaultMapperApi", builder.SolutionName);
        Assert.Equal(Mappers.AutoMapper, builder.Mapper);
    }

    [Fact]
    public void EntityBuilder_WithoutMapperFlag_DefaultsToAutoMapper()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = CreateTempDirectory();
        try
        {
            var solution = ScaffoldSolution(tempRoot, "SampleAutoApi", Mappers.AutoMapper);
            var builder = ScaffoldEntity("Customer", "long");

            Assert.Equal(Mappers.AutoMapper, builder.Mapper);
            Assert.Equal("long", builder.PkType);

            var entityPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Database", "Tables", "Customer.cs");
            var modifyModelPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Models", "CustomerModifyModel.cs");
            var readModelPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Models", "CustomerReadModel.cs");
            var validatorPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Validations", "CustomerValidator.cs");
            var configPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.EntityFramework", "EntityConfigurations", "CustomerConfig.cs");
            var servicePath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Services", "CustomerService.cs");
            var controllerPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.AppApi", "Controllers", "v1", "CustomersController.cs");
            var dbContextPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.EntityFramework", "ApplicationDbContext.cs");
            var mappingProfilePath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "MappingProfile.cs");

            Assert.True(File.Exists(entityPath));
            Assert.True(File.Exists(modifyModelPath));
            Assert.True(File.Exists(readModelPath));
            Assert.True(File.Exists(validatorPath));
            Assert.True(File.Exists(configPath));
            Assert.True(File.Exists(servicePath));
            Assert.True(File.Exists(controllerPath));

            Assert.Contains("DbSet<Customer>", File.ReadAllText(dbContextPath));
            Assert.Contains("CustomerConfig", File.ReadAllText(dbContextPath));
            Assert.Contains("Customer", File.ReadAllText(mappingProfilePath));
        }
        finally
        {
            RestoreAndDelete(originalCwd, tempRoot);
        }
    }

    [Fact]
    public void EntityBuilder_Mapster_GeneratesDotGCsModelsAndSkipsControllerWhenNoApi()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = CreateTempDirectory();
        try
        {
            var solution = ScaffoldSolution(tempRoot, "SampleMapApi", Mappers.Mapster);
            var args = new[] { "g", "e", "Product", "Guid", "--mapper", "Mapster", "--no-api" };

            var builder = EntityBuilder.New(args)
                .GenerateModels()
                .GenerateValidator()
                .GenerateEntity()
                .GenerateEntityConfig()
                .DbContextMapping()
                .GenerateService()
                .GenerateController(false);

            Assert.Equal(Mappers.Mapster, builder.Mapper);

            var modifyModelPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Models", "ProductModifyModel.g.cs");
            var readModelPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Models", "ProductReadModel.g.cs");
            var controllerPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.AppApi", "Controllers", "v1", "ProductsController.cs");

            Assert.True(File.Exists(modifyModelPath));
            Assert.True(File.Exists(readModelPath));
            Assert.False(File.Exists(controllerPath));
        }
        finally
        {
            RestoreAndDelete(originalCwd, tempRoot);
        }
    }

    [Fact]
    public void PropertiesBuilder_AutoMapper_AddsAndRemovesPropertyAcrossFiles()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = CreateTempDirectory();
        try
        {
            var solution = ScaffoldSolution(tempRoot, "SampleAutoApi", Mappers.AutoMapper);
            ScaffoldEntity("Customer", "long");

            var entityPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Database", "Tables", "Customer.cs");
            var modifyModelPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Models", "CustomerModifyModel.cs");
            var readModelPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Models", "CustomerReadModel.cs");
            var configPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.EntityFramework", "EntityConfigurations", "CustomerConfig.cs");

            PropertiesBuilder.New(new[] { "g", "p", "Customer", "Title", "string", "false" }, Mappers.AutoMapper)
                .UpdateEntityModels()
                .UpdateEntity()
                .UpdateEntityConfig();

            Assert.Contains("public string Title {get; set;}", File.ReadAllText(modifyModelPath));
            Assert.Contains("public string Title {get; set;}", File.ReadAllText(readModelPath));
            Assert.Contains("public string Title {get; private set;}", File.ReadAllText(entityPath));
            Assert.Contains("Title = dto.Title;", File.ReadAllText(entityPath));
            Assert.Contains("builder.Property(e => e.Title).HasMaxLength(255);", File.ReadAllText(configPath));

            PropertiesBuilder.New(new[] { "g", "p", "Customer", "Title", "string", "false", "--remove" }, Mappers.AutoMapper)
                .UpdateEntityModels()
                .UpdateEntity()
                .UpdateEntityConfig();

            Assert.DoesNotContain("public string Title {get; set;}", File.ReadAllText(modifyModelPath));
            Assert.DoesNotContain("public string Title {get; set;}", File.ReadAllText(readModelPath));
            Assert.DoesNotContain("public string Title {get; private set;}", File.ReadAllText(entityPath));
            Assert.DoesNotContain("Title = dto.Title;", File.ReadAllText(entityPath));
            Assert.DoesNotContain("builder.Property(e => e.Title)", File.ReadAllText(configPath));
        }
        finally
        {
            RestoreAndDelete(originalCwd, tempRoot);
        }
    }

    [Fact]
    public void PropertiesBuilder_MapsterSolution_UpdatesDotGCsModels()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = CreateTempDirectory();
        try
        {
            var solution = ScaffoldSolution(tempRoot, "SampleMapApi", Mappers.Mapster);
            ScaffoldEntity("Product", "Guid", Mappers.Mapster);

            var entityPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Database", "Tables", "Product.cs");
            var modifyModelPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Models", "ProductModifyModel.g.cs");
            var readModelPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Models", "ProductReadModel.g.cs");
            var configPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.EntityFramework", "EntityConfigurations", "ProductConfig.cs");

            PropertiesBuilder.New(new[] { "g", "p", "Product", "Price", "decimal", "false" }, BuildHelper.DetectMapper())
                .UpdateEntityModels()
                .UpdateEntity()
                .UpdateEntityConfig();

            Assert.Contains("public decimal Price {get; set;}", File.ReadAllText(modifyModelPath));
            Assert.Contains("public decimal Price {get; set;}", File.ReadAllText(readModelPath));
            Assert.Contains("public decimal Price {get; private set;}", File.ReadAllText(entityPath));
            Assert.Contains("builder.Property(e => e.Price).HasColumnType(\"decimal(9,3)\");", File.ReadAllText(configPath));

            PropertiesBuilder.New(new[] { "g", "p", "Product", "Price", "decimal", "false", "--remove" }, BuildHelper.DetectMapper())
                .UpdateEntityModels()
                .UpdateEntity()
                .UpdateEntityConfig();

            Assert.DoesNotContain("public decimal Price {get; set;}", File.ReadAllText(modifyModelPath));
            Assert.DoesNotContain("public decimal Price {get; set;}", File.ReadAllText(readModelPath));
            Assert.DoesNotContain("public decimal Price {get; private set;}", File.ReadAllText(entityPath));
            Assert.DoesNotContain("Price = dto.Price;", File.ReadAllText(entityPath));
            Assert.DoesNotContain("builder.Property(e => e.Price)", File.ReadAllText(configPath));
        }
        finally
        {
            RestoreAndDelete(originalCwd, tempRoot);
        }
    }

    [Fact]
    public void RelationsBuilder_AutoMapper_AddsAndRemovesRelationAcrossParentAndChild()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = CreateTempDirectory();
        try
        {
            var solution = ScaffoldSolution(tempRoot, "SampleAutoApi", Mappers.AutoMapper);
            ScaffoldEntity("Order", "Guid");
            ScaffoldEntity("OrderItem", "Guid");

            var orderModifyPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Models", "OrderModifyModel.cs");
            var orderReadPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Models", "OrderReadModel.cs");
            var itemModifyPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Models", "OrderItemModifyModel.cs");
            var itemReadPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Models", "OrderItemReadModel.cs");
            var orderEntityPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Database", "Tables", "Order.cs");
            var itemEntityPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.Core", "Database", "Tables", "OrderItem.cs");
            var orderConfigPath = Path.Combine(solution.ProjectsPath, "SampleAutoApi.EntityFramework", "EntityConfigurations", "OrderConfig.cs");

            var orderReadBefore = File.ReadAllText(orderReadPath);

            RelationsBuilder.New(new[] { "g", "r", "Order", "OrderItem", "OrderId", "Guid" })
                .UpdateParentDtos()
                .UpdateChildDtos()
                .UpdateParentEntity()
                .UpdateChildEntity()
                .UpdateParentEntityConfig();

            Assert.Contains("List<OrderItem>", File.ReadAllText(orderModifyPath));
            Assert.Contains("OrderId", File.ReadAllText(itemModifyPath));
            Assert.Contains("OrderId", File.ReadAllText(itemReadPath));
            Assert.Contains("ICollection<OrderItem>", File.ReadAllText(orderEntityPath));
            Assert.Contains("Order? Order", File.ReadAllText(itemEntityPath));
            Assert.Contains("OrderId", File.ReadAllText(itemEntityPath));
            Assert.Contains("HasMany(e => e.OrderItems).WithOne(e => e.Order)", File.ReadAllText(orderConfigPath));
            Assert.Equal(orderReadBefore, File.ReadAllText(orderReadPath));

            RelationsBuilder.New(new[] { "g", "r", "Order", "OrderItem", "OrderId", "Guid", "--remove" })
                .UpdateParentDtos()
                .UpdateChildDtos()
                .UpdateParentEntity()
                .UpdateChildEntity()
                .UpdateParentEntityConfig();

            Assert.DoesNotContain("List<OrderItem>", File.ReadAllText(orderModifyPath));
            Assert.DoesNotContain("OrderId", File.ReadAllText(itemModifyPath));
            Assert.DoesNotContain("OrderId", File.ReadAllText(itemReadPath));
            Assert.DoesNotContain("ICollection<OrderItem>", File.ReadAllText(orderEntityPath));
            Assert.DoesNotContain("OrderItems ??= new List<OrderItem>();", File.ReadAllText(orderEntityPath));
            Assert.DoesNotContain("Order? Order", File.ReadAllText(itemEntityPath));
            Assert.DoesNotContain("OrderId", File.ReadAllText(itemEntityPath));
            Assert.DoesNotContain("HasMany(e => e.OrderItems).WithOne(e => e.Order)", File.ReadAllText(orderConfigPath));
            Assert.Equal(orderReadBefore, File.ReadAllText(orderReadPath));
        }
        finally
        {
            RestoreAndDelete(originalCwd, tempRoot);
        }
    }

    [Fact]
    public void RelationsBuilder_Mapster_AddsAndRemovesRelationOnDotGCsModels()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = CreateTempDirectory();
        try
        {
            var solution = ScaffoldSolution(tempRoot, "SampleMapApi", Mappers.Mapster);
            ScaffoldEntity("Category", "Guid", Mappers.Mapster);
            ScaffoldEntity("Product", "Guid", Mappers.Mapster);

            var categoryModifyPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Models", "CategoryModifyModel.g.cs");
            var categoryReadPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Models", "CategoryReadModel.g.cs");
            var productModifyPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Models", "ProductModifyModel.g.cs");
            var productReadPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Models", "ProductReadModel.g.cs");
            var categoryEntityPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Database", "Tables", "Category.cs");
            var productEntityPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.Core", "Database", "Tables", "Product.cs");
            var categoryConfigPath = Path.Combine(solution.ProjectsPath, "SampleMapApi.EntityFramework", "EntityConfigurations", "CategoryConfig.cs");

            var categoryReadBefore = File.ReadAllText(categoryReadPath);

            RelationsBuilder.New(new[] { "g", "r", "Category", "Product", "CategoryId", "Guid" })
                .UpdateParentDtos()
                .UpdateChildDtos()
                .UpdateParentEntity()
                .UpdateChildEntity()
                .UpdateParentEntityConfig();

            Assert.Contains("List<Product>", File.ReadAllText(categoryModifyPath));
            Assert.Contains("CategoryId", File.ReadAllText(productModifyPath));
            Assert.Contains("Category? Category", File.ReadAllText(productModifyPath));
            Assert.Contains("CategoryId", File.ReadAllText(productReadPath));
            Assert.Contains("ICollection<Product>", File.ReadAllText(categoryEntityPath));
            Assert.Contains("CategoryId", File.ReadAllText(productEntityPath));
            Assert.Contains("HasMany(e => e.Products).WithOne(e => e.Category)", File.ReadAllText(categoryConfigPath));

            RelationsBuilder.New(new[] { "g", "r", "Category", "Product", "CategoryId", "Guid", "--remove" })
                .UpdateParentDtos()
                .UpdateChildDtos()
                .UpdateParentEntity()
                .UpdateChildEntity()
                .UpdateParentEntityConfig();

            Assert.DoesNotContain("List<Product>", File.ReadAllText(categoryModifyPath));
            Assert.DoesNotContain("CategoryId", File.ReadAllText(productModifyPath));
            Assert.DoesNotContain("Category? Category", File.ReadAllText(productModifyPath));
            Assert.DoesNotContain("CategoryId", File.ReadAllText(productReadPath));
            Assert.DoesNotContain("ICollection<Product>", File.ReadAllText(categoryEntityPath));
            Assert.DoesNotContain("CategoryId", File.ReadAllText(productEntityPath));
            Assert.DoesNotContain("HasMany(e => e.Products).WithOne(e => e.Category)", File.ReadAllText(categoryConfigPath));
            Assert.Equal(categoryReadBefore, File.ReadAllText(categoryReadPath));
        }
        finally
        {
            RestoreAndDelete(originalCwd, tempRoot);
        }
    }

    [Fact]
    public void RelationsBuilder_FiveArgsWithoutFkType_DefaultsToGuid()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "Dummy.sln"), string.Empty);
            Directory.SetCurrentDirectory(tempRoot);

            var builder = RelationsBuilder.New(new[] { "g", "r", "Order", "OrderItem", "OrderId" });

            Assert.Equal(PkTypes.GUID, builder.ForeignDataType);
        }
        finally
        {
            RestoreAndDelete(originalCwd, tempRoot);
        }
    }

    [Fact]
    public void ImportFactory_DoesNotRegisterGlobalTlsBypass()
    {
#pragma warning disable SYSLIB0014
        var before = ServicePointManager.ServerCertificateValidationCallback;
        try
        {
            _ = new ImportFactory();
            var after = ServicePointManager.ServerCertificateValidationCallback;
            Assert.Same(before, after);
        }
        finally
        {
            ServicePointManager.ServerCertificateValidationCallback = before;
        }
#pragma warning restore SYSLIB0014
    }
}
