using System.Diagnostics;
using System.Text.Json.Serialization;

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
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
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

builder.Services.AddSingleton(new DatabaseTargetPolicyOptions
{
    AllowedOrigins = allowedOrigins.ToArray(),
    AllowLoopbackTargets = builder.Configuration.GetValue<bool>("Introspection:AllowLoopbackTargets"),
    AllowPrivateNetworkTargets = builder.Configuration.GetValue<bool>("Introspection:AllowPrivateNetworkTargets"),
});
builder.Services.AddSingleton<IDnsHostResolver, SystemDnsHostResolver>();
builder.Services.AddSingleton<IDatabaseTargetPolicy, DatabaseTargetPolicy>();
builder.Services.AddSingleton<IDatabaseIntrospectionProvider, PostgreSqlIntrospectionProvider>();
builder.Services.AddSingleton<IDatabaseIntrospectionProvider, MySqlIntrospectionProvider>();
builder.Services.AddSingleton<IDatabaseIntrospectionProvider, SqlServerIntrospectionProvider>();
builder.Services.AddSingleton<IDatabaseIntrospectionService, DatabaseIntrospectionService>();

var app = builder.Build();

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
