using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mma.Cli.Core.CodeGen;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Roslyn;
using Mma.Cli.Core.Services;
using Mma.Cli.Core.Solutions;
using Mma.Cli.Core.Templates;

namespace Mma.Cli.Core.Tests;

public sealed class RoslynRewriterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SolutionLocator _locator = new();
    private readonly EmbeddedTemplateProvider _templates = new();

    public RoslynRewriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-roslyn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static void AssertZeroSyntaxErrors(string sourceText)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    [Fact]
    public void ModelSyntaxRewriter_AutoMapper_PreservesCommentsAttributesAndCustomMethods_AndIsIdempotent()
    {
        const string source = """
            using System;
            using System.ComponentModel.DataAnnotations;

            namespace Shop.Core.Database.Tables
            {
                /// <summary>Order modify DTO.</summary>
                public partial class OrderModifyModel
                {
                    public Guid Id { get;  set; }

                    // Preserve this annotation and comment
                    [Required]
                    [MaxLength(64)]
                    public string Reference { get; set; } = string.Empty;

                    // Custom computed helper after properties
                    public string DisplayLabel() => $"{Id}:{Reference}";
                }
            }
            """;

        var rewriter = new ModelSyntaxRewriter();
        Assert.True(rewriter.HasProperty(source, "Reference"));
        Assert.False(rewriter.HasProperty(source, "Title"));

        var addResult = rewriter.AddProperty(source, "Title", "string", isNullable: false);
        Assert.True(addResult.IsSuccess, addResult.Error?.Message);
        var added = addResult.Value!;
        AssertZeroSyntaxErrors(added);

        Assert.Contains("public string Title {get; set;}", added, StringComparison.Ordinal);
        Assert.Contains("/// <summary>Order modify DTO.</summary>", added, StringComparison.Ordinal);
        Assert.Contains("// Preserve this annotation and comment", added, StringComparison.Ordinal);
        Assert.Contains("[Required]", added, StringComparison.Ordinal);
        Assert.Contains("// Custom computed helper after properties", added, StringComparison.Ordinal);

        var titleIndex = added.IndexOf("public string Title {get; set;}", StringComparison.Ordinal);
        var helperIndex = added.IndexOf("// Custom computed helper after properties", StringComparison.Ordinal);
        Assert.True(titleIndex > 0 && titleIndex < helperIndex, "New property must be inserted before custom helper methods.");

        // Idempotent on second call
        var secondAdd = rewriter.AddProperty(added, "Title", "string", isNullable: false);
        Assert.True(secondAdd.IsSuccess, secondAdd.Error?.Message);
        Assert.Equal(added, secondAdd.Value);

        // Remove cleanly while preserving custom annotations, comments, and helper methods
        var removeResult = rewriter.RemoveProperty(added, "Title");
        Assert.True(removeResult.IsSuccess, removeResult.Error?.Message);
        var removed = removeResult.Value!;
        AssertZeroSyntaxErrors(removed);
        Assert.DoesNotContain("Title", removed, StringComparison.Ordinal);
        Assert.Contains("public string Reference { get; set; }", removed, StringComparison.Ordinal);
        Assert.Contains("public string DisplayLabel()", removed, StringComparison.Ordinal);
        Assert.Contains("// Preserve this annotation and comment", removed, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSyntaxRewriter_MapsterDto_SupportsBeforePropertyVirtualAndRequireVirtualRemoval()
    {
        const string source = """
            using System;
            using System.Collections.Generic;

            namespace Catalog.Core.Database.Tables
            {
                public partial class OrderDto
                {
                    public Guid Id { get;  set; }
                    public string Code { get; set; } = string.Empty;
                    public DateTime? DeletedDate { get;  set; }
                }
            }
            """;

        var rewriter = new ModelSyntaxRewriter();
        var addNav = rewriter.AddProperty(source, "OrderItems", "List<OrderItem>", isNullable: true, isVirtual: true, beforePropertyName: "DeletedDate");
        Assert.True(addNav.IsSuccess, addNav.Error?.Message);
        var updated = addNav.Value!;
        AssertZeroSyntaxErrors(updated);

        var navIndex = updated.IndexOf("public virtual List<OrderItem>? OrderItems {get; set;}", StringComparison.Ordinal);
        var deletedIndex = updated.IndexOf("DeletedDate", StringComparison.Ordinal);
        Assert.True(navIndex > 0 && navIndex < deletedIndex);

        // requireVirtual: true must not remove non-virtual Code property
        var skipNonVirtual = rewriter.RemoveProperty(updated, "Code", requireVirtual: true);
        Assert.True(skipNonVirtual.IsSuccess);
        Assert.Equal(updated, skipNonVirtual.Value);

        // requireVirtual: true removes virtual navigation property
        var removeVirtual = rewriter.RemoveProperty(updated, "OrderItems", requireVirtual: true);
        Assert.True(removeVirtual.IsSuccess, removeVirtual.Error?.Message);
        AssertZeroSyntaxErrors(removeVirtual.Value!);
        Assert.DoesNotContain("OrderItems", removeVirtual.Value!, StringComparison.Ordinal);
        Assert.Contains("public string Code", removeVirtual.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSyntaxRewriter_MalformedCSharp_ReturnsTemplateError()
    {
        var rewriter = new ModelSyntaxRewriter();
        var result = rewriter.AddProperty("public class Broken { public string ", "Title", "string");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.TEMPLATE_ERROR, result.Error?.Code);
    }

    [Fact]
    public void EntitySyntaxRewriter_HandlesCustomMethodsExtraConstructorLinesModelParamAndIdempotency()
    {
        const string source = """
            using System;
            using System.Collections.Generic;

            namespace Shop.Core.Database.Tables
            {
                /// <summary>Domain order entity.</summary>
                public class Order : BaseEntity<Guid>
                {
                    // Existing property
                    public string Code { get; private set; }

                    OrderValidator _Validator;
                    private OrderValidator Validator => _Validator ??= new OrderValidator();

                    // Reordered helper before constructors
                    public bool IsHighValue() => true;

                    public Order(OrderModifyModel dto)
                    {
                        var r = Validator.Validate(dto);
                        if (!r.IsValid)
                        {
                            throw new InvalidOperationException("bad");
                        }
                        // Line 1
                        // Line 2
                        // Line 3
                        // Line 4
                        // Line 5
                        // Line 6
                        // Line 7
                        // Line 8
                        // Line 9
                        // Line 10
                        // Line 11
                        // Line 12
                        Code = dto.Code;
                        CreatedDate = DateTime.UtcNow;
                    }

                    public Order Update(OrderModifyModel model)
                    {
                        var r = Validator.Validate(model);
                        if (!r.IsValid)
                        {
                            throw new InvalidOperationException("bad");
                        }
                        // Extra custom domain check spanning many lines
                        if (string.IsNullOrWhiteSpace(model.Code))
                        {
                            throw new ArgumentException("code");
                        }

                        Code = model.Code;
                        ModifiedDate = DateTime.UtcNow;
                        return this;
                    }
                }
            }
            """;

        var rewriter = new EntitySyntaxRewriter();
        Assert.True(rewriter.HasProperty(source, "Code"));
        Assert.False(rewriter.HasProperty(source, "Title"));

        var withProp = rewriter.AddProperty(source, "Order", "Title", "string", isNullable: false, isVirtual: false, isPrivateSetter: true);
        Assert.True(withProp.IsSuccess, withProp.Error?.Message);
        AssertZeroSyntaxErrors(withProp.Value!);

        var titleIdx = withProp.Value!.IndexOf("public string Title {get; private set;}", StringComparison.Ordinal);
        var validatorIdx = withProp.Value!.IndexOf("OrderValidator _Validator;", StringComparison.Ordinal);
        Assert.True(titleIdx > 0 && titleIdx < validatorIdx);

        // Idempotent property add
        var dupProp = rewriter.AddProperty(withProp.Value!, "Order", "Title", "string");
        Assert.True(dupProp.IsSuccess);
        Assert.Equal(withProp.Value, dupProp.Value);

        // Assignment insertion regardless of +10 line offset
        var withAssign = rewriter.AddConstructorAndUpdateAssignment(withProp.Value!, "Order", "Title = dto.Title;");
        Assert.True(withAssign.IsSuccess, withAssign.Error?.Message);
        var updated = withAssign.Value!;
        AssertZeroSyntaxErrors(updated);

        var ctorAssignIdx = updated.IndexOf("Title = dto.Title;", StringComparison.Ordinal);
        var createdIdx = updated.IndexOf("CreatedDate = DateTime.UtcNow;", StringComparison.Ordinal);
        var updateAssignIdx = updated.LastIndexOf("Title = dto.Title;", StringComparison.Ordinal);
        var modifiedIdx = updated.IndexOf("ModifiedDate = DateTime.UtcNow;", StringComparison.Ordinal);

        Assert.True(ctorAssignIdx > 0 && ctorAssignIdx < createdIdx);
        Assert.True(updateAssignIdx > ctorAssignIdx && updateAssignIdx < modifiedIdx);
        Assert.Contains("// Extra custom domain check spanning many lines", updated, StringComparison.Ordinal);
        Assert.Contains("public bool IsHighValue() => true;", updated, StringComparison.Ordinal);

        // Idempotent assignment insertion (even with different spacing)
        var dupAssign = rewriter.AddConstructorAndUpdateAssignment(updated, "Order", "Title=dto.Title;");
        Assert.True(dupAssign.IsSuccess);
        Assert.Equal(updated, dupAssign.Value);

        // Remove property and assignments cleanly
        var removedResult = rewriter.RemovePropertyAndAssignments(updated, "Title");
        Assert.True(removedResult.IsSuccess, removedResult.Error?.Message);
        var cleaned = removedResult.Value!;
        AssertZeroSyntaxErrors(cleaned);
        Assert.DoesNotContain("public string Title", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("Title = dto.Title;", cleaned, StringComparison.Ordinal);
        Assert.Contains("Code = model.Code;", cleaned, StringComparison.Ordinal);
        Assert.Contains("CreatedDate = DateTime.UtcNow;", cleaned, StringComparison.Ordinal);
        Assert.Contains("ModifiedDate = DateTime.UtcNow;", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void EntitySyntaxRewriter_RemovePropertyAndAssignments_RemovesModelAndThisAndCoalesceAssignments()
    {
        const string source = """
            using System;
            using System.Collections.Generic;

            namespace Shop.Core.Database.Tables
            {
                public class Category : BaseEntity<Guid>
                {
                    public string? Title { get; private set; }
                    public virtual ICollection<Product>? Products { get; set; }

                    public Category(CategoryModifyModel model)
                    {
                        this.Title = model.Title;
                        Products ??= new List<Product>();
                        CreatedDate = DateTime.UtcNow;
                    }

                    public Category Update(CategoryModifyModel model)
                    {
                        Title = model.Title;
                        Products ??= new List<Product>();
                        return this;
                    }
                }
            }
            """;

        var rewriter = new EntitySyntaxRewriter();
        var noTitle = rewriter.RemovePropertyAndAssignments(source, "Title");
        Assert.True(noTitle.IsSuccess, noTitle.Error?.Message);
        AssertZeroSyntaxErrors(noTitle.Value!);
        Assert.DoesNotContain("Title", noTitle.Value!, StringComparison.Ordinal);
        Assert.Contains("Products ??= new List<Product>();", noTitle.Value!, StringComparison.Ordinal);

        var noProducts = rewriter.RemovePropertyAndAssignments(noTitle.Value!, "Products");
        Assert.True(noProducts.IsSuccess, noProducts.Error?.Message);
        AssertZeroSyntaxErrors(noProducts.Value!);
        Assert.DoesNotContain("Products", noProducts.Value!, StringComparison.Ordinal);
        Assert.Contains("CreatedDate = DateTime.UtcNow;", noProducts.Value!, StringComparison.Ordinal);
        Assert.Contains("return this;", noProducts.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public void EntitySyntaxRewriter_RefactoredEntityWithoutValidatorField_AddsPropertyBeforeConstructor()
    {
        const string source = """
            namespace Shop.Core.Database.Tables
            {
                public class RefactoredEntity : BaseEntity<Guid>
                {
                    public RefactoredEntity(RefactoredEntityModifyModel model)
                    {
                        CreatedDate = DateTime.UtcNow;
                    }

                    public RefactoredEntity Update(RefactoredEntityModifyModel model)
                    {
                        return this;
                    }
                }
            }
            """;

        var rewriter = new EntitySyntaxRewriter();
        var withProp = rewriter.AddProperty(source, "RefactoredEntity", "Note", "string", isNullable: true);
        Assert.True(withProp.IsSuccess, withProp.Error?.Message);
        AssertZeroSyntaxErrors(withProp.Value!);
        Assert.Contains("public string? Note {get; private set;}", withProp.Value!, StringComparison.Ordinal);

        var withAssign = rewriter.AddConstructorAndUpdateAssignment(withProp.Value!, "RefactoredEntity", "Note = dto.Note;");
        Assert.True(withAssign.IsSuccess, withAssign.Error?.Message);
        AssertZeroSyntaxErrors(withAssign.Value!);

        var updateAssignIdx = withAssign.Value!.LastIndexOf("Note = dto.Note;", StringComparison.Ordinal);
        var returnIdx = withAssign.Value!.IndexOf("return this;", StringComparison.Ordinal);
        Assert.True(updateAssignIdx > 0 && updateAssignIdx < returnIdx);
    }

    [Fact]
    public void EntityConfigSyntaxRewriter_PreservesMultilineFluentChainsAndRemovesFormattedPropertyAndRelation()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            namespace Shop.EntityFramework.EntityConfigurations
            {
                public class CategoryConfig : IEntityTypeConfiguration<Category>
                {
                    public void Configure(EntityTypeBuilder<Category> builder)
                    {
                        builder.ToTable("Categories", "dbo");

                        builder.Property(e => e.Title)
                            .IsRequired()
                            .HasMaxLength(255);

                        builder.HasIndex(e => e.IsDeleted);
                        builder.Property(e => e.DeletedDate)
                            .HasColumnType("datetime");

                        builder.HasMany(x => x.Products)
                            .WithOne(p => p.Category)
                            .HasForeignKey(p => p.CategoryId)
                            .OnDelete(DeleteBehavior.Restrict);
                    }
                }
            }
            """;

        var rewriter = new EntityConfigSyntaxRewriter();

        Assert.True(rewriter.HasRelationConfiguration(source, "Products", "Category", "CategoryId"));
        Assert.False(rewriter.HasRelationConfiguration(source, "Items", "Category", "CategoryId"));

        // Add statement preserves multi-line DeletedDate and is idempotent across formatting
        var addResult = rewriter.AddConfigurationStatement(source, "builder.Property(e => e.Code).HasMaxLength(32);");
        Assert.True(addResult.IsSuccess, addResult.Error?.Message);
        var added = addResult.Value!;
        AssertZeroSyntaxErrors(added);
        Assert.Contains("builder.Property(e => e.DeletedDate)\n                .HasColumnType(\"datetime\");", added.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("builder.Property(e => e.Code).HasMaxLength(32);", added, StringComparison.Ordinal);

        var dupAdd = rewriter.AddConfigurationStatement(added, "builder.Property(e => e.Code)\n    .HasMaxLength(32);");
        Assert.True(dupAdd.IsSuccess);
        Assert.Equal(added, dupAdd.Value);

        // Remove multi-line Title property configuration cleanly
        var withoutTitle = rewriter.RemovePropertyConfiguration(added, "Title");
        Assert.True(withoutTitle.IsSuccess, withoutTitle.Error?.Message);
        AssertZeroSyntaxErrors(withoutTitle.Value!);
        Assert.DoesNotContain("e.Title", withoutTitle.Value!, StringComparison.Ordinal);
        Assert.DoesNotContain("HasMaxLength(255)", withoutTitle.Value!, StringComparison.Ordinal);
        Assert.Contains("e.DeletedDate", withoutTitle.Value!, StringComparison.Ordinal);
        Assert.Contains("e.Code", withoutTitle.Value!, StringComparison.Ordinal);

        // Remove multi-line relation with DeleteBehavior.Restrict and custom lambda params
        var withoutRelation = rewriter.RemoveRelationConfiguration(withoutTitle.Value!, "Products", "Category", "CategoryId");
        Assert.True(withoutRelation.IsSuccess, withoutRelation.Error?.Message);
        AssertZeroSyntaxErrors(withoutRelation.Value!);
        Assert.False(rewriter.HasRelationConfiguration(withoutRelation.Value!, "Products", "Category", "CategoryId"));
        Assert.DoesNotContain("HasMany", withoutRelation.Value!, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteBehavior.Restrict", withoutRelation.Value!, StringComparison.Ordinal);
        Assert.Contains("builder.HasIndex(e => e.IsDeleted);", withoutRelation.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public void EntityConfigSyntaxRewriter_MissingConfigureMethod_ReturnsTemplateError()
    {
        const string source = """
            namespace Shop.EntityFramework.EntityConfigurations
            {
                public class EmptyConfig { }
            }
            """;

        var rewriter = new EntityConfigSyntaxRewriter();
        var result = rewriter.AddConfigurationStatement(source, "builder.ToTable(\"X\");");
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.TEMPLATE_ERROR, result.Error?.Code);
    }

    [Fact]
    public void DbContextSyntaxRewriter_DbContext_PreservesHelperMethodsAndCustomParamAndIsIdempotent()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;

            namespace Shop.EntityFramework
            {
                public class ApplicationDbContext : DbContext
                {
                    public virtual DbSet<SysSetting> SysSettings { get; set; }

                    protected override void OnModelCreating(ModelBuilder builder)
                    {
                        base.OnModelCreating(builder);
                        builder.ApplyConfiguration(new SysSettingConfig());
                    }

                    // Helper method after OnModelCreating with semicolons
                    public int CountSettings()
                    {
                        var x = 1;
                        return x;
                    }
                }
            }
            """;

        var rewriter = new DbContextSyntaxRewriter();
        var first = rewriter.RegisterEntityInDbContext(
            source,
            "Order",
            "Orders",
            "            public virtual DbSet<Order> Orders { get; set; }",
            "            modelBuilder.ApplyConfiguration(new OrderConfig());");

        Assert.True(first.IsSuccess, first.Error?.Message);
        var updated = first.Value!;
        AssertZeroSyntaxErrors(updated);

        Assert.Contains("public virtual DbSet<Order> Orders { get; set; }", updated, StringComparison.Ordinal);
        // Should adapt modelBuilder -> builder when OnModelCreating parameter is named builder
        Assert.Contains("builder.ApplyConfiguration(new OrderConfig());", updated, StringComparison.Ordinal);

        var onModelIdx = updated.IndexOf("protected override void OnModelCreating", StringComparison.Ordinal);
        var dbSetIdx = updated.IndexOf("DbSet<Order>", StringComparison.Ordinal);
        var helperIdx = updated.IndexOf("public int CountSettings()", StringComparison.Ordinal);
        var applyIdx = updated.IndexOf("builder.ApplyConfiguration(new OrderConfig());", StringComparison.Ordinal);

        Assert.True(dbSetIdx > 0 && dbSetIdx < onModelIdx);
        Assert.True(applyIdx > onModelIdx && applyIdx < helperIdx);

        // Idempotent when called twice
        var second = rewriter.RegisterEntityInDbContext(
            updated,
            "Order",
            "Orders",
            "public virtual DbSet<Order> Orders { get; set; }",
            "modelBuilder.ApplyConfiguration(new OrderConfig());");
        Assert.True(second.IsSuccess, second.Error?.Message);
        Assert.Equal(updated, second.Value);
    }

    [Fact]
    public void DbContextSyntaxRewriter_MappingProfile_WithHelperMethodsContainingSemicolonsBelowConstructor_InsertsInsideConstructor()
    {
        const string source = """
            using AutoMapper;

            namespace Shop.Core
            {
                public class MappingProfile : Profile
                {
                    public MappingProfile()
                    {
                        CreateMap<SysSetting, SysSettingReadModel>();
                    }

                    // Legacy FindLastIndex(l => l.EndsWith(";")) falsely inserted after this helper!
                    private static string HelperWithSemicolons()
                    {
                        var text = "keep-inside-helper";
                        return text;
                    }
                }
            }
            """;

        var rewriter = new DbContextSyntaxRewriter();
        var first = rewriter.RegisterEntityInMappingProfile(source, "Order");
        Assert.True(first.IsSuccess, first.Error?.Message);
        var updated = first.Value!;
        AssertZeroSyntaxErrors(updated);

        var readMapIdx = updated.IndexOf("CreateMap<Order, OrderReadModel>()", StringComparison.Ordinal);
        var modifyMapIdx = updated.IndexOf("CreateMap<Order, OrderModifyModel>()", StringComparison.Ordinal);
        var helperIdx = updated.IndexOf("private static string HelperWithSemicolons()", StringComparison.Ordinal);

        Assert.True(readMapIdx > 0 && readMapIdx < helperIdx);
        Assert.True(modifyMapIdx > readMapIdx && modifyMapIdx < helperIdx);

        // Idempotent when called twice
        var second = rewriter.RegisterEntityInMappingProfile(updated, "Order");
        Assert.True(second.IsSuccess, second.Error?.Message);
        Assert.Equal(updated, second.Value);
    }

    [Fact]
    public async Task PropertyService_And_RelationService_EndToEnd_DryRunAndCommit_PopulatePlanAndPreserveUserFormatting()
    {
        var slnSvc = new SolutionService();
        Assert.True((await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "RoslynE2EApi", MapperKind.AutoMapper))).IsSuccess);
        var slnDir = Path.Combine(_tempRoot, "RoslynE2EApi");

        var entitySvc = new EntityService(_locator, _templates);
        Assert.True((await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Category", "Guid"))).IsSuccess);
        Assert.True((await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Product", "Guid"))).IsSuccess);

        var projectsDir = Path.Combine(slnDir, "RoslynE2EApi");
        var productEntityPath = Path.Combine(projectsDir, "RoslynE2EApi.Core", "Database", "Tables", "Product.cs");
        var productModifyPath = Path.Combine(projectsDir, "RoslynE2EApi.Core", "Models", "ProductModifyModel.cs");
        var productReadPath = Path.Combine(projectsDir, "RoslynE2EApi.Core", "Models", "ProductReadModel.cs");
        var productConfigPath = Path.Combine(projectsDir, "RoslynE2EApi.EntityFramework", "EntityConfigurations", "ProductConfig.cs");
        var categoryConfigPath = Path.Combine(projectsDir, "RoslynE2EApi.EntityFramework", "EntityConfigurations", "CategoryConfig.cs");

        // Customize Product.cs with comments and >15 extra lines in constructor/Update, plus helper method in ModifyModel
        var productEntityText = await File.ReadAllTextAsync(productEntityPath);
        productEntityText = productEntityText.Replace(
            "CreatedDate = DateTime.UtcNow;",
            "// line1\n            // line2\n            // line3\n            // line4\n            // line5\n            // line6\n            CreatedDate = DateTime.UtcNow;",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(productEntityPath, productEntityText);

        var modifyText = await File.ReadAllTextAsync(productModifyPath);
        modifyText = modifyText.Replace(
            "public DateTime? DeletedDate { get;  set; }",
            "public DateTime? DeletedDate { get;  set; }\n        // Custom helper below properties\n        public string Describe() => nameof(ProductModifyModel);",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(productModifyPath, modifyText);

        var propSvc = new PropertyService(_locator);
        var dryProp = await propSvc.ApplyAsync(new AddPropertyRequest(slnDir, "Product", "Name", "string", DryRun: true));
        Assert.True(dryProp.IsSuccess, dryProp.Error?.Message);
        Assert.True(dryProp.Value!.DryRun);
        Assert.NotNull(dryProp.Value.Plan);
        Assert.Equal(4, dryProp.Value.Plan.FileChanges.Count);
        Assert.All(dryProp.Value.Plan.FileChanges, diff => Assert.False(string.IsNullOrWhiteSpace(diff.UnifiedDiff)));
        Assert.DoesNotContain("public string Name", await File.ReadAllTextAsync(productEntityPath), StringComparison.Ordinal);

        var applyProp = await propSvc.ApplyAsync(new AddPropertyRequest(slnDir, "Product", "Name", "string", DryRun: false));
        Assert.True(applyProp.IsSuccess, applyProp.Error?.Message);
        Assert.NotNull(applyProp.Value!.Plan);
        Assert.Equal(dryProp.Value.Plan.PlanHash, applyProp.Value.Plan.PlanHash);

        var updatedProductEntity = await File.ReadAllTextAsync(productEntityPath);
        var updatedModify = await File.ReadAllTextAsync(productModifyPath);
        var updatedRead = await File.ReadAllTextAsync(productReadPath);
        var updatedProductConfig = await File.ReadAllTextAsync(productConfigPath);

        AssertZeroSyntaxErrors(updatedProductEntity);
        AssertZeroSyntaxErrors(updatedModify);
        AssertZeroSyntaxErrors(updatedRead);
        AssertZeroSyntaxErrors(updatedProductConfig);

        Assert.Contains("// line6", updatedProductEntity, StringComparison.Ordinal);
        Assert.True(
            updatedProductEntity.IndexOf("Name = dto.Name;", StringComparison.Ordinal) <
            updatedProductEntity.IndexOf("CreatedDate = DateTime.UtcNow;", StringComparison.Ordinal));
        Assert.True(
            updatedModify.IndexOf("public string Name {get; set;}", StringComparison.Ordinal) <
            updatedModify.IndexOf("// Custom helper below properties", StringComparison.Ordinal));

        // RelationService DryRun + Commit + Plan verification
        var relSvc = new RelationService(_locator);
        var dryRel = await relSvc.ApplyAsync(new AddRelationRequest(slnDir, "Category", "Product", "CategoryId", "Guid", DryRun: true));
        Assert.True(dryRel.IsSuccess, dryRel.Error?.Message);
        Assert.True(dryRel.Value!.DryRun);
        Assert.NotNull(dryRel.Value.Plan);
        Assert.Equal(6, dryRel.Value.Plan.FileChanges.Count);
        Assert.All(dryRel.Value.Plan.FileChanges, diff => Assert.False(string.IsNullOrWhiteSpace(diff.UnifiedDiff)));
        Assert.DoesNotContain("HasMany", await File.ReadAllTextAsync(categoryConfigPath), StringComparison.Ordinal);

        var applyRel = await relSvc.ApplyAsync(new AddRelationRequest(slnDir, "Category", "Product", "CategoryId", "Guid", DryRun: false));
        Assert.True(applyRel.IsSuccess, applyRel.Error?.Message);
        Assert.NotNull(applyRel.Value!.Plan);
        Assert.Equal(dryRel.Value.Plan.PlanHash, applyRel.Value.Plan.PlanHash);

        foreach (var path in applyRel.Value.ModifiedFiles)
        {
            AssertZeroSyntaxErrors(await File.ReadAllTextAsync(path));
        }

        // Remove relation & property and verify zero syntax errors and preserved custom code
        var removeRel = await relSvc.ApplyAsync(new AddRelationRequest(slnDir, "Category", "Product", "CategoryId", "Guid", Remove: true));
        Assert.True(removeRel.IsSuccess, removeRel.Error?.Message);
        Assert.NotNull(removeRel.Value!.Plan);
        foreach (var path in removeRel.Value.ModifiedFiles)
        {
            AssertZeroSyntaxErrors(await File.ReadAllTextAsync(path));
        }

        var removeProp = await propSvc.ApplyAsync(new AddPropertyRequest(slnDir, "Product", "Name", "string", Remove: true));
        Assert.True(removeProp.IsSuccess, removeProp.Error?.Message);
        Assert.NotNull(removeProp.Value!.Plan);
        foreach (var path in removeProp.Value.ModifiedFiles)
        {
            AssertZeroSyntaxErrors(await File.ReadAllTextAsync(path));
        }

        Assert.Contains("public string Describe()", await File.ReadAllTextAsync(productModifyPath), StringComparison.Ordinal);
        Assert.Contains("// line6", await File.ReadAllTextAsync(productEntityPath), StringComparison.Ordinal);

        var loaded = await new ManifestStore().LoadAsync(slnDir);
        Assert.True(loaded.IsSuccess);
        Assert.Empty(loaded.Value!.Manifest.Relations);
        var productManifest = Assert.Single(loaded.Value.Manifest.Entities, e => e.Name == "Product");
        Assert.DoesNotContain(productManifest.Properties, p => p.Name == "Name");
    }

    [Fact]
    public async Task EntityService_WithMappingProfileHelperSemicolonsAndDbContextHelper_InsertsIntoConstructorAndOnModelCreating()
    {
        var slnSvc = new SolutionService();
        Assert.True((await slnSvc.CreateAsync(new CreateSolutionRequest(_tempRoot, "CustomProfileApi", MapperKind.AutoMapper))).IsSuccess);
        var slnDir = Path.Combine(_tempRoot, "CustomProfileApi");
        var projectsDir = Path.Combine(slnDir, "CustomProfileApi");

        var mappingProfilePath = Path.Combine(projectsDir, "CustomProfileApi.Core", "MappingProfile.cs");
        var profileText = await File.ReadAllTextAsync(mappingProfilePath);
        profileText = profileText.Replace(
            "        }\n    }",
            "        }\n\n        private static int LegacyBugTrap()\n        {\n            var value = 42;\n            return value;\n        }\n    }",
            StringComparison.Ordinal);
        if (!profileText.Contains("LegacyBugTrap", StringComparison.Ordinal))
        {
            profileText = profileText.Replace(
                "        }\r\n    }",
                "        }\r\n\r\n        private static int LegacyBugTrap()\r\n        {\r\n            var value = 42;\r\n            return value;\r\n        }\r\n    }",
                StringComparison.Ordinal);
        }

        await File.WriteAllTextAsync(mappingProfilePath, profileText);

        var dbContextPath = Path.Combine(projectsDir, "CustomProfileApi.EntityFramework", "ApplicationDbContext.cs");
        var dbContextText = await File.ReadAllTextAsync(dbContextPath);
        dbContextText = dbContextText.Replace(
            "OnModelCreating(ModelBuilder modelBuilder)",
            "OnModelCreating(ModelBuilder builder)",
            StringComparison.Ordinal).Replace(
            "modelBuilder.",
            "builder.",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(dbContextPath, dbContextText);

        var entitySvc = new EntityService(_locator, _templates);
        var result = await entitySvc.GenerateAsync(new AddEntityRequest(slnDir, "Invoice", "Guid"));
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.NotNull(result.Value!.Plan);

        var updatedProfile = await File.ReadAllTextAsync(mappingProfilePath);
        AssertZeroSyntaxErrors(updatedProfile);
        var mapIdx = updatedProfile.IndexOf("CreateMap<Invoice, InvoiceReadModel>()", StringComparison.Ordinal);
        var helperIdx = updatedProfile.IndexOf("LegacyBugTrap", StringComparison.Ordinal);
        Assert.True(mapIdx > 0 && mapIdx < helperIdx);

        var updatedDbContext = await File.ReadAllTextAsync(dbContextPath);
        AssertZeroSyntaxErrors(updatedDbContext);
        Assert.Contains("builder.ApplyConfiguration(new InvoiceConfig());", updatedDbContext, StringComparison.Ordinal);
        Assert.Contains("public virtual DbSet<Invoice> Invoices { get; set; }", updatedDbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void RoslynSyntaxHelper_PreservesDirectivesAndSectionCommentsAndTabIndentationOnRemoval()
    {
        const string source = """
            namespace Shop.Core.Models
            {
            	public partial class TabbedModel
            	{
            		#region Generated
            		public Guid Id { get; set; }

            		// Section: Keep this note

            		public string Title { get; set; }
            		#endregion
            	}
            }
            """;

        var rewriter = new ModelSyntaxRewriter();
        var added = rewriter.AddProperty(source, "Subtitle", "string", isNullable: true);
        Assert.True(added.IsSuccess, added.Error?.Message);
        AssertZeroSyntaxErrors(added.Value!);
        Assert.Contains("\t\tpublic string? Subtitle {get; set;}", added.Value!, StringComparison.Ordinal);

        var removed = rewriter.RemoveProperty(added.Value!, "Title");
        Assert.True(removed.IsSuccess, removed.Error?.Message);
        AssertZeroSyntaxErrors(removed.Value!);
        Assert.DoesNotContain("Title", removed.Value!, StringComparison.Ordinal);
        Assert.Contains("// Section: Keep this note", removed.Value!, StringComparison.Ordinal);
        Assert.Contains("#region Generated", removed.Value!, StringComparison.Ordinal);
        Assert.Contains("#endregion", removed.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public void EntityConfigSyntaxRewriter_RemovesParenthesizedLambdaPropertyAndHandlesEmptyDbContextFallback()
    {
        const string configSource = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            namespace Shop.EntityFramework.EntityConfigurations
            {
                public class ItemConfig : IEntityTypeConfiguration<Item>
                {
                    public void Configure(EntityTypeBuilder<Item> builder)
                    {
                        builder.Property((x) => x.Title)
                            .HasMaxLength(128);
                        builder.HasIndex(e => e.IsDeleted);
                    }
                }
            }
            """;

        var cfgRewriter = new EntityConfigSyntaxRewriter();
        var removedProp = cfgRewriter.RemovePropertyConfiguration(configSource, "Title");
        Assert.True(removedProp.IsSuccess, removedProp.Error?.Message);
        AssertZeroSyntaxErrors(removedProp.Value!);
        Assert.DoesNotContain("x.Title", removedProp.Value!, StringComparison.Ordinal);
        Assert.Contains("builder.HasIndex(e => e.IsDeleted);", removedProp.Value!, StringComparison.Ordinal);

        const string emptyDbContext = """
            using Microsoft.EntityFrameworkCore;

            namespace Shop.EntityFramework
            {
                public class ApplicationDbContext : DbContext
                {
                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        base.OnModelCreating(modelBuilder);
                    }
                }
            }
            """;

        var dbRewriter = new DbContextSyntaxRewriter();
        var registered = dbRewriter.RegisterEntityInDbContext(emptyDbContext, "Item", "Items");
        Assert.True(registered.IsSuccess, registered.Error?.Message);
        AssertZeroSyntaxErrors(registered.Value!);
        Assert.Contains("public virtual DbSet<Item> Items { get; set; }", registered.Value!, StringComparison.Ordinal);
        Assert.Contains("modelBuilder.ApplyConfiguration(new ItemConfig());", registered.Value!, StringComparison.Ordinal);
    }
}
