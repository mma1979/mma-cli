using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;
using Mma.Cli.Shared.Solutions;

using Xunit;

namespace Mma.Cli.Shared.Tests;

[Collection("CurrentDirectory")]
public sealed class SolutionAndEntityBuilderTests : IDisposable
{
    private readonly string _tempRoot;

    public SolutionAndEntityBuilderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-cli-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of temp directory.
        }
    }

    [Fact]
    public async Task CreateSolution_AutoMapper_ScaffoldsExpectedSlnAndMmaFolder()
    {
        const string solutionName = "Acme.Orders";
        var originalCwd = Directory.GetCurrentDirectory();
        var fakeRunner = new FakeTemplateRunner();

        try
        {
            Directory.SetCurrentDirectory(_tempRoot);

            var builder = SolutionBuilder.New(solutionName, Mappers.AutoMapper)
                .CreateSolutionDirectory();
            await builder.ScaffoldFromTemplateAsync(fakeRunner);
            builder.CreateMmaFolder();

            var solutionPath = Path.Combine(_tempRoot, solutionName);
            Assert.Equal(solutionPath, builder.SolutionPath);
            Assert.True(File.Exists(Path.Combine(solutionPath, $"{solutionName}.sln")));
            Assert.True(File.Exists(Path.Combine(solutionPath, ".mma", "project.mma")));
            Assert.Equal(Mappers.AutoMapper, BuildHelper.DetectMapper(solutionPath));
            Assert.Equal(1, fakeRunner.InstallCalls);
            Assert.Equal("Mma.Cli.Solutions.AutoMapper", fakeRunner.LastInstalledPackageId);
            Assert.Equal("mma-sln-automapper", fakeRunner.LastShortName);

            var (resolvedName, resolvedProjectsPath) = BuildHelper.CheckSolutionPath(solutionPath);
            Assert.Equal(solutionName, resolvedName);
            Assert.Equal(Path.Combine(solutionPath, solutionName), resolvedProjectsPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }
    }

    [Fact]
    public async Task GenerateEntity_AndProperty_ProducesExpectedFiles()
    {
        const string solutionName = "Acme.Catalog";
        var originalCwd = Directory.GetCurrentDirectory();
        var solutionPath = Path.Combine(_tempRoot, solutionName);
        var fakeRunner = new FakeTemplateRunner();

        try
        {
            Directory.SetCurrentDirectory(_tempRoot);

            var builder = SolutionBuilder.New(solutionName, Mappers.AutoMapper)
                .CreateSolutionDirectory();
            await builder.ScaffoldFromTemplateAsync(fakeRunner);
            builder.CreateMmaFolder();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }

        var entityArgs = new[] { "g", "e", "Product", PkTypes.GUID, Flags.MapperFlag, Mappers.AutoMapper };
        EntityBuilder.New(entityArgs, solutionPath)
            .GenerateModels()
            .GenerateValidator()
            .GenerateEntity()
            .GenerateEntityConfig()
            .DbContextMapping()
            .GenerateService()
            .GenerateController(true);

        var projectsPath = Path.Combine(solutionPath, solutionName);
        var entityFile = Path.Combine(projectsPath, $"{solutionName}.Core", "Database", "Tables", "Product.cs");
        var modifyModelFile = Path.Combine(projectsPath, $"{solutionName}.Core", "Models", "ProductModifyModel.cs");
        var readModelFile = Path.Combine(projectsPath, $"{solutionName}.Core", "Models", "ProductReadModel.cs");

        Assert.True(File.Exists(entityFile), $"Expected generated entity file at {entityFile}");
        Assert.True(File.Exists(modifyModelFile), $"Expected generated modify model at {modifyModelFile}");
        Assert.True(File.Exists(readModelFile), $"Expected generated read model at {readModelFile}");

        var propArgs = new[] { "g", "p", "Product", "Title", PkTypes.STRING, "false" };
        PropertiesBuilder.New(propArgs, Mappers.AutoMapper, solutionPath)
            .UpdateEntityModels()
            .UpdateEntity()
            .UpdateEntityConfig();

        var entityContent = File.ReadAllText(entityFile);
        Assert.Contains("Title", entityContent);
    }

    [Fact]
    public void DetectMapper_ReturnsMapster_WhenMappingProfileDoesNotExist()
    {
        var emptySolutionDir = Path.Combine(_tempRoot, "MapsterProbe");
        Directory.CreateDirectory(emptySolutionDir);

        Assert.Equal(Mappers.Mapster, BuildHelper.DetectMapper(emptySolutionDir));
    }

    private sealed class FakeTemplateRunner : IDotnetTemplateRunner
    {
        private readonly HashSet<string> _installed = new(StringComparer.OrdinalIgnoreCase);

        public int InstallCalls { get; private set; }
        public string? LastInstalledPackageId { get; private set; }
        public string? LastShortName { get; private set; }

        public Task<bool> IsTemplateInstalledAsync(string shortName, CancellationToken cancellationToken = default) =>
            Task.FromResult(_installed.Contains(shortName));

        public Task<TemplateCommandResult> InstallPackageAsync(
            string packageId,
            string? version = null,
            string? nugetSource = null,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            LastInstalledPackageId = packageId;
            if (packageId.Contains("Mapster", StringComparison.OrdinalIgnoreCase))
            {
                _installed.Add("mma-sln-mapster");
            }
            else
            {
                _installed.Add("mma-sln-automapper");
            }

            return Task.FromResult(new TemplateCommandResult(true, "Installed", "", 0));
        }

        public Task<TemplateCommandResult> InstantiateAsync(
            string shortName,
            string solutionName,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            LastShortName = shortName;
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
}
