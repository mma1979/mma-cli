using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mma.Cli.Database.Introspection;

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

        services.AddSingleton<SchemaNamingService>();
        services.AddSingleton<SqliteIntrospector>();
        services.AddSingleton<SqlServerIntrospector>();
        services.AddSingleton<PostgresIntrospector>();
        services.AddSingleton<MySqlIntrospector>();
        services.AddSingleton<IDatabaseIntrospector>(sp => sp.GetRequiredService<SqliteIntrospector>());
        services.AddSingleton<IDatabaseIntrospector>(sp => sp.GetRequiredService<SqlServerIntrospector>());
        services.AddSingleton<IDatabaseIntrospector>(sp => sp.GetRequiredService<PostgresIntrospector>());
        services.AddSingleton<IDatabaseIntrospector>(sp => sp.GetRequiredService<MySqlIntrospector>());
        services.AddSingleton<IDatabaseIntrospectorFactory, DatabaseIntrospectorFactory>();

        return services;
    }
}
