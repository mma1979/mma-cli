using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Mma.Cli.Bff.CodeGen;
using Mma.Cli.Bff.Projects;
using Mma.Cli.Shared.Solutions;

using Xunit;

namespace Mma.Cli.Bff.Tests;

public sealed class MmaCodeGenOrchestratorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dataDir;
    private readonly string _workDir;

    public MmaCodeGenOrchestratorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-codegen-tests-{Guid.NewGuid():N}");
        _dataDir = Path.Combine(_tempRoot, "data");
        _workDir = Path.Combine(_tempRoot, "work");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_workDir);
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
            // Best-effort cleanup.
        }
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MMA_CLI_DATA_DIR"] = _dataDir,
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDotnetTemplateRunner>();
                services.AddSingleton<IDotnetTemplateRunner, FakeDotnetTemplateRunner>();
            });
        });

    [Fact]
    public async Task PreviewAndApply_GeneratesEntityAndPropertySequentially_AndMarksApplied()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var createRes = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Acme.Inventory", _workDir, "AutoMapper"));
        createRes.EnsureSuccessStatusCode();
        var project = (await createRes.Content.ReadFromJsonAsync<ProjectSummaryDto>())!;

        var schema = new SchemaModelDto(
            Id: $"project-{project.Id}",
            Name: project.Name,
            Engine: "mssql",
            Description: "",
            UpdatedAt: DateTime.UtcNow.ToString("O"),
            Tables:
            [
                new SchemaTableDto(
                    Id: "t_item",
                    Name: "Item",
                    Schema: "dbo",
                    StorageEngine: null,
                    Charset: null,
                    CodeGen: new TableCodeGenConfigDto(true, null, true),
                    Color: "emerald",
                    Comment: "",
                    X: 100,
                    Y: 100,
                    Columns:
                    [
                        new SchemaColumnDto("c_id", "Id", "uniqueidentifier", true, false, true, false, "", "", ""),
                        new SchemaColumnDto("c_sku", "Sku", "nvarchar(64)", false, false, true, false, "", "", ""),
                    ]),
            ],
            Relations: []);

        var putRes = await client.PutAsJsonAsync($"/api/projects/{project.Id}/schema", schema);
        putRes.EnsureSuccessStatusCode();

        // 1. Preview
        var previewRes = await client.PostAsync($"/api/projects/{project.Id}/generate/preview", null);
        Assert.Equal(HttpStatusCode.OK, previewRes.StatusCode);
        var preview = await previewRes.Content.ReadFromJsonAsync<CodeGenPreviewDto>();
        Assert.NotNull(preview);
        Assert.Single(preview!.PendingEntities);
        Assert.Single(preview.PendingProperties);
        Assert.Contains(preview.EquivalentCommands, c => c.Contains("mma g e Item Guid"));
        Assert.Contains(preview.EquivalentCommands, c => c.Contains("mma g p Item Sku string false"));

        // 2. Apply
        var applyRes = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/generate/apply",
            new CodeGenApplyRequest(RunEfMigration: false));
        Assert.Equal(HttpStatusCode.OK, applyRes.StatusCode);

        var apply = await applyRes.Content.ReadFromJsonAsync<CodeGenApplyResultDto>();
        Assert.NotNull(apply);
        Assert.True(apply!.Succeeded);
        Assert.Equal(1, apply.SyncState.AppliedEntities);
        Assert.Equal(0, apply.SyncState.PendingEntities);
        Assert.Equal(0, apply.SyncState.PendingProperties);

        var entityFile = Path.Combine(project.Path, "Acme.Inventory", "Acme.Inventory.Core", "Database", "Tables", "Item.cs");
        Assert.True(File.Exists(entityFile));
        Assert.Contains("Sku", File.ReadAllText(entityFile));
    }

    [Fact]
    public async Task PreviewAndApply_GeneratesRelationBetweenEntities_AndMarksApplied()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var createRes = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Acme.Crm", _workDir, "AutoMapper"));
        createRes.EnsureSuccessStatusCode();
        var project = (await createRes.Content.ReadFromJsonAsync<ProjectSummaryDto>())!;

        var schema = new SchemaModelDto(
            Id: $"project-{project.Id}",
            Name: project.Name,
            Engine: "mssql",
            Description: "",
            UpdatedAt: DateTime.UtcNow.ToString("O"),
            Tables:
            [
                new SchemaTableDto(
                    Id: "t_contact",
                    Name: "Contact",
                    Schema: "dbo",
                    StorageEngine: null,
                    Charset: null,
                    CodeGen: new TableCodeGenConfigDto(true, null, true),
                    Color: "emerald",
                    Comment: "",
                    X: 100,
                    Y: 100,
                    Columns:
                    [
                        new SchemaColumnDto("c_contact_id", "Id", "uniqueidentifier", true, false, true, false, "", "", ""),
                    ]),
                new SchemaTableDto(
                    Id: "t_contact_type",
                    Name: "ContactType",
                    Schema: "dbo",
                    StorageEngine: null,
                    Charset: null,
                    CodeGen: new TableCodeGenConfigDto(true, null, true),
                    Color: "blue",
                    Comment: "",
                    X: 400,
                    Y: 100,
                    Columns:
                    [
                        new SchemaColumnDto("c_type_id", "Id", "uniqueidentifier", true, false, true, false, "", "", ""),
                    ]),
            ],
            Relations:
            [
                new SchemaRelationDto(
                    Id: "r_contact_type",
                    Name: "Contact -> ContactType",
                    SourceTableId: "t_contact",
                    SourceColumnId: "c_contact_id",
                    TargetTableId: "t_contact_type",
                    TargetColumnId: "c_type_id",
                    SourceColumnIds: null,
                    TargetColumnIds: null,
                    Cardinality: "1:N",
                    OnDelete: "CASCADE",
                    OnUpdate: "NO ACTION"),
            ]);

        var putRes = await client.PutAsJsonAsync($"/api/projects/{project.Id}/schema", schema);
        putRes.EnsureSuccessStatusCode();

        var applyRes = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/generate/apply",
            new CodeGenApplyRequest(RunEfMigration: false));
        Assert.Equal(HttpStatusCode.OK, applyRes.StatusCode);

        var apply = await applyRes.Content.ReadFromJsonAsync<CodeGenApplyResultDto>();
        Assert.NotNull(apply);
        Assert.True(apply!.Succeeded, string.Join("; ", apply.Steps.Select(s => $"{s.Kind}:{s.Target}:{s.Succeeded}:{s.Message}")));
        Assert.Equal(0, apply.SyncState.PendingRelations);
        Assert.Contains(apply.Steps, s => s.Kind == "relation" && s.Succeeded);
    }

    [Fact]
    public void FilterMmaSystemObjects_RemovesIdentityTablesAndAuditColumns()
    {
        var raw = new IntrospectionResult(
            Database: "AppDb",
            Engine: "mssql",
            Tables:
            [
                new IntrospectionTable(
                    "t_users", "AppUsers", "dbo", "slate", "", 0, 0,
                    [new IntrospectionColumn("u_id", "Id", "uniqueidentifier", true, false, true, false, "", "", "")],
                    []),
                new IntrospectionTable(
                    "t_order", "Orders", "dbo", "blue", "", 0, 0,
                    [
                        new IntrospectionColumn("o_id", "Id", "uniqueidentifier", true, false, true, false, "", "", ""),
                        new IntrospectionColumn("o_total", "Total", "decimal(18,2)", false, false, false, false, "", "", ""),
                        new IntrospectionColumn("o_created", "CreatedDate", "datetime2", false, false, false, false, "", "", ""),
                        new IntrospectionColumn("o_deleted", "IsDeleted", "bit", false, false, false, false, "", "", ""),
                    ],
                    []),
            ],
            Relations: [],
            Warnings: []);

        var filtered = DatabaseIntrospectionService.FilterMmaSystemObjects(raw);

        var table = Assert.Single(filtered.Tables);
        Assert.Equal("Orders", table.Name);
        Assert.Equal(2, table.Columns.Count);
        Assert.DoesNotContain(table.Columns, c => c.Name is "CreatedDate" or "IsDeleted");
    }

    [Fact]
    public async Task PreviewAndApply_HonorsTableCodeGenOptions_ServiceMethodsAndApiToggle()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var createRes = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Acme.Studio", _workDir, "AutoMapper"));
        createRes.EnsureSuccessStatusCode();
        var project = (await createRes.Content.ReadFromJsonAsync<ProjectSummaryDto>())!;

        var schema = new SchemaModelDto(
            Id: $"project-{project.Id}",
            Name: project.Name,
            Engine: "mssql",
            Description: "",
            UpdatedAt: DateTime.UtcNow.ToString("O"),
            Tables:
            [
                new SchemaTableDto(
                    Id: "t_ledger",
                    Name: "Ledger",
                    Schema: "dbo",
                    StorageEngine: null,
                    Charset: null,
                    CodeGen: new TableCodeGenConfigDto(false, null, false),
                    Color: "slate",
                    Comment: "",
                    X: 100,
                    Y: 100,
                    Columns:
                    [
                        new SchemaColumnDto("c_ledger_id", "Id", "uniqueidentifier", true, false, true, false, "", "", ""),
                    ]),
                new SchemaTableDto(
                    Id: "t_invoice",
                    Name: "Invoice",
                    Schema: "dbo",
                    StorageEngine: null,
                    Charset: null,
                    CodeGen: new TableCodeGenConfigDto(
                        AddService: true,
                        ServiceMethods: new Dictionary<string, bool>
                        {
                            ["getAll"] = true,
                            ["select"] = false,
                            ["find"] = true,
                            ["add"] = true,
                            ["update"] = false,
                            ["delete"] = false,
                        },
                        AddApiController: true),
                    Color: "blue",
                    Comment: "",
                    X: 400,
                    Y: 100,
                    Columns:
                    [
                        new SchemaColumnDto("c_inv_id", "Id", "bigint", true, false, true, true, "", "", ""),
                    ]),
                new SchemaTableDto(
                    Id: "t_note",
                    Name: "AuditNote",
                    Schema: "dbo",
                    StorageEngine: null,
                    Charset: null,
                    CodeGen: new TableCodeGenConfigDto(true, null, false),
                    Color: "amber",
                    Comment: "",
                    X: 700,
                    Y: 100,
                    Columns:
                    [
                        new SchemaColumnDto("c_note_id", "Id", "uniqueidentifier", true, false, true, false, "", "", ""),
                    ]),
            ],
            Relations: []);

        var putRes = await client.PutAsJsonAsync($"/api/projects/{project.Id}/schema", schema);
        putRes.EnsureSuccessStatusCode();

        // Preview reflects CodeGen flags
        var previewRes = await client.PostAsync($"/api/projects/{project.Id}/generate/preview", null);
        previewRes.EnsureSuccessStatusCode();
        var preview = (await previewRes.Content.ReadFromJsonAsync<CodeGenPreviewDto>())!;

        Assert.Contains(preview.EquivalentCommands, c => c.Contains("mma g e Ledger Guid") && c.Contains("--no-service"));
        Assert.Contains(preview.EquivalentCommands, c => c.Contains("mma g e Invoice long") && c.Contains("--methods getAll,find,add"));
        Assert.Contains(preview.EquivalentCommands, c => c.Contains("mma g e AuditNote Guid") && c.Contains("--no-api"));

        var invoicePreview = Assert.Single(preview.PendingEntities, e => e.EntityName == "Invoice");
        Assert.True(invoicePreview.HasService);
        Assert.True(invoicePreview.HasApi);
        Assert.Equal(["getAll", "find", "add"], invoicePreview.ServiceMethods);

        // Apply honors CodeGen flags on disk
        var applyRes = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/generate/apply",
            new CodeGenApplyRequest(RunEfMigration: false));
        applyRes.EnsureSuccessStatusCode();
        var apply = (await applyRes.Content.ReadFromJsonAsync<CodeGenApplyResultDto>())!;
        Assert.True(apply.Succeeded);

        var projectsPath = Path.Combine(project.Path, "Acme.Studio");
        Assert.False(File.Exists(Path.Combine(projectsPath, "Acme.Studio.Services", "LedgerService.cs")));
        Assert.False(File.Exists(Path.Combine(projectsPath, "Acme.Studio.AppApi", "Controllers", "v1", "LedgersController.cs")));

        var invoiceServicePath = Path.Combine(projectsPath, "Acme.Studio.Services", "InvoiceService.cs");
        var invoiceControllerPath = Path.Combine(projectsPath, "Acme.Studio.AppApi", "Controllers", "v1", "InvoicesController.cs");
        Assert.True(File.Exists(invoiceServicePath));
        Assert.True(File.Exists(invoiceControllerPath));

        var invoiceServiceText = File.ReadAllText(invoiceServicePath);
        Assert.Contains("All(QueryViewModel query)", invoiceServiceText);
        Assert.Contains("Find(long id)", invoiceServiceText);
        Assert.Contains("Add(InvoiceModifyModel model)", invoiceServiceText);
        Assert.DoesNotContain("Select(QueryViewModel query)", invoiceServiceText);
        Assert.DoesNotContain("Update(InvoiceModifyModel model)", invoiceServiceText);
        Assert.DoesNotContain("Delete(long id)", invoiceServiceText);

        var invoiceControllerText = File.ReadAllText(invoiceControllerPath);
        Assert.Contains("GetAll(", invoiceControllerText);
        Assert.Contains("GetOne(", invoiceControllerText);
        Assert.Contains("PostInvoice(", invoiceControllerText);
        Assert.DoesNotContain("PutInvoice(", invoiceControllerText);
        Assert.DoesNotContain("DeleteInvoice(", invoiceControllerText);

        Assert.True(File.Exists(Path.Combine(projectsPath, "Acme.Studio.Services", "AuditNoteService.cs")));
        Assert.False(File.Exists(Path.Combine(projectsPath, "Acme.Studio.AppApi", "Controllers", "v1", "AuditNotesController.cs")));
    }

    [Fact]
    public void ResolveMigrationPaths_SupportsBothFlatAndLegacyNestedLayouts()
    {
        // Flat .slnx layout
        var flatRoot = Path.Combine(_workDir, "FlatSolution");
        Directory.CreateDirectory(Path.Combine(flatRoot, "FlatSolution.EntityFramework"));
        Directory.CreateDirectory(Path.Combine(flatRoot, "FlatSolution.AppApi"));

        var (flatProjectsPath, flatEfPath, flatApiPath) = EfMigrationRunner.ResolveMigrationPaths(flatRoot, "FlatSolution");
        Assert.Equal(flatRoot, flatProjectsPath);
        Assert.Equal(Path.Combine(flatRoot, "FlatSolution.EntityFramework"), flatEfPath);
        Assert.Equal(Path.Combine(flatRoot, "FlatSolution.AppApi"), flatApiPath);

        // Legacy nested layout
        var nestedRoot = Path.Combine(_workDir, "NestedSolution");
        Directory.CreateDirectory(Path.Combine(nestedRoot, "NestedSolution", "NestedSolution.EntityFramework"));
        Directory.CreateDirectory(Path.Combine(nestedRoot, "NestedSolution", "NestedSolution.AppApi"));

        var (nestedProjectsPath, nestedEfPath, nestedApiPath) = EfMigrationRunner.ResolveMigrationPaths(nestedRoot, "NestedSolution");
        Assert.Equal(Path.Combine(nestedRoot, "NestedSolution"), nestedProjectsPath);
        Assert.Equal(Path.Combine(nestedRoot, "NestedSolution", "NestedSolution.EntityFramework"), nestedEfPath);
        Assert.Equal(Path.Combine(nestedRoot, "NestedSolution", "NestedSolution.AppApi"), nestedApiPath);
    }
}
