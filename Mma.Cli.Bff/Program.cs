using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

using Mma.Cli.Bff.Persistence;
using Mma.Cli.Bff.Security;
using Mma.Cli.Shared.Data;

var currentDirectory = Directory.GetCurrentDirectory();
var webRootCandidates = new[]
{
    Path.Combine(currentDirectory, "wwwroot"),
    Path.Combine(currentDirectory, "Mma.Cli.Bff", "wwwroot"),
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),
};
var webRoot = webRootCandidates.FirstOrDefault(Directory.Exists)
    ?? webRootCandidates[^1];
Directory.CreateDirectory(webRoot);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = webRoot,
});

var configuredUrls = builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(configuredUrls))
{
    configuredUrls = "http://127.0.0.1:5080";
    builder.WebHost.UseUrls(configuredUrls);
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "http://localhost:5080",
    "http://127.0.0.1:5080",
    "http://[::1]:5080",
    "https://localhost:44361"
};

foreach (var rawUrl in configuredUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
{
    if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
    {
        allowedOrigins.Add($"{uri.Scheme}://localhost:{uri.Port}");
        allowedOrigins.Add($"{uri.Scheme}://127.0.0.1:{uri.Port}");
        allowedOrigins.Add($"{uri.Scheme}://[::1]:{uri.Port}");
    }
}

if (builder.Environment.IsDevelopment())
{
    allowedOrigins.Add("http://localhost:5173");
    allowedOrigins.Add("http://127.0.0.1:5173");
    allowedOrigins.Add("http://[::1]:5173");
}

var configuredOrigins = builder.Configuration.GetSection("Introspection:AllowedOrigins").Get<string[]>();
if (configuredOrigins is not null)
{
    foreach (var origin in configuredOrigins)
    {
        if (!string.IsNullOrWhiteSpace(origin) && !origin.Contains('*'))
        {
            allowedOrigins.Add(origin.Trim());
        }
    }
}

var appVersion = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "5.0.0";

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var cliMode = config["MMA_CLI_MODE"] ?? Environment.GetEnvironmentVariable("MMA_CLI_MODE");
    var isLocalCliMode = string.Equals(cliMode, "local", StringComparison.OrdinalIgnoreCase);
    var sessionToken = config["MMA_UI_TOKEN"] ?? Environment.GetEnvironmentVariable("MMA_UI_TOKEN");
    var initialPath = config["MMA_INITIAL_PATH"] ?? Environment.GetEnvironmentVariable("MMA_INITIAL_PATH");

    return new LocalSessionOptions
    {
        Token = sessionToken,
        Mode = isLocalCliMode ? "local-cli" : "standalone",
        InitialPath = string.IsNullOrWhiteSpace(initialPath) ? null : initialPath,
        Version = appVersion,
    };
});

builder.Services.AddDbContext<CliDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var databasePath = UserDataPathResolver.ResolveDatabasePath(config);
    options.UseSqlite(UserDataPathResolver.BuildConnectionString(databasePath));
});

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();

    var origins = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase);
    if (env.IsDevelopment())
    {
        origins.Add("http://localhost:5173");
        origins.Add("http://127.0.0.1:5173");
        origins.Add("http://[::1]:5173");
    }

    var extraOrigins = config.GetSection("Introspection:AllowedOrigins").Get<string[]>();
    if (extraOrigins is not null)
    {
        foreach (var origin in extraOrigins)
        {
            if (!string.IsNullOrWhiteSpace(origin) && !origin.Contains('*'))
            {
                origins.Add(origin.Trim());
            }
        }
    }

    var cliMode = config["MMA_CLI_MODE"] ?? Environment.GetEnvironmentVariable("MMA_CLI_MODE");
    var isLocalCliMode = string.Equals(cliMode, "local", StringComparison.OrdinalIgnoreCase);
    var noLocalDb = config.GetValue<bool>("Introspection:NoLocalDb");
    var allowLoopbackTargets = config.GetValue<bool?>("Introspection:AllowLoopbackTargets")
        ?? (isLocalCliMode && !noLocalDb);
    var allowPrivateNetworkTargets = config.GetValue<bool?>("Introspection:AllowPrivateNetworkTargets")
        ?? (isLocalCliMode && !noLocalDb);

    return new DatabaseTargetPolicyOptions
    {
        AllowedOrigins = origins.ToArray(),
        AllowLoopbackTargets = allowLoopbackTargets,
        AllowPrivateNetworkTargets = allowPrivateNetworkTargets,
    };
});
builder.Services.AddSingleton<IDnsHostResolver, SystemDnsHostResolver>();
builder.Services.AddSingleton<IDatabaseTargetPolicy, DatabaseTargetPolicy>();
builder.Services.AddSingleton<IDatabaseIntrospectionProvider, PostgreSqlIntrospectionProvider>();
builder.Services.AddSingleton<IDatabaseIntrospectionProvider, MySqlIntrospectionProvider>();
builder.Services.AddSingleton<IDatabaseIntrospectionProvider, SqlServerIntrospectionProvider>();
builder.Services.AddSingleton<IDatabaseIntrospectionService, DatabaseIntrospectionService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CliDbContext>();
    var legacyDbPath = Path.Combine(webRoot, "data", "mma_cli.db");
    await LegacySqliteMigrator.InitializeAndMigrateAsync(dbContext, legacyDbPath);
}

app.UseMiddleware<LocalSessionAuthMiddleware>();
app.MapSessionAndHealthEndpoints();

app.MapPost("/api/db/introspect", async (
    HttpContext context,
    IDatabaseTargetPolicy targetPolicy,
    IDatabaseIntrospectionService service) =>
{
    var origin = context.Request.Headers.Origin.ToString();
    var requestHost = context.Request.Host.ToString();

    try
    {
        targetPolicy.ValidateOriginAndHost(origin, requestHost);
    }
    catch (DatabaseIntrospectionException exception)
    {
        return Error(exception.StatusCode, exception.Code, exception.SafeMessage);
    }

    IntrospectionRequest? request;
    try
    {
        request = await context.Request.ReadFromJsonAsync<IntrospectionRequest>(context.RequestAborted);
    }
    catch (System.Text.Json.JsonException)
    {
        return Error(400, "invalid_request", "The introspection request is invalid.");
    }

    if (request is null)
    {
        return Error(400, "invalid_request", "The introspection request is invalid.");
    }

    request = request with
    {
        Host = request.Host?.Trim() ?? string.Empty,
        Database = request.Database?.Trim() ?? string.Empty,
        Username = request.Username?.Trim() ?? string.Empty,
        SchemaFilter = request.SchemaFilter?.Trim(),
    };

    if (string.IsNullOrWhiteSpace(request.Engine) || string.IsNullOrWhiteSpace(request.Host)
        || string.IsNullOrWhiteSpace(request.Database) || string.IsNullOrWhiteSpace(request.Username)
        || request.Host.Length > 253 || request.Database.Length > 128 || request.Username.Length > 128
        || (request.Password?.Length ?? 0) > 512 || (request.SchemaFilter?.Length ?? 0) > 128)
    {
        return Error(400, "invalid_request", "The introspection request is invalid.");
    }

    if (request.Engine is not ("postgresql" or "mysql" or "mssql"))
    {
        return Error(400, "unsupported_engine", "The requested database engine is not supported.");
    }

    if (request.Port is < 1 or > 65535)
    {
        return Error(400, "invalid_port", "The database port is invalid.");
    }

    try
    {
        var validatedTarget = await targetPolicy.ValidateAndResolveAsync(
            request.Host,
            request.Port,
            origin,
            requestHost,
            context.RequestAborted);

        var result = await service.IntrospectAsync(
            request with { Ssl = request.Ssl ?? true },
            validatedTarget,
            context.RequestAborted);
        return Results.Ok(result);
    }
    catch (DatabaseIntrospectionException exception)
    {
        if (exception.Code == "introspection_busy")
        {
            context.Response.Headers.RetryAfter = "1";
        }

        return Error(exception.StatusCode, exception.Code, exception.SafeMessage);
    }
    catch (Exception)
    {
        return Error(500, "internal_error", "Database introspection failed.");
    }
});

app.MapMethods(
    "/api/{**path}",
    ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
    () => Results.Json(new ApiErrorDto("API endpoint not found.", "not_found", Guid.NewGuid().ToString("N")), statusCode: 404));

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("{*path:nonfile}", "index.html");

app.Run();

static IResult Error(int statusCode, string code, string message) =>
    Results.Json(new ApiErrorDto(message, code, Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")), statusCode: statusCode);

public partial class Program;
