using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mma.Cli.Database;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMmaDatabase(this IServiceCollection services, string? databasePath = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var resolvedPath = WorkspaceDbPathResolver.Resolve(databasePath);

        services.AddDbContext<WorkspaceDbContext>(options =>
            options.UseSqlite($"Data Source={resolvedPath}"));
        services.AddScoped<IWorkspaceRegistry, SqliteWorkspaceRegistry>();

        return services;
    }
}
