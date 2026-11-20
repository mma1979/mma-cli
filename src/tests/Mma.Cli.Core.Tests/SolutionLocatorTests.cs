using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Tests;

public sealed class SolutionLocatorTests : IDisposable
{
    private readonly string _tempRoot;

    public SolutionLocatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-locator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Locate_WhenSlnExists_ReturnsSlnSolutionInfo()
    {
        var slnFile = Path.Combine(_tempRoot, "MyApp.sln");
        File.WriteAllText(slnFile, string.Empty);

        var locator = new SolutionLocator();
        var result = locator.Locate(_tempRoot);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("MyApp", result.Value.SolutionName);
        Assert.Equal(".sln", result.Value.SolutionFormat);
        Assert.Equal(Path.GetFullPath(_tempRoot), result.Value.SolutionPath);
        Assert.Equal(Path.Combine(Path.GetFullPath(_tempRoot), "MyApp"), result.Value.ProjectsPath);
        Assert.Equal(slnFile, result.Value.SolutionFilePath);
    }

    [Fact]
    public void Locate_WhenSlnxExists_ReturnsSlnxSolutionInfo()
    {
        var slnxFile = Path.Combine(_tempRoot, "ModernApp.slnx");
        File.WriteAllText(slnxFile, "<Solution />");

        var locator = new SolutionLocator();
        var result = locator.Locate(_tempRoot);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("ModernApp", result.Value.SolutionName);
        Assert.Equal(".slnx", result.Value.SolutionFormat);
        Assert.Equal(slnxFile, result.Value.SolutionFilePath);
    }

    [Fact]
    public void Locate_WhenBothSlnxAndSlnExist_PrefersSlnx()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "LegacyApp.sln"), string.Empty);
        var slnxFile = Path.Combine(_tempRoot, "PreferredApp.slnx");
        File.WriteAllText(slnxFile, "<Solution />");

        var locator = new SolutionLocator();
        var result = locator.Locate(_tempRoot);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("PreferredApp", result.Value.SolutionName);
        Assert.Equal(".slnx", result.Value.SolutionFormat);
        Assert.Equal(slnxFile, result.Value.SolutionFilePath);
    }

    [Fact]
    public void Locate_WhenDirectoryDoesNotExist_ReturnsSolutionNotFound()
    {
        var missingDir = Path.Combine(_tempRoot, "DoesNotExist");

        var locator = new SolutionLocator();
        var result = locator.Locate(missingDir);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SOLUTION_NOT_FOUND, result.Error?.Code);
    }

    [Fact]
    public void Locate_WhenNoSolutionFileExists_ReturnsSolutionNotFound()
    {
        var locator = new SolutionLocator();
        var result = locator.Locate(_tempRoot);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SOLUTION_NOT_FOUND, result.Error?.Code);
    }

    [Fact]
    public void Locate_WhenMappingProfileExistsWithoutManifest_DetectsAutoMapper()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "AutoApp.sln"), string.Empty);
        var coreDir = Path.Combine(_tempRoot, "AutoApp", "AutoApp.Core");
        Directory.CreateDirectory(coreDir);
        File.WriteAllText(Path.Combine(coreDir, "MappingProfile.cs"), "public class MappingProfile {}");

        var locator = new SolutionLocator();
        var result = locator.Locate(_tempRoot);

        Assert.True(result.IsSuccess);
        Assert.Equal(MapperKind.AutoMapper, result.Value!.DetectedMapper);
    }

    [Fact]
    public void Locate_WhenNoMappingProfileAndNoManifest_DetectsMapster()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "MapApp.sln"), string.Empty);

        var locator = new SolutionLocator();
        var result = locator.Locate(_tempRoot);

        Assert.True(result.IsSuccess);
        Assert.Equal(MapperKind.Mapster, result.Value!.DetectedMapper);
    }

    [Theory]
    [InlineData("AutoMapper", MapperKind.AutoMapper)]
    [InlineData("Mapster", MapperKind.Mapster)]
    public void Locate_WhenManifestSpecifiesMapper_UsesManifestMapper(string manifestMapper, MapperKind expected)
    {
        File.WriteAllText(Path.Combine(_tempRoot, "ManifestApp.slnx"), "<Solution />");
        var mmaDir = Path.Combine(_tempRoot, ".mma");
        Directory.CreateDirectory(mmaDir);
        File.WriteAllText(Path.Combine(mmaDir, "project.mma"), $$"""{"mapper":"{{manifestMapper}}"}""");

        var locator = new SolutionLocator();
        var result = locator.Locate(_tempRoot);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value!.DetectedMapper);
    }

    [Fact]
    public void Locate_WhenManifestIsCorruptJson_FallsBackToFilesystemDetection()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "CorruptApp.sln"), string.Empty);
        var mmaDir = Path.Combine(_tempRoot, ".mma");
        Directory.CreateDirectory(mmaDir);
        File.WriteAllText(Path.Combine(mmaDir, "project.mma"), "{ not valid json");

        var coreDir = Path.Combine(_tempRoot, "CorruptApp", "CorruptApp.Core");
        Directory.CreateDirectory(coreDir);
        File.WriteAllText(Path.Combine(coreDir, "MappingProfile.cs"), string.Empty);

        var locator = new SolutionLocator();
        var result = locator.Locate(_tempRoot);

        Assert.True(result.IsSuccess);
        Assert.Equal(MapperKind.AutoMapper, result.Value!.DetectedMapper);
    }
}
