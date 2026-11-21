using Mma.Cli.Core.CodeGen;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Templates;

namespace Mma.Cli.Core.Tests;

public sealed class TemplateCompatibilityGoldenTests : IDisposable
{
    private readonly string _tempRoot;

    public TemplateCompatibilityGoldenTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-tpl-golden-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("ModifyModel")]
    [InlineData("ReadModel")]
    [InlineData("Validator")]
    [InlineData("Entity")]
    [InlineData("EntityConfig")]
    [InlineData("Config")]
    [InlineData("DbSetEntry")]
    [InlineData("ConfigEntry")]
    [InlineData("Service")]
    [InlineData("Controller")]
    public void RenderTemplate_AllAutoMapperBuiltInTemplates_MatchLegacyTokenSubstitutionByteForByte(string templateName)
    {
        var model = new TemplateModel(
            SolutionName: "Acme.Shop",
            EntityName: "OrderItem",
            EntityVarName: "orderItem",
            EntitySetName: "OrderItems",
            PkType: "long",
            Mapper: MapperKind.AutoMapper,
            Properties: [],
            Relations: []);

        var renderer = new ScribanTemplateRenderer(new EmbeddedTemplateProvider());
        var result = renderer.RenderTemplate(_tempRoot, MapperKind.AutoMapper, templateName, model);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var legacyRaw = DefaultTemplates.GetDefault(MapperKind.AutoMapper, templateName);
        var expected = ApplyLegacyTokens(legacyRaw, model);

        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("ModifyModel")]
    [InlineData("ReadModel")]
    [InlineData("Validator")]
    [InlineData("Entity")]
    [InlineData("EntityConfig")]
    [InlineData("DbSetEntry")]
    [InlineData("ConfigEntry")]
    [InlineData("Service")]
    [InlineData("Controller")]
    public void RenderTemplate_AllMapsterBuiltInTemplates_MatchLegacyTokenSubstitutionByteForByte(string templateName)
    {
        var model = new TemplateModel(
            SolutionName: "Acme.Catalog",
            EntityName: "Product",
            EntityVarName: "product",
            EntitySetName: "Products",
            PkType: "Guid",
            Mapper: MapperKind.Mapster,
            Properties: [],
            Relations: []);

        var renderer = new ScribanTemplateRenderer(new EmbeddedTemplateProvider());
        var result = renderer.RenderTemplate(_tempRoot, MapperKind.Mapster, templateName, model);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var legacyRaw = DefaultTemplates.GetDefault(MapperKind.Mapster, templateName);
        var expected = ApplyLegacyTokens(legacyRaw, model);

        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void RenderTemplate_LegacyDotMmaTemplatesCsOverride_TranslatesDollarTokens()
    {
        var overrideDir = Path.Combine(_tempRoot, ".mma", "templates", "AutoMapper");
        Directory.CreateDirectory(overrideDir);
        File.WriteAllText(
            Path.Combine(overrideDir, "Entity.cs"),
            """
            namespace $SolutionName.Core.Database.Tables
            {
                public class $EntityName : BaseEntity<$PK>
                {
                    public string SetName => "$EntitySetName";
                    public string VarName => "$EntityVarName";
                }
            }
            """);

        var model = new TemplateModel(
            "ShopApi",
            "CustomerOrder",
            "customerOrder",
            "CustomerOrders",
            "Guid",
            MapperKind.AutoMapper);

        var renderer = new ScribanTemplateRenderer(new EmbeddedTemplateProvider());
        var result = renderer.RenderTemplate(_tempRoot, MapperKind.AutoMapper, "Entity", model);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains("namespace ShopApi.Core.Database.Tables", result.Value);
        Assert.Contains("public class CustomerOrder : BaseEntity<Guid>", result.Value);
        Assert.Contains("public string SetName => \"CustomerOrders\";", result.Value);
        Assert.Contains("public string VarName => \"customerOrder\";", result.Value);
    }

    [Fact]
    public void RenderTemplate_CustomScribanOverride_IteratesPropertiesToGenerateFluentValidation()
    {
        var overrideDir = Path.Combine(_tempRoot, ".mma", "templates", "AutoMapper");
        Directory.CreateDirectory(overrideDir);

        // Ensure .scriban override takes precedence over .cs if both exist
        File.WriteAllText(Path.Combine(overrideDir, "Validator.cs"), "// legacy cs override");
        File.WriteAllText(
            Path.Combine(overrideDir, "Validator.scriban"),
            """
            using FluentValidation;
            using {{ solution_name }}.Core.Database.Tables;

            namespace {{ solution_name }}.Core.Validations
            {
                public class {{ entity_name }}Validator : AbstractValidator<{{ entity_name }}ModifyModel>
                {
                    public {{ entity_name }}Validator()
                    {
            {{- for prop in properties }}
                        RuleFor(x => x.{{ prop.Name }}){{ if prop.Required }}.NotEmpty(){{ end }}{{ if prop.MaxLength }}.MaximumLength({{ prop.MaxLength }}){{ end }};
            {{- end }}
                    }
                }
            }
            """);

        var properties = new List<ManifestProperty>
        {
            new("Title", "string", Nullable: false, IsForeignKey: false, Applied: true, MaxLength: 255, Required: true),
            new("Subtitle", "string", Nullable: true, IsForeignKey: false, Applied: true, MaxLength: 100, Required: false)
        };

        var model = new TemplateModel(
            "BlogApi",
            "Article",
            "article",
            "Articles",
            "Guid",
            MapperKind.AutoMapper,
            Properties: properties,
            Relations: []);

        var renderer = new ScribanTemplateRenderer(new EmbeddedTemplateProvider());
        var result = renderer.RenderTemplate(_tempRoot, MapperKind.AutoMapper, "Validator", model);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains("RuleFor(x => x.Title).NotEmpty().MaximumLength(255);", result.Value);
        Assert.Contains("RuleFor(x => x.Subtitle).MaximumLength(100);", result.Value);
    }

    [Fact]
    public void Render_BothSnakeCaseAndPascalCaseMembers_ResolveCleanly()
    {
        var template = """
            {{ entity_name }}|{{ EntityName }}
            {{ solution_name }}|{{ SolutionName }}
            {{ entity_var_name }}|{{ EntityVarName }}
            {{ entity_set_name }}|{{ EntitySetName }}
            {{ pk }}|{{ PK }}
            {{ mapper }}|{{ Mapper }}
            {{- for p in properties }}
            {{ p.name }}:{{ p.Name }}:{{ p.data_type }}:{{ p.DataType }}:{{ p.max_length }}:{{ p.MaxLength }}
            {{- end }}
            {{- for r in Relations }}
            {{ r.parent_entity }}:{{ r.ParentEntity }}:{{ r.foreign_key }}:{{ r.ForeignKey }}
            {{- end }}
            """;

        var model = new TemplateModel(
            "Acme.Core",
            "Invoice",
            "invoice",
            "Invoices",
            "int",
            MapperKind.Mapster,
            Properties: [new ManifestProperty("Code", "string", Nullable: false, MaxLength: 32, Required: true)],
            Relations: [new ManifestRelation("Customer_Invoice_CustomerId", "Customer", "Invoice", "CustomerId", "int")]);

        var renderer = new ScribanTemplateRenderer(new EmbeddedTemplateProvider());
        var result = renderer.Render(template, model);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains("Invoice|Invoice", result.Value);
        Assert.Contains("Acme.Core|Acme.Core", result.Value);
        Assert.Contains("invoice|invoice", result.Value);
        Assert.Contains("Invoices|Invoices", result.Value);
        Assert.Contains("int|int", result.Value);
        Assert.Contains("Mapster|Mapster", result.Value);
        Assert.Contains("Code:Code:string:string:32:32", result.Value);
        Assert.Contains("Customer:Customer:CustomerId:CustomerId", result.Value);
    }

    [Theory]
    [InlineData("{{ if true }}missing end")]
    [InlineData("{{ 1 + }}")]
    [InlineData("{{ for x in }}")]
    public void Render_MalformedScribanSyntax_ReturnsTemplateError(string badTemplate)
    {
        var model = new TemplateModel("App", "Item", "item", "Items", "Guid", MapperKind.AutoMapper);
        var renderer = new ScribanTemplateRenderer(new EmbeddedTemplateProvider());

        var result = renderer.Render(badTemplate, model);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.TEMPLATE_ERROR, result.Error?.Code);
    }

    [Theory]
    [InlineData("{{ 1 // 0 }}")]
    [InlineData("{{ unknown.foo() }}")]
    public void Render_RuntimeError_ReturnsTemplateError(string failingTemplate)
    {
        var model = new TemplateModel("App", "Item", "item", "Items", "Guid", MapperKind.AutoMapper);
        var renderer = new ScribanTemplateRenderer(new EmbeddedTemplateProvider());

        var result = renderer.Render(failingTemplate, model);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.TEMPLATE_ERROR, result.Error?.Code);
    }

    [Fact]
    public void RenderTemplate_LowercaseMapperOverrideDirectory_LoadsCleanly()
    {
        var overrideDir = Path.Combine(_tempRoot, ".mma", "templates", "automapper");
        Directory.CreateDirectory(overrideDir);
        File.WriteAllText(Path.Combine(overrideDir, "DbSetEntry.cs"), "public virtual DbSet<$EntityName> $EntitySetName { get; set; }");

        var model = new TemplateModel("App", "Widget", "widget", "Widgets", "Guid", MapperKind.AutoMapper);
        var renderer = new ScribanTemplateRenderer(new EmbeddedTemplateProvider());

        var result = renderer.RenderTemplate(_tempRoot, MapperKind.AutoMapper, "DbSetEntry", model);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("public virtual DbSet<Widget> Widgets { get; set; }", result.Value);
    }

    [Fact]
    public void Render_MixedLegacyDollarTokensAndScribanTags_DoesNotBreakScribanTags()
    {
        var mixed = "// $SolutionName\npublic class {{ entity_name }} { public $PK Id { get; set; } }";
        var model = new TemplateModel("MixApp", "Widget", "widget", "Widgets", "long", MapperKind.AutoMapper);
        var renderer = new ScribanTemplateRenderer(new EmbeddedTemplateProvider());

        var result = renderer.Render(mixed, model);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("// MixApp\npublic class Widget { public long Id { get; set; } }", result.Value);
    }

    private static string ApplyLegacyTokens(string template, TemplateModel model) =>
        template
            .Replace("$SolutionName", model.SolutionName, StringComparison.Ordinal)
            .Replace("$EntityName", model.EntityName, StringComparison.Ordinal)
            .Replace("$EntityVarName", model.EntityVarName, StringComparison.Ordinal)
            .Replace("$EntitySetName", model.EntitySetName, StringComparison.Ordinal)
            .Replace("$PK", model.PkType, StringComparison.Ordinal);
}
