using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Process;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Bff.Tests;

public sealed class WorkspaceEndpointsTests : IDisposable
{
    private const string BootstrapToken = "workspace-test-bootstrap-token";

    private readonly string _tempRoot;
    private readonly string _allowedRoot;
    private readonly string _dbPath;

    public WorkspaceEndpointsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-bff-e2e-{Guid.NewGuid():N}");
        _allowedRoot = Path.Combine(_tempRoot, "workspaces");
        _dbPath = Path.Combine(_tempRoot, "data", "workspace.db");

        Directory.CreateDirectory(_allowedRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["BffSecurity:BootstrapToken"] = BootstrapToken,
                        ["BffSecurity:AllowDevAutoSession"] = "false",
                        ["BffSecurity:AllowedRoots:0"] = _allowedRoot,
                        ["Bff:WorkspaceDbPath"] = _dbPath
                    });
                });
            });
    }

    private static async Task<(HttpClient Client, string CsrfToken)> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        using var resp = await client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { token = BootstrapToken });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var csrf = doc.RootElement.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(csrf));
        return (client, csrf!);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string csrfToken,
        object? payload = null)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-Mma-Csrf", csrfToken);
        if (payload is not null)
        {
            req.Content = JsonContent.Create(payload);
        }

        return await client.SendAsync(req);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    [Fact]
    public async Task EndToEnd_Solution_DraftModeling_PlanDiff_Apply_Sse_Templates_And_OpenApi()
    {
        var subDir = Path.Combine(_allowedRoot, "projects");
        Directory.CreateDirectory(subDir);

        await using var factory = CreateFactory();
        var (client, csrf) = await CreateAuthenticatedClientAsync(factory);
        using var authClient = client;

        // 1. Health
        using var healthResp = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
        using (var healthDoc = await ReadJsonAsync(healthResp))
        {
            Assert.Equal("healthy", healthDoc.RootElement.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(healthDoc.RootElement.GetProperty("version").GetString()));
        }

        // 2. Browse filesystem
        using var browseResp = await client.GetAsync("/api/v1/fs/browse");
        Assert.Equal(HttpStatusCode.OK, browseResp.StatusCode);
        using (var browseDoc = await ReadJsonAsync(browseResp))
        {
            var entries = browseDoc.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Contains(entries, e =>
                e.GetProperty("name").GetString() == "projects" &&
                e.GetProperty("isDirectory").GetBoolean());
        }

        // 3. Create solution
        using var createResp = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/workspaces", csrf, new
        {
            parentDirectory = subDir,
            solutionName = "SampleBffApi",
            mapper = "AutoMapper",
            dryRun = false
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        int projectId;
        string solutionPath;
        using (var createDoc = await ReadJsonAsync(createResp))
        {
            var ws = createDoc.RootElement.GetProperty("workspace");
            projectId = ws.GetProperty("id").GetInt32();
            solutionPath = ws.GetProperty("solutionPath").GetString()!;
            Assert.Equal("SampleBffApi", ws.GetProperty("name").GetString());
            Assert.True(Directory.Exists(solutionPath));
        }

        // 4. Draft modeling (entities, property, relation) without writing C# entity files on disk
        using (var r1 = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/entities", csrf, new
        {
            name = "Order",
            pkType = "Guid",
            hasService = true,
            hasApi = true
        }))
        {
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        }

        using (var r2 = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/entities", csrf, new
        {
            name = "OrderItem",
            pkType = "Guid",
            hasService = true,
            hasApi = true
        }))
        {
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        }

        using (var r3 = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/entities/Order/properties", csrf, new
        {
            name = "Title",
            dataType = "string",
            nullable = false
        }))
        {
            Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        }

        using (var r4 = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/relations", csrf, new
        {
            parentEntity = "Order",
            childEntity = "OrderItem",
            foreignKey = "OrderId",
            foreignKeyType = "Guid",
            relationType = "1:N",
            deleteBehavior = "Cascade"
        }))
        {
            Assert.Equal(HttpStatusCode.OK, r4.StatusCode);
        }

        var orderEntityFile = Path.Combine(solutionPath, "SampleBffApi", "SampleBffApi.Core", "Database", "Tables", "Order.cs");
        var orderItemEntityFile = Path.Combine(solutionPath, "SampleBffApi", "SampleBffApi.Core", "Database", "Tables", "OrderItem.cs");
        Assert.False(File.Exists(orderEntityFile));
        Assert.False(File.Exists(orderItemEntityFile));

        // 5. Plan diff (dry run, zero disk writes)
        using var planResp = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/plan-diff", csrf, new { });
        Assert.Equal(HttpStatusCode.OK, planResp.StatusCode);

        string planHash;
        using (var planDoc = await ReadJsonAsync(planResp))
        {
            planHash = planDoc.RootElement.GetProperty("planHash").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(planHash));
            Assert.True(planDoc.RootElement.GetProperty("hasChanges").GetBoolean());

            var diffs = planDoc.RootElement.GetProperty("fileChanges").EnumerateArray().ToArray();
            var paths = diffs.Select(d => d.GetProperty("relativePath").GetString() ?? string.Empty).ToArray();
            Assert.Contains(paths, p => p.EndsWith("/Order.cs", StringComparison.Ordinal));
            Assert.Contains(paths, p => p.EndsWith("/OrderItem.cs", StringComparison.Ordinal));
            Assert.Contains(paths, p => p.EndsWith("/OrderConfig.cs", StringComparison.Ordinal));
            Assert.Contains(paths, p => p.EndsWith("/ApplicationDbContext.cs", StringComparison.Ordinal));

            var orderDiff = Assert.Single(diffs, d => (d.GetProperty("relativePath").GetString() ?? "").EndsWith("/Order.cs", StringComparison.Ordinal));
            Assert.Contains("Title", orderDiff.GetProperty("unifiedDiff").GetString(), StringComparison.Ordinal);
        }

        Assert.False(File.Exists(orderEntityFile));
        Assert.False(File.Exists(orderItemEntityFile));

        // 6. Stale planHash -> 409 MANIFEST_CONFLICT
        using var staleResp = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/apply", csrf, new
        {
            planHash = "sha256:stale0000000000000000",
            runBuild = false
        });
        Assert.Equal(HttpStatusCode.Conflict, staleResp.StatusCode);
        using (var staleDoc = await ReadJsonAsync(staleResp))
        {
            Assert.Equal("MANIFEST_CONFLICT", staleDoc.RootElement.GetProperty("errorCode").GetString());
        }
        Assert.False(File.Exists(orderEntityFile));

        // 7. Matching planHash -> Apply writes C# files, sets Applied=true, syncs SQLite, emits SSE events
        using var applyResp = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/apply", csrf, new
        {
            planHash,
            runBuild = false
        });
        Assert.Equal(HttpStatusCode.OK, applyResp.StatusCode);

        string jobId;
        using (var applyDoc = await ReadJsonAsync(applyResp))
        {
            jobId = applyDoc.RootElement.GetProperty("jobId").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(jobId));
            Assert.NotEmpty(applyDoc.RootElement.GetProperty("createdFiles").EnumerateArray());
            Assert.NotEmpty(applyDoc.RootElement.GetProperty("modifiedFiles").EnumerateArray());

            var entities = applyDoc.RootElement.GetProperty("manifest").GetProperty("entities").EnumerateArray().ToArray();
            Assert.All(entities, e => Assert.True(e.GetProperty("applied").GetBoolean()));
        }

        Assert.True(File.Exists(orderEntityFile));
        Assert.True(File.Exists(orderItemEntityFile));
        var orderCode = await File.ReadAllTextAsync(orderEntityFile);
        Assert.Contains("public string Title", orderCode);
        Assert.Contains("OrderItems", orderCode);
        var orderItemCode = await File.ReadAllTextAsync(orderItemEntityFile);
        Assert.Contains("OrderId", orderItemCode);

        using (var workspacesResp = await client.GetAsync("/api/v1/workspaces"))
        {
            Assert.Equal(HttpStatusCode.OK, workspacesResp.StatusCode);
            using var wsDoc = await ReadJsonAsync(workspacesResp);
            var item = Assert.Single(wsDoc.RootElement.EnumerateArray(), w => w.GetProperty("id").GetInt32() == projectId);
            Assert.Equal(2, item.GetProperty("entityCount").GetInt32());
            Assert.Equal(1, item.GetProperty("relationCount").GetInt32());
        }

        // 8. SSE job events
        using var sseResp = await client.GetAsync($"/api/v1/jobs/{Uri.EscapeDataString(jobId)}/events");
        Assert.Equal(HttpStatusCode.OK, sseResp.StatusCode);
        Assert.Equal("text/event-stream", sseResp.Content.Headers.ContentType?.MediaType);
        Assert.True(sseResp.Headers.CacheControl?.NoCache);
        var sseText = await sseResp.Content.ReadAsStringAsync();
        Assert.Contains("event: progress", sseText);
        Assert.Contains("event: done", sseText);

        // 9. Templates list & update (invalid Scriban rejected + valid saved)
        using (var tplGetResp = await client.GetAsync($"/api/v1/projects/{projectId}/templates"))
        {
            Assert.Equal(HttpStatusCode.OK, tplGetResp.StatusCode);
            using var tplDoc = await ReadJsonAsync(tplGetResp);
            Assert.Equal("AutoMapper", tplDoc.RootElement.GetProperty("activeMapper").GetString());
            var templates = tplDoc.RootElement.GetProperty("templates").EnumerateArray().ToArray();
            Assert.Contains(templates, t => t.GetProperty("name").GetString() == "Validator");
        }

        using (var invalidTplResp = await SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"/api/v1/projects/{projectId}/templates/AutoMapper/Validator",
                   csrf,
                   new { content = "{{ if unclosed" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidTplResp.StatusCode);
            using var errDoc = await ReadJsonAsync(invalidTplResp);
            Assert.Equal("TEMPLATE_ERROR", errDoc.RootElement.GetProperty("errorCode").GetString());
        }

        var customValidator = "// Custom {{ entity_name }}Validator\npublic class {{ entity_name }}Validator {}";
        using (var validTplResp = await SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"/api/v1/projects/{projectId}/templates/AutoMapper/Validator",
                   csrf,
                   new { content = customValidator }))
        {
            Assert.Equal(HttpStatusCode.OK, validTplResp.StatusCode);
            using var okDoc = await ReadJsonAsync(validTplResp);
            Assert.True(okDoc.RootElement.GetProperty("isCustomOverride").GetBoolean());
        }

        var customTplPath = Path.Combine(solutionPath, ".mma", "templates", "AutoMapper", "Validator.scriban");
        Assert.True(File.Exists(customTplPath));

        // 10. OpenAPI v1 spec
        using var openApiResp = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApiResp.StatusCode);
        using var openApiDoc = await ReadJsonAsync(openApiResp);
        var apiPaths = openApiDoc.RootElement.GetProperty("paths");
        Assert.True(apiPaths.TryGetProperty("/api/v1/workspaces", out _));
        Assert.True(apiPaths.TryGetProperty("/api/v1/projects/{id}/plan-diff", out _));
    }

    [Fact]
    public async Task DatabaseImport_Test_Introspect_And_Apply_AgainstTempSqliteDb()
    {
        var dbFile = Path.Combine(_allowedRoot, "legacy-store.sqlite");
        await CreateSampleSqliteDbAsync(dbFile);

        await using var factory = CreateFactory();
        var (client, csrf) = await CreateAuthenticatedClientAsync(factory);
        using var authClient = client;

        using var createResp = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/workspaces", csrf, new
        {
            parentDirectory = _allowedRoot,
            solutionName = "DbImportBffApi",
            mapper = "AutoMapper",
            dryRun = false
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        int projectId;
        string solutionPath;
        using (var createDoc = await ReadJsonAsync(createResp))
        {
            var ws = createDoc.RootElement.GetProperty("workspace");
            projectId = ws.GetProperty("id").GetInt32();
            solutionPath = ws.GetProperty("solutionPath").GetString()!;
        }

        using var testResp = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/db-import/test", csrf, new
        {
            provider = "Sqlite",
            parameters = new { serverOrPath = dbFile }
        });
        Assert.Equal(HttpStatusCode.OK, testResp.StatusCode);

        using var introspectResp = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/db-import/introspect", csrf, new
        {
            provider = "Sqlite",
            parameters = new { serverOrPath = dbFile }
        });
        Assert.Equal(HttpStatusCode.OK, introspectResp.StatusCode);

        string snapshotJson;
        using (var introDoc = await ReadJsonAsync(introspectResp))
        {
            var tables = introDoc.RootElement.GetProperty("tables").EnumerateArray().ToArray();
            Assert.Contains(tables, t => t.GetProperty("tableName").GetString() == "Customers");
            Assert.Contains(tables, t => t.GetProperty("tableName").GetString() == "Invoices");
            snapshotJson = introDoc.RootElement.GetRawText();
        }

        using var snapshotDoc = JsonDocument.Parse(snapshotJson);
        using var applyImportResp = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/projects/{projectId}/db-import/apply",
            csrf,
            new
            {
                snapshot = snapshotDoc.RootElement,
                applyImmediately = true
            });
        Assert.Equal(HttpStatusCode.OK, applyImportResp.StatusCode);

        using (var applyImportDoc = await ReadJsonAsync(applyImportResp))
        {
            Assert.False(string.IsNullOrWhiteSpace(applyImportDoc.RootElement.GetProperty("jobId").GetString()));
            var entities = applyImportDoc.RootElement.GetProperty("manifest").GetProperty("entities").EnumerateArray().ToArray();
            Assert.Contains(entities, e => e.GetProperty("name").GetString() == "Customer" && e.GetProperty("applied").GetBoolean());
            Assert.Contains(entities, e => e.GetProperty("name").GetString() == "Invoice" && e.GetProperty("applied").GetBoolean());
        }

        Assert.True(File.Exists(Path.Combine(solutionPath, "DbImportBffApi", "DbImportBffApi.Core", "Database", "Tables", "Customer.cs")));
        Assert.True(File.Exists(Path.Combine(solutionPath, "DbImportBffApi", "DbImportBffApi.Core", "Database", "Tables", "Invoice.cs")));
    }

    [Fact]
    public async Task RegisterExistingSolution_DraftDeletes_TemplateWhitelist_And_BackgroundJobs()
    {
        // Pre-create solution directly on disk via Core service to verify /workspaces/register
        var existingSvc = new Mma.Cli.Core.Services.SolutionService();
        var createRes = await existingSvc.CreateAsync(new Mma.Cli.Core.Contracts.CreateSolutionRequest(
            _allowedRoot,
            "RegisteredBffApi",
            Mma.Cli.Core.Domain.MapperKind.Mapster,
            DryRun: false));
        Assert.True(createRes.IsSuccess, createRes.Error?.Message);
        var solutionPath = Path.Combine(_allowedRoot, "RegisteredBffApi");

        await using var factory = CreateFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDotnetRunner>();
                services.AddSingleton<IDotnetRunner, FakeDotnetRunner>();
            });
        });
        var (client, csrf) = await CreateAuthenticatedClientAsync(factory);
        using var authClient = client;

        // Register existing solution
        using var regResp = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/workspaces/register", csrf, new
        {
            solutionPath
        });
        Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);

        int projectId;
        using (var regDoc = await ReadJsonAsync(regResp))
        {
            projectId = regDoc.RootElement.GetProperty("workspace").GetProperty("id").GetInt32();
            Assert.Equal("Mapster", regDoc.RootElement.GetProperty("workspace").GetProperty("mapper").GetString());
        }

        // GET /projects/{id}
        using (var detailResp = await client.GetAsync($"/api/v1/projects/{projectId}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
            using var detailDoc = await ReadJsonAsync(detailResp);
            Assert.True(detailDoc.RootElement.GetProperty("validation").GetProperty("isValid").GetBoolean());
        }

        // Add & delete draft entity, property, relation
        using (var addParent = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/entities", csrf, new { name = "Tenant", pkType = "Guid" }))
            Assert.Equal(HttpStatusCode.OK, addParent.StatusCode);
        using (var addChild = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/entities", csrf, new { name = "Project", pkType = "Guid" }))
            Assert.Equal(HttpStatusCode.OK, addChild.StatusCode);
        using (var addProp = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/entities/Project/properties", csrf, new { name = "Code", dataType = "string" }))
            Assert.Equal(HttpStatusCode.OK, addProp.StatusCode);
        using (var addRel = await SendJsonAsync(client, HttpMethod.Post, $"/api/v1/projects/{projectId}/relations", csrf, new { parentEntity = "Tenant", childEntity = "Project", foreignKey = "TenantId" }))
            Assert.Equal(HttpStatusCode.OK, addRel.StatusCode);

        using (var delRel = await SendJsonAsync(client, HttpMethod.Delete, $"/api/v1/projects/{projectId}/relations/Tenant_Project_TenantId", csrf))
            Assert.Equal(HttpStatusCode.OK, delRel.StatusCode);
        using (var delProp = await SendJsonAsync(client, HttpMethod.Delete, $"/api/v1/projects/{projectId}/entities/Project/properties/Code", csrf))
            Assert.Equal(HttpStatusCode.OK, delProp.StatusCode);
        using (var delEntity = await SendJsonAsync(client, HttpMethod.Delete, $"/api/v1/projects/{projectId}/entities/Project", csrf))
        {
            Assert.Equal(HttpStatusCode.OK, delEntity.StatusCode);
            using var delDoc = await ReadJsonAsync(delEntity);
            var remaining = delDoc.RootElement.GetProperty("manifest").GetProperty("entities").EnumerateArray().ToArray();
            Assert.Single(remaining);
            Assert.Equal("Tenant", remaining[0].GetProperty("name").GetString());
        }

        // Mapster template listing and disallowed template name check
        using (var tplListResp = await client.GetAsync($"/api/v1/projects/{projectId}/templates"))
        {
            Assert.Equal(HttpStatusCode.OK, tplListResp.StatusCode);
            using var tplDoc = await ReadJsonAsync(tplListResp);
            Assert.Equal("Mapster", tplDoc.RootElement.GetProperty("activeMapper").GetString());
        }

        using (var badTplNameResp = await SendJsonAsync(
                   client,
                   HttpMethod.Put,
                   $"/api/v1/projects/{projectId}/templates/Mapster/NotAllowedTemplate",
                   csrf,
                   new { content = "hello" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badTplNameResp.StatusCode);
            using var errDoc = await ReadJsonAsync(badTplNameResp);
            Assert.Equal("VALIDATION_ERROR", errDoc.RootElement.GetProperty("errorCode").GetString());
        }

        // Unsafe migration name rejected
        using (var badMigResp = await SendJsonAsync(
                   client,
                   HttpMethod.Post,
                   $"/api/v1/projects/{projectId}/migrations",
                   csrf,
                   new { name = "Init;echo pwn" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badMigResp.StatusCode);
            using var errDoc = await ReadJsonAsync(badMigResp);
            Assert.Equal("VALIDATION_ERROR", errDoc.RootElement.GetProperty("errorCode").GetString());
        }

        // Background build job returns 202 Accepted and completes via SSE
        using var buildResp = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/projects/{projectId}/build",
            csrf,
            new { configuration = "Debug" });
        Assert.Equal(HttpStatusCode.Accepted, buildResp.StatusCode);
        string buildJobId;
        using (var buildDoc = await ReadJsonAsync(buildResp))
        {
            buildJobId = buildDoc.RootElement.GetProperty("jobId").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(buildJobId));
        }

        using var sseResp = await client.GetAsync($"/api/v1/jobs/{Uri.EscapeDataString(buildJobId)}/events");
        Assert.Equal(HttpStatusCode.OK, sseResp.StatusCode);
        var ssePayload = await sseResp.Content.ReadAsStringAsync();
        Assert.Contains("event: progress", ssePayload);
        Assert.Contains("event: log", ssePayload);
        Assert.Contains("Build succeeded.", ssePayload);
        Assert.Contains("event: done", ssePayload);
    }

    private sealed class FakeDotnetRunner : IDotnetRunner
    {
        public Task<OperationResult<ProcessExecutionResult>> BuildAsync(
            string workingDirectory,
            string configuration = "Debug",
            IProgress<GenerationEvent>? progress = null,
            CancellationToken ct = default)
        {
            progress?.Report(GenerationEvent.Create(GenerationStage.ProcessOutput, "Build succeeded."));
            return Task.FromResult(OperationResult<ProcessExecutionResult>.Success(
                new ProcessExecutionResult(0, "Build succeeded.", string.Empty)));
        }

        public Task<OperationResult<ProcessExecutionResult>> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            IProgress<GenerationEvent>? progress = null,
            CancellationToken ct = default)
        {
            var output = arguments.Count > 0 && arguments[0] == "--version"
                ? "10.0.100"
                : string.Empty;
            return Task.FromResult(OperationResult<ProcessExecutionResult>.Success(
                new ProcessExecutionResult(0, output, string.Empty)));
        }
    }

    private static async Task CreateSampleSqliteDbAsync(string dbPath)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false
        };

        await using var conn = new SqliteConnection(csb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE Customers (
                Id TEXT PRIMARY KEY,
                FullName TEXT NOT NULL
            );
            CREATE TABLE Invoices (
                Id TEXT PRIMARY KEY,
                CustomerId TEXT NOT NULL REFERENCES Customers(Id) ON DELETE CASCADE,
                InvoiceNumber TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
