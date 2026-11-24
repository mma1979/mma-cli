using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using Mma.Cli.Bff.Projects;

using Xunit;

namespace Mma.Cli.Bff.Tests;

public sealed class ProjectEndpointsTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dataDir;
    private readonly string _workDir;

    public ProjectEndpointsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-proj-tests-{Guid.NewGuid():N}");
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
        });

    [Fact]
    public async Task BrowseFs_AndMkdir_WorksCrossPlatform()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var mkdirRes = await client.PostAsJsonAsync("/api/fs/mkdir", new MkdirRequest(_workDir, "SubFolderA"));
        Assert.Equal(HttpStatusCode.Created, mkdirRes.StatusCode);

        var browseRes = await client.GetAsync($"/api/fs/browse?path={Uri.EscapeDataString(_workDir)}");
        Assert.Equal(HttpStatusCode.OK, browseRes.StatusCode);

        var payload = await browseRes.Content.ReadFromJsonAsync<BrowseFsResponse>();
        Assert.NotNull(payload);
        Assert.Equal(Path.GetFullPath(_workDir), payload!.CurrentPath);
        Assert.Contains("SubFolderA", payload.Directories);
        Assert.False(payload.HasSolutionFile);
    }

    [Fact]
    public async Task CreateProject_ScaffoldsSolution_Lists_AndDeletesWithoutRemovingDiskFiles()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var createRes = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Acme.Shop", _workDir, "AutoMapper", RunInitialMigration: false));

        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<ProjectSummaryDto>();
        Assert.NotNull(created);
        Assert.Equal("Acme.Shop", created!.Name);
        Assert.Equal("AutoMapper", created.Mapper);
        Assert.True(created.ExistsOnDisk);
        Assert.True(File.Exists(Path.Combine(created.Path, "Acme.Shop.sln")));

        var list = await client.GetFromJsonAsync<List<ProjectSummaryDto>>("/api/projects");
        Assert.NotNull(list);
        Assert.Single(list!);

        var delRes = await client.DeleteAsync($"/api/projects/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Files on disk must remain intact!
        Assert.True(File.Exists(Path.Combine(created.Path, "Acme.Shop.sln")));
    }

    [Fact]
    public async Task ImportProject_InvalidDirectoryWithoutSln_Returns400_InvalidSolutionPath()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var emptyDir = Path.Combine(_workDir, "NotASolution");
        Directory.CreateDirectory(emptyDir);

        var res = await client.PostAsJsonAsync("/api/projects/import", new ImportProjectRequest(emptyDir));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var err = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_solution_path", err.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ImportProject_ReadsLegacyProjectMma_AndImportsEntities()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var slnDir = Path.Combine(_workDir, "LegacySln");
        Directory.CreateDirectory(Path.Combine(slnDir, ".mma"));
        File.WriteAllText(Path.Combine(slnDir, "LegacySln.sln"), string.Empty);
        File.WriteAllText(
            Path.Combine(slnDir, ".mma", "project.mma"),
            """
            {
              "Project": { "Id": 1, "Name": "LegacySln", "Path": "C:\\old\\LegacySln" },
              "Entities": [
                { "Id": 10, "EntityName": "Invoice", "PkType": "Guid", "HasService": true, "HasApi": true, "Applied": true }
              ],
              "Rows": [
                { "Id": 100, "ColumnName": "Amount", "DataType": "decimal", "Nullable": false, "Applied": true, "IsForeignKey": false, "EntityId": 10 }
              ],
              "Relations": []
            }
            """);

        var res = await client.PostAsJsonAsync("/api/projects/import", new ImportProjectRequest(slnDir));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var imported = await res.Content.ReadFromJsonAsync<ProjectSummaryDto>();
        Assert.NotNull(imported);
        Assert.Equal("LegacySln", imported!.Name);
        Assert.Equal(1, imported.EntityCount);
        Assert.Equal(0, imported.PendingCount);
    }
}
