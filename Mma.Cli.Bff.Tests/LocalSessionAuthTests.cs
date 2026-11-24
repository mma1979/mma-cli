using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Mma.Cli.Bff.Tests;

public sealed class LocalSessionAuthTests : IDisposable
{
    private readonly string _tempDataDir;

    public LocalSessionAuthTests()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), $"mma-bff-auth-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDataDir))
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?> extraConfig)
    {
        var config = new Dictionary<string, string?>
        {
            ["MMA_CLI_DATA_DIR"] = _tempDataDir,
        };
        foreach (var kv in extraConfig)
        {
            config[kv.Key] = kv.Value;
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(config));
        });
    }

    [Fact]
    public async Task HealthEndpoint_IsAccessibleWithoutSessionCookie_WhenTokenConfigured()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["MMA_UI_TOKEN"] = "secret-launch-token-12345",
            ["MMA_CLI_MODE"] = "local",
            ["MMA_INITIAL_PATH"] = "C:\\work\\MySolution",
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", doc.GetProperty("status").GetString());
        Assert.Equal("local-cli", doc.GetProperty("mode").GetString());
        Assert.True(doc.GetProperty("requiresAuth").GetBoolean());
        Assert.Equal("C:\\work\\MySolution", doc.GetProperty("initialPath").GetString());
    }

    [Fact]
    public async Task ApiRequest_WithoutSessionCookie_WhenTokenConfigured_Returns401()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["MMA_UI_TOKEN"] = "secret-launch-token-12345",
            ["MMA_CLI_MODE"] = "local",
        });

        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/unknown");
        req.Headers.Host = "localhost:5080";
        req.Headers.Add("Origin", "http://localhost:5080");

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SessionExchange_ValidToken_SetsHttpOnlySameSiteStrictCookie_AndAuthorizesSubsequentRequests()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["MMA_UI_TOKEN"] = "secret-launch-token-12345",
            ["MMA_CLI_MODE"] = "local",
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        using var badReq = new HttpRequestMessage(HttpMethod.Post, "/api/session/exchange")
        {
            Content = JsonContent.Create(new { token = "wrong-token" }),
        };
        badReq.Headers.Host = "localhost:5080";
        badReq.Headers.Add("Origin", "http://localhost:5080");
        var badResponse = await client.SendAsync(badReq);
        Assert.Equal(HttpStatusCode.Unauthorized, badResponse.StatusCode);

        using var goodReq = new HttpRequestMessage(HttpMethod.Post, "/api/session/exchange")
        {
            Content = JsonContent.Create(new { token = "secret-launch-token-12345" }),
        };
        goodReq.Headers.Host = "localhost:5080";
        goodReq.Headers.Add("Origin", "http://localhost:5080");
        var goodResponse = await client.SendAsync(goodReq);

        Assert.Equal(HttpStatusCode.NoContent, goodResponse.StatusCode);
        Assert.True(goodResponse.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var cookieHeader = Assert.Single(setCookies);
        Assert.Contains("mma_ui_session=", cookieHeader);
        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookieHeader, StringComparison.OrdinalIgnoreCase);

        var cookiePair = cookieHeader.Split(';')[0];
        using var authedReq = new HttpRequestMessage(HttpMethod.Get, "/api/does-not-exist");
        authedReq.Headers.Host = "localhost:5080";
        authedReq.Headers.Add("Origin", "http://localhost:5080");
        authedReq.Headers.Add("Cookie", cookiePair);

        var authedResponse = await client.SendAsync(authedReq);
        // Passes auth middleware and reaches the API 404 fallback instead of 401.
        Assert.Equal(HttpStatusCode.NotFound, authedResponse.StatusCode);
    }

    [Fact]
    public async Task LocalCliMode_AllowsLoopbackDbTarget_ButStillBlocksCloudMetadataIp()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["MMA_CLI_MODE"] = "local",
        });

        var policy = factory.Services.GetRequiredService<IDatabaseTargetPolicy>();

        var loopbackTarget = await policy.ValidateAndResolveAsync(
            "127.0.0.1",
            5432,
            "http://localhost:5080",
            "localhost:5080",
            CancellationToken.None);
        Assert.Equal(IPAddress.Loopback, loopbackTarget.Address);

        var metadataEx = await Assert.ThrowsAsync<DatabaseIntrospectionException>(() =>
            policy.ValidateAndResolveAsync(
                "169.254.169.254",
                5432,
                "http://localhost:5080",
                "localhost:5080",
                CancellationToken.None));
        Assert.Equal("target_not_allowed", metadataEx.Code);
    }
}
