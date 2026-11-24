using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Helpers;

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
    public void CreateSolution_AutoMapper_ScaffoldsExpectedSlnAndMmaFolder()
    {
        const string solutionName = "Acme.Orders";
        var originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(_tempRoot);

            var builder = SolutionBuilder.New(solutionName, Mappers.AutoMapper)
                .CreateSolutionDirectory()
                .ExtractSolution()
                .RootRenameAndReplace()
                .RenameProjects()
                .RenameCsprojFiles()
                .ReplaceNamespaces()
                .CreateMmaFolder();

            var solutionPath = Path.Combine(_tempRoot, solutionName);
            Assert.Equal(solutionPath, builder.SolutionPath);
            Assert.True(File.Exists(Path.Combine(solutionPath, $"{solutionName}.sln")));
            Assert.True(File.Exists(Path.Combine(solutionPath, ".mma", "project.mma")));
            Assert.Equal(Mappers.AutoMapper, BuildHelper.DetectMapper(solutionPath));

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
    public void GenerateEntity_AndProperty_ProducesExpectedFiles()
    {
        const string solutionName = "Acme.Catalog";
        var originalCwd = Directory.GetCurrentDirectory();
        var solutionPath = Path.Combine(_tempRoot, solutionName);

        try
        {
            Directory.SetCurrentDirectory(_tempRoot);

            SolutionBuilder.New(solutionName, Mappers.AutoMapper)
                .CreateSolutionDirectory()
                .ExtractSolution()
                .RootRenameAndReplace()
                .RenameProjects()
                .RenameCsprojFiles()
                .ReplaceNamespaces()
                .CreateMmaFolder();
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
}
