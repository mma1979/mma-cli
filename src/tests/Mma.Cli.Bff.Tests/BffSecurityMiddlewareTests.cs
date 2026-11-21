using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Mma.Cli.Bff.Security;
using Mma.Cli.Bff.Services;

namespace Mma.Cli.Bff.Tests;

public sealed class BffSecurityMiddlewareTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _allowedRoot;
    private readonly string _outsideRoot;
    private readonly string _dbPath;

    public BffSecurityMiddlewareTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-bff-sec-{Guid.NewGuid():N}");
        _allowedRoot = Path.Combine(_tempRoot, "allowed");
        _outsideRoot = Path.Combine(_tempRoot, "outside");
        _dbPath = Path.Combine(_tempRoot, "db", "workspace.db");

        Directory.CreateDirectory(_allowedRoot);
        Directory.CreateDirectory(_outsideRoot);
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

    private WebApplicationFactory<Program> CreateFactory(
        string? bootstrapToken = "test-bootstrap-token-123",
        bool allowDevAutoSession = false)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        ["BffSecurity:BootstrapToken"] = bootstrapToken ?? string.Empty,
                        ["BffSecurity:AllowDevAutoSession"] = allowDevAutoSession ? "true" : "false",
                        ["BffSecurity:AllowedRoots:0"] = _allowedRoot,
                        ["Bff:WorkspaceDbPath"] = _dbPath
                    };
                    config.AddInMemoryCollection(settings);
                });
            });
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("errorCode", out var elem)
            ? elem.GetString()
            : null;
    }

    private static async Task<string> BootstrapAsync(HttpClient client, string token)
    {
        using var resp = await client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { token });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var csrf = doc.RootElement.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(csrf));
        return csrf!;
    }

    [Fact]
    public async Task DisallowedHostHeader_Returns403InvalidHost()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.Host = "evil.com";

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("INVALID_HOST", await ReadErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1:5180")]
    [InlineData("[::1]:5180")]
    public async Task LoopbackHostHeaders_ArePermitted(string host)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.Host = host;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DisallowedOriginHeader_Returns403OriginNotAllowed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ORIGIN_NOT_ALLOWED", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task NullOriginHeaderOnPost_Returns403OriginNotAllowed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/bootstrap")
        {
            Content = JsonContent.Create(new { token = "test-bootstrap-token-123" })
        };
        request.Headers.Add("Origin", "null");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ORIGIN_NOT_ALLOWED", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task UnauthenticatedApiRequest_Returns401Unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task BootstrapToken_IssuesHttpOnlyStrictCookie_AndRejectsReplay()
    {
        const string token = "one-time-secret-token";
        await using var factory = CreateFactory(bootstrapToken: token);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        using var badResp = await client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { token = "wrong-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, badResp.StatusCode);
        Assert.Equal("AUTH_TOKEN_INVALID", await ReadErrorCodeAsync(badResp));

        using var okResp = await client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { token });
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

        Assert.True(okResp.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var sessionCookieHeader = Assert.Single(setCookies, c => c.Contains("mma_bff_session=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookieHeader, StringComparison.OrdinalIgnoreCase);

        using var okDoc = await JsonDocument.ParseAsync(await okResp.Content.ReadAsStreamAsync());
        Assert.True(okDoc.RootElement.GetProperty("authenticated").GetBoolean());
        var csrfToken = okDoc.RootElement.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(csrfToken));

        using var replayResp = await client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { token });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResp.StatusCode);
        Assert.Equal("AUTH_TOKEN_CONSUMED", await ReadErrorCodeAsync(replayResp));

        using var sessionResp = await client.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.OK, sessionResp.StatusCode);
        using var sessionDoc = await JsonDocument.ParseAsync(await sessionResp.Content.ReadAsStreamAsync());
        Assert.True(sessionDoc.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(csrfToken, sessionDoc.RootElement.GetProperty("csrfToken").GetString());
    }

    [Fact]
    public async Task PostWithoutOrMismatchedCsrfHeader_Returns403CsrfInvalid()
    {
        const string token = "csrf-test-token";
        await using var factory = CreateFactory(bootstrapToken: token);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var csrf = await BootstrapAsync(client, token);

        using var missingCsrfResp = await client.PostAsJsonAsync("/api/v1/workspaces", new
        {
            parentDirectory = _allowedRoot,
            solutionName = "MissingCsrfApi",
            mapper = "AutoMapper",
            dryRun = true
        });
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrfResp.StatusCode);
        Assert.Equal("CSRF_INVALID", await ReadErrorCodeAsync(missingCsrfResp));

        using var mismatchReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/workspaces")
        {
            Content = JsonContent.Create(new
            {
                parentDirectory = _allowedRoot,
                solutionName = "MismatchCsrfApi",
                mapper = "AutoMapper",
                dryRun = true
            })
        };
        mismatchReq.Headers.Add("X-Mma-Csrf", csrf + "-tampered");

        using var mismatchResp = await client.SendAsync(mismatchReq);
        Assert.Equal(HttpStatusCode.Forbidden, mismatchResp.StatusCode);
        Assert.Equal("CSRF_INVALID", await ReadErrorCodeAsync(mismatchResp));
    }

    [Fact]
    public async Task TraversalAndOutsideRootPaths_Return403PathOutsideAllowlist()
    {
        const string token = "path-guard-token";
        await using var factory = CreateFactory(bootstrapToken: token);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var csrf = await BootstrapAsync(client, token);

        using var traversalResp = await client.GetAsync("/api/v1/fs/browse?path=../../Outside");
        Assert.Equal(HttpStatusCode.Forbidden, traversalResp.StatusCode);
        Assert.Equal("PATH_OUTSIDE_ALLOWLIST", await ReadErrorCodeAsync(traversalResp));

        using var outsideBrowseResp = await client.GetAsync($"/api/v1/fs/browse?path={Uri.EscapeDataString(_outsideRoot)}");
        Assert.Equal(HttpStatusCode.Forbidden, outsideBrowseResp.StatusCode);
        Assert.Equal("PATH_OUTSIDE_ALLOWLIST", await ReadErrorCodeAsync(outsideBrowseResp));

        // Sibling prefix trick: /tmp/.../allowed-evil must NOT match /tmp/.../allowed
        var siblingDir = Path.Combine(_tempRoot, "allowed-evil");
        Directory.CreateDirectory(siblingDir);
        using var siblingResp = await client.GetAsync($"/api/v1/fs/browse?path={Uri.EscapeDataString(siblingDir)}");
        Assert.Equal(HttpStatusCode.Forbidden, siblingResp.StatusCode);
        Assert.Equal("PATH_OUTSIDE_ALLOWLIST", await ReadErrorCodeAsync(siblingResp));

        using var regReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/workspaces/register")
        {
            Content = JsonContent.Create(new { solutionPath = _outsideRoot })
        };
        regReq.Headers.Add("X-Mma-Csrf", csrf);
        using var regResp = await client.SendAsync(regReq);
        Assert.Equal(HttpStatusCode.Forbidden, regResp.StatusCode);
        Assert.Equal("PATH_OUTSIDE_ALLOWLIST", await ReadErrorCodeAsync(regResp));

        var externalSqlite = Path.Combine(_outsideRoot, "secret.sqlite");
        using var dbReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/db-import/test")
        {
            Content = JsonContent.Create(new
            {
                provider = "Sqlite",
                parameters = new { serverOrPath = externalSqlite }
            })
        };
        dbReq.Headers.Add("X-Mma-Csrf", csrf);
        using var dbResp = await client.SendAsync(dbReq);
        Assert.Equal(HttpStatusCode.Forbidden, dbResp.StatusCode);
        Assert.Equal("PATH_OUTSIDE_ALLOWLIST", await ReadErrorCodeAsync(dbResp));

        var linkPath = Path.Combine(_allowedRoot, "escape-link");
        var symlinkCreated = false;
        try
        {
            Directory.CreateSymbolicLink(linkPath, _outsideRoot);
            symlinkCreated = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Windows without Developer Mode / admin elevation may disallow symlink creation.
        }

        if (symlinkCreated)
        {
            using var symlinkResp = await client.GetAsync($"/api/v1/fs/browse?path={Uri.EscapeDataString(linkPath)}");
            Assert.Equal(HttpStatusCode.Forbidden, symlinkResp.StatusCode);
            Assert.Equal("PATH_OUTSIDE_ALLOWLIST", await ReadErrorCodeAsync(symlinkResp));
        }
    }

    [Fact]
    public async Task DevAutoSession_IssuesCookieOnlyWhenExplicitlyEnabledWithoutBootstrapToken()
    {
        await using var factory = CreateFactory(bootstrapToken: null, allowDevAutoSession: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        using var unauthResp = await client.GetAsync("/api/v1/workspaces");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);

        using var sessionResp = await client.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.OK, sessionResp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await sessionResp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("csrfToken").GetString()));

        using var authResp = await client.GetAsync("/api/v1/workspaces");
        Assert.Equal(HttpStatusCode.OK, authResp.StatusCode);
    }

    [Fact]
    public void WorkspacePathGuard_ValidatesInMemorySqlite_RejectsNullBytesAndTraversal()
    {
        var guard = new WorkspacePathGuard(new BffSecurityOptions
        {
            AllowedRoots = [_allowedRoot]
        });

        Assert.True(guard.ValidateSqlitePathIfLocal(":memory:", null).IsSuccess);
        Assert.True(guard.ValidateSqlitePathIfLocal(null, "Data Source=:memory:").IsSuccess);
        Assert.True(guard.ValidateSqlitePathIfLocal(Path.Combine(_allowedRoot, "app.db"), null).IsSuccess);

        var outside = guard.ValidateSqlitePathIfLocal(null, $"Data Source={Path.Combine(_outsideRoot, "app.db")}");
        Assert.False(outside.IsSuccess);
        Assert.Equal(BffErrorCodes.PATH_OUTSIDE_ALLOWLIST, outside.Error?.Code);

        var nullByte = guard.ValidateDirectoryPath(Path.Combine(_allowedRoot, "bad\0dir"));
        Assert.False(nullByte.IsSuccess);
        Assert.Equal("VALIDATION_ERROR", nullByte.Error?.Code);
    }

    [Fact]
    public async Task DbImportFailure_RedactsPasswords_And_BffHasZeroEnvironmentExit()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mma.Cli.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var bffDir = Path.Combine(dir!.FullName, "src", "Mma.Cli.Bff");
        Assert.True(Directory.Exists(bffDir));
        foreach (var cs in Directory.GetFiles(bffDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain("Environment.Exit", File.ReadAllText(cs), StringComparison.Ordinal);
        }

        const string token = "redact-check-token";
        const string secretPassword = "SuperSecretDbPassword_9988!";
        await using var factory = CreateFactory(bootstrapToken: token);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        var csrf = await BootstrapAsync(client, token);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/db-import/test")
        {
            Content = JsonContent.Create(new
            {
                provider = "SqlServer",
                connectionString = $"Server=127.0.0.1,1;Database=DoesNotExist;User Id=sa;Password={secretPassword};Connect Timeout=1;"
            })
        };
        req.Headers.Add("X-Mma-Csrf", csrf);
        using var resp = await client.SendAsync(req);
        Assert.False(resp.IsSuccessStatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secretPassword, body, StringComparison.Ordinal);
    }
}
