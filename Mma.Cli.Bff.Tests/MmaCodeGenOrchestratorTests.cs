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
}
