using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public sealed class DatabaseIntrospectionEndpointTests
{
    private const string DefaultOrigin = "http://localhost:5080";

    private static readonly IntrospectionRequest ValidRequest = new()
    {
        Engine = "postgresql", Host = "db.example.test", Port = 5432,
        Database = "appdb", Username = "reader"
    };

    private static HttpClient CreateOriginClient(WebApplicationFactory<Program> factory, string? origin = DefaultOrigin)
    {
        var client = factory.CreateClient();
        if (origin is not null)
        {
            client.DefaultRequestHeaders.Add("Origin", origin);
        }
        return client;
    }

    [Fact]
    public async Task Returns_exact_success_contract_including_empty_collections()
    {
        var expected = new IntrospectionResult("appdb", "postgresql", [], [], []);
        using var factory = new ApiFactory((_, _) => Task.FromResult(expected));
        var response = await CreateOriginClient(factory).PostAsJsonAsync("/api/db/introspect", ValidRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"database":"appdb","engine":"postgresql","tables":[],"relations":[],"warnings":[]}""", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("general", 5432, "unsupported_engine")]
    [InlineData("postgresql", 0, "invalid_port")]
    public async Task Invalid_request_returns_400_error_shape(string engine, int port, string code)
    {
        using var factory = new ApiFactory((_, _) => throw new InvalidOperationException("service should not run"));
        var request = ValidRequest with { Engine = engine, Port = port };
        var response = await CreateOriginClient(factory).PostAsJsonAsync("/api/db/introspect", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertError(response, code, "provider diagnostic");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Missing_or_empty_password_is_accepted_and_ssl_defaults_on(string? password)
    {
        IntrospectionRequest? received = null;
        using var factory = new ApiFactory((request, _) =>
        {
            received = request;
            return Task.FromResult(new IntrospectionResult("appdb", "postgresql", [], [], []));
        });
        var request = ValidRequest with { Password = password };
        var response = await CreateOriginClient(factory).PostAsJsonAsync("/api/db/introspect", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(received?.Ssl);
    }

    [Theory]
    [InlineData("host", 253)]
    [InlineData("database", 128)]
    [InlineData("username", 128)]
    [InlineData("password", 512)]
    [InlineData("schemaFilter", 128)]
    public async Task Exact_length_limits_are_accepted_and_trimmed_fields_are_normalized(string field, int limit)
    {
        IntrospectionRequest? received = null;
        using var factory = new ApiFactory((request, _) =>
        {
            received = request;
            return Task.FromResult(new IntrospectionResult("appdb", "postgresql", [], [], []));
        });
        var response = await CreateOriginClient(factory).PostAsJsonAsync("/api/db/introspect", WithFieldLength(field, limit, padTrimmedField: true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(received);
        Assert.Equal(limit, GetFieldLength(received!, field));
    }

    [Theory]
    [InlineData("host", 254)]
    [InlineData("database", 129)]
    [InlineData("username", 129)]
    [InlineData("password", 513)]
    [InlineData("schemaFilter", 129)]
    public async Task Over_limit_fields_return_safe_invalid_request(string field, int length)
    {
        using var factory = new ApiFactory((_, _) => throw new InvalidOperationException("service should not run"));
        var response = await CreateOriginClient(factory).PostAsJsonAsync("/api/db/introspect", WithFieldLength(field, length));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertError(response, "invalid_request", "provider diagnostic");
    }

    [Fact]
    public async Task Unknown_json_member_returns_safe_invalid_request()
    {
        using var factory = new ApiFactory((_, _) => throw new InvalidOperationException("service should not run"));
        const string body = """{"engine":"postgresql","host":"db.example.test","port":5432,"database":"appdb","username":"reader","unexpected":"secret"}""";
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await CreateOriginClient(factory).PostAsync("/api/db/introspect", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertError(response, "invalid_request", "secret");
    }

    [Theory]
    [InlineData(422, "authentication_failed")]
    [InlineData(502, "database_unreachable")]
    [InlineData(504, "connection_timeout")]
    public async Task Typed_failure_is_safe_and_keeps_error_property(int status, string code)
    {
        using var factory = new ApiFactory((_, _) => throw new DatabaseIntrospectionException("provider diagnostic secret", status, code));
        var response = await CreateOriginClient(factory).PostAsJsonAsync("/api/db/introspect", ValidRequest);
        Assert.Equal(status, (int)response.StatusCode);
        await AssertError(response, code, "provider diagnostic secret");
    }

    [Fact]
    public async Task Untrusted_or_missing_origin_returns_403_origin_not_allowed_without_invoking_service()
    {
        var serviceInvoked = false;
        using var factory = new ApiFactory((_, _) =>
        {
            serviceInvoked = true;
            return Task.FromResult(new IntrospectionResult("appdb", "postgresql", [], [], []));
        });

        var missingOriginResp = await CreateOriginClient(factory, origin: null).PostAsJsonAsync("/api/db/introspect", ValidRequest);
        Assert.Equal(HttpStatusCode.Forbidden, missingOriginResp.StatusCode);
        await AssertError(missingOriginResp, "origin_not_allowed", "localhost");

        var evilOriginResp = await CreateOriginClient(factory, origin: "https://evil.example.com").PostAsJsonAsync("/api/db/introspect", ValidRequest);
        Assert.Equal(HttpStatusCode.Forbidden, evilOriginResp.StatusCode);
        await AssertError(evilOriginResp, "origin_not_allowed", "evil.example.com");

        Assert.False(serviceInvoked);
    }

    [Fact]
    public async Task Blocked_target_returns_403_target_not_allowed_without_invoking_service()
    {
        var serviceInvoked = false;
        using var factory = new ApiFactory((_, _) =>
        {
            serviceInvoked = true;
            return Task.FromResult(new IntrospectionResult("appdb", "postgresql", [], [], []));
        });

        var request = ValidRequest with { Host = "127.0.0.1" };
        var response = await CreateOriginClient(factory).PostAsJsonAsync("/api/db/introspect", request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertError(response, "target_not_allowed", "127.0.0.1");
        Assert.False(serviceInvoked);
    }

    [Fact]
    public async Task Third_concurrent_introspection_returns_429_introspection_busy_with_retry_after()
    {
        var bothEntered = new CountdownEvent(2);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var factory = new ProviderFactory(new BlockingProvider("postgresql", bothEntered, releaseGate));
        var client = CreateOriginClient(factory);

        var first = client.PostAsJsonAsync("/api/db/introspect", ValidRequest);
        var second = client.PostAsJsonAsync("/api/db/introspect", ValidRequest);

        Assert.True(bothEntered.Wait(TimeSpan.FromSeconds(5)));

        var third = await client.PostAsJsonAsync("/api/db/introspect", ValidRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("1", third.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        await AssertError(third, "introspection_busy", "semaphore");

        releaseGate.SetResult();
        var responses = await Task.WhenAll(first, second);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task Unknown_api_path_returns_json_404_not_spa_html()
    {
        using var factory = new ApiFactory((_, _) => Task.FromResult(new IntrospectionResult("appdb", "postgresql", [], [], [])));
        var response = await factory.CreateClient().GetAsync("/api/unknown");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("<html", (await response.Content.ReadAsStringAsync()).ToLowerInvariant());
    }

    private static IntrospectionRequest WithFieldLength(string field, int length, bool padTrimmedField = false)
    {
        var value = field == "host"
            ? BuildValidHostnameOfLength(length)
            : new string('x', length);
        var paddedValue = padTrimmedField ? $" {value} " : value;
        return field switch
        {
            "host" => ValidRequest with { Host = paddedValue },
            "database" => ValidRequest with { Database = paddedValue },
            "username" => ValidRequest with { Username = paddedValue },
            "password" => ValidRequest with { Password = value },
            "schemaFilter" => ValidRequest with { SchemaFilter = paddedValue },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static string BuildValidHostnameOfLength(int length)
    {
        var labels = new List<string>();
        var remaining = length;
        while (remaining > 63)
        {
            var take = Math.Min(63, remaining - 2);
            labels.Add(new string('a', take));
            remaining -= take + 1;
        }
        labels.Add(new string('b', remaining));
        return string.Join('.', labels);
    }

    private static int GetFieldLength(IntrospectionRequest request, string field) => field switch
    {
        "host" => request.Host.Length,
        "database" => request.Database.Length,
        "username" => request.Username.Length,
        "password" => request.Password?.Length ?? 0,
        "schemaFilter" => request.SchemaFilter?.Length ?? 0,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private static async Task AssertError(HttpResponseMessage response, string code, string leakedText)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.TryGetProperty("error", out var error));
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(leakedText, error.GetString());
    }

    private sealed class ApiFactory(Func<IntrospectionRequest, ValidatedDatabaseTarget, Task<IntrospectionResult>> handler) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDnsHostResolver>();
            services.AddSingleton<IDnsHostResolver>(new StaticDnsResolver(IPAddress.Parse("93.184.216.34")));
            services.RemoveAll<IDatabaseIntrospectionService>();
            services.AddSingleton<IDatabaseIntrospectionService>(new FakeService(handler));
        });
    }

    private sealed class ProviderFactory(IDatabaseIntrospectionProvider provider) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDnsHostResolver>();
            services.AddSingleton<IDnsHostResolver>(new StaticDnsResolver(IPAddress.Parse("93.184.216.34")));
            services.RemoveAll<IDatabaseIntrospectionProvider>();
            services.AddSingleton(provider);
        });
    }

    private sealed class StaticDnsResolver(IPAddress address) : IDnsHostResolver
    {
        public Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { address });
    }

    private sealed class FakeService(Func<IntrospectionRequest, ValidatedDatabaseTarget, Task<IntrospectionResult>> handler) : IDatabaseIntrospectionService
    {
        public Task<IntrospectionResult> IntrospectAsync(
            IntrospectionRequest request,
            ValidatedDatabaseTarget target,
            CancellationToken cancellationToken) => handler(request, target);
    }

    private sealed class BlockingProvider(string engine, CountdownEvent entered, TaskCompletionSource releaseGate) : IDatabaseIntrospectionProvider
    {
        public string Engine => engine;

        public async Task<IntrospectionResult> IntrospectAsync(
            IntrospectionRequest request,
            ValidatedDatabaseTarget target,
            CancellationToken cancellationToken)
        {
            entered.Signal();
            await releaseGate.Task.WaitAsync(cancellationToken);
            return new IntrospectionResult(request.Database, Engine, [], [], []);
        }
    }
}
