using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Mma.Cli.Bff.Endpoints;
using Mma.Cli.Bff.Jobs;
using Mma.Cli.Bff.Security;
using Mma.Cli.Bff.Services;
using Mma.Cli.Core;
using Mma.Cli.Database;

var builder = WebApplication.CreateBuilder(args);

var configuredUrls = builder.Configuration[WebHostDefaults.ServerUrlsKey];
if (string.IsNullOrWhiteSpace(configuredUrls)
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5180");
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton(sp => BffSecurityOptions.LoadFromConfiguration(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<BootstrapSessionManager>();
builder.Services.AddSingleton<WorkspacePathGuard>();
builder.Services.AddScoped<IMmaIdentityContext, LocalSingleUserIdentity>();
builder.Services.AddSingleton<IBackgroundJobCoordinator, BackgroundJobCoordinator>();
builder.Services.AddScoped<BffProjectMutationService>();

builder.Services.AddMmaCore();
builder.Services.AddMmaDatabase();

// Re-bind WorkspaceDbContext lazily from IConfiguration so test/host overrides
// applied via ConfigureAppConfiguration take effect before DbContext initialization.
for (var i = builder.Services.Count - 1; i >= 0; i--)
{
    var serviceType = builder.Services[i].ServiceType;
    if (serviceType == typeof(DbContextOptions<WorkspaceDbContext>)
        || serviceType == typeof(DbContextOptions)
        || (serviceType.IsGenericType
            && serviceType.GenericTypeArguments.Length == 1
            && serviceType.GenericTypeArguments[0] == typeof(WorkspaceDbContext)
            && serviceType.Name.Contains("DbContextOptionsConfiguration", StringComparison.Ordinal)))
    {
        builder.Services.RemoveAt(i);
    }
}

builder.Services.AddDbContext<WorkspaceDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var configuredDbPath = config["Bff:WorkspaceDbPath"] ?? config["WorkspaceDbPath"];
    var resolvedDbPath = WorkspaceDbPathResolver.Resolve(configuredDbPath);
    options.UseSqlite($"Data Source={resolvedDbPath}");
});

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var registry = scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
    await registry.EnsureInitializedAsync().ConfigureAwait(false);
}

app.UseMiddleware<LocalhostSecurityMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapHealthAndAuthEndpoints();
app.MapWorkspaceEndpoints();
app.MapProjectEndpoints();
app.MapDbImportAndTemplateEndpoints();

await app.RunAsync().ConfigureAwait(false);

public partial class Program;
