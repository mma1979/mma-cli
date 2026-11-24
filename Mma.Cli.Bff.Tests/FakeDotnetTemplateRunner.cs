using Mma.Cli.Shared.Solutions;

namespace Mma.Cli.Bff.Tests;

internal sealed class FakeDotnetTemplateRunner : IDotnetTemplateRunner
{
    public Task<bool> IsTemplateInstalledAsync(string shortName, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<TemplateCommandResult> InstallPackageAsync(
        string packageId,
        string? version = null,
        string? nugetSource = null,
        bool force = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TemplateCommandResult(true, "Installed", "", 0));

    public Task<TemplateCommandResult> InstantiateAsync(
        string shortName,
        string solutionName,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, $"{solutionName}.sln"), string.Empty);

        var projectsPath = Path.Combine(outputDirectory, solutionName);
        var corePath = Path.Combine(projectsPath, $"{solutionName}.Core");
        var efPath = Path.Combine(projectsPath, $"{solutionName}.EntityFramework");
        var servicesPath = Path.Combine(projectsPath, $"{solutionName}.Services");
        var apiPath = Path.Combine(projectsPath, $"{solutionName}.AppApi");

        Directory.CreateDirectory(Path.Combine(corePath, "Database", "Tables"));
        Directory.CreateDirectory(Path.Combine(corePath, "Models"));
        Directory.CreateDirectory(Path.Combine(corePath, "Validations"));
        Directory.CreateDirectory(Path.Combine(efPath, "EntityConfigurations"));
        Directory.CreateDirectory(servicesPath);
        Directory.CreateDirectory(Path.Combine(apiPath, "Controllers", "v1"));

        if (string.Equals(shortName, "mma-sln-automapper", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(
                Path.Combine(corePath, "MappingProfile.cs"),
                """
                using AutoMapper;

                namespace Template.Core;

                public class MappingProfile : Profile
                {
                    public MappingProfile()
                    {
                        CreateMap<object, object>();
                    }
                }
                """);
        }

        File.WriteAllText(
            Path.Combine(efPath, "ApplicationDbContext.cs"),
            """
            public class ApplicationDbContext
            {
                public virtual DbSet<object> SeedItems { get; set; }
                protected void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.ApplyConfiguration(new object());
                }
            }
            """);

        File.WriteAllText(
            Path.Combine(servicesPath, "ServicesDI.cs"),
            """
            public static class ServicesDI
            {
                public static void RegisterServices(IServiceCollection services)
                {
                    // #region Services
                    // #endregion
                }
            }
            """);

        return Task.FromResult(new TemplateCommandResult(true, "Created", "", 0));
    }
}
