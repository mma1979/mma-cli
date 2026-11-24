using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Mma.Cli.Bff.CodeGen;
using Mma.Cli.Bff.Projects;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Solutions;

using Xunit;

namespace Mma.Cli.Bff.Tests;

public sealed class MmaSchemaMapperTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dataDir;
    private readonly string _workDir;

    public MmaSchemaMapperTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-schema-tests-{Guid.NewGuid():N}");
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

    [Theory]
    [InlineData("uuid", PkTypes.GUID)]
    [InlineData("uniqueidentifier", PkTypes.GUID)]
    [InlineData("int4", PkTypes.INT)]
    [InlineData("integer", PkTypes.INT)]
    [InlineData("bigint", PkTypes.LONG)]
    [InlineData("decimal(18,2)", PkTypes.DECIMAL)]
    [InlineData("double precision", PkTypes.FLOAT)]
    [InlineData("bit", PkTypes.BOOL)]
    [InlineData("timestamptz", PkTypes.DATE_TIME)]
    [InlineData("nvarchar(255)", PkTypes.STRING)]
    public void MapSqlTypeToCSharp_MapsAcrossEngines(string sqlType, string expectedCSharpType)
    {
        Assert.Equal(expectedCSharpType, MmaSchemaMapper.MapSqlTypeToCSharp(sqlType));
    }

    [Fact]
    public async Task SaveAndLoadSchema_DualWritesSchemaJsonAndLegacyProjectMma()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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

        using var client = factory.CreateClient();

        var createRes = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Acme.Billing", _workDir, "AutoMapper"));
        createRes.EnsureSuccessStatusCode();
        var project = (await createRes.Content.ReadFromJsonAsync<ProjectSummaryDto>())!;

        var schema = new SchemaModelDto(
            Id: $"project-{project.Id}",
            Name: project.Name,
            Engine: "postgresql",
            Description: "Billing schema",
            UpdatedAt: DateTime.UtcNow.ToString("O"),
            Tables:
            [
                new SchemaTableDto(
                    Id: "t_customer",
                    Name: "Customer",
                    Schema: "public",
                    StorageEngine: null,
                    Charset: null,
                    CodeGen: new TableCodeGenConfigDto(true, null, true),
                    Color: "blue",
                    Comment: "",
                    X: 120,
                    Y: 90,
                    Columns:
                    [
                        new SchemaColumnDto("c_id", "Id", "uuid", true, false, true, false, "", "", ""),
                        new SchemaColumnDto("c_email", "Email", "varchar(255)", false, false, true, false, "", "", ""),
                    ]),
            ],
            Relations: []);

        var putRes = await client.PutAsJsonAsync($"/api/projects/{project.Id}/schema", schema);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var saved = await putRes.Content.ReadFromJsonAsync<SaveSchemaResponseDto>();
        Assert.NotNull(saved);
        Assert.Equal(1, saved!.SyncState.TotalEntities);
        Assert.Equal(1, saved.SyncState.PendingEntities);
        Assert.Equal(1, saved.SyncState.PendingProperties);

        Assert.True(File.Exists(Path.Combine(project.Path, ".mma", "schema.json")));
        Assert.True(File.Exists(Path.Combine(project.Path, ".mma", "project.mma")));

        var getRes = await client.GetFromJsonAsync<ProjectSchemaResponseDto>($"/api/projects/{project.Id}/schema");
        Assert.NotNull(getRes);
        Assert.Single(getRes!.Schema.Tables);
        Assert.Equal(120, getRes.Schema.Tables[0].X);
        Assert.Equal("Customer", getRes.Schema.Tables[0].Name);
    }
}
