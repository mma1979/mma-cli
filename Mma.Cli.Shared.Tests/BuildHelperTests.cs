using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Exceptions;
using Mma.Cli.Shared.Helpers;

using Xunit;

namespace Mma.Cli.Shared.Tests;

public sealed class BuildHelperTests : IDisposable
{
    private readonly string _tempRoot;

    public BuildHelperTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mma-cli-helper-tests-{Guid.NewGuid():N}");
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
    public void CheckSolutionPath_MissingSolution_ThrowsMmaCliException_DoesNotExitProcess()
    {
        var emptyDir = Path.Combine(_tempRoot, "Empty");
        Directory.CreateDirectory(emptyDir);

        var ex = Assert.Throws<MmaCliException>(() => BuildHelper.CheckSolutionPath(emptyDir));
        Assert.Equal("invalid_solution_path", ex.Code);
        Assert.NotEqual(0, ex.ExitCode);
    }

    [Fact]
    public void CheckSolutionPath_SupportsSlnFile()
    {
        var slnDir = Path.Combine(_tempRoot, "ClassicSln");
        Directory.CreateDirectory(slnDir);
        File.WriteAllText(Path.Combine(slnDir, "MyApp.sln"), string.Empty);

        var (solutionName, projectsPath) = BuildHelper.CheckSolutionPath(slnDir);

        Assert.Equal("MyApp", solutionName);
        Assert.Equal(Path.Combine(slnDir, "MyApp"), projectsPath);
    }

    [Fact]
    public void CheckSolutionPath_SupportsSlnxFile()
    {
        var slnxDir = Path.Combine(_tempRoot, "ModernSlnx");
        Directory.CreateDirectory(slnxDir);
        File.WriteAllText(Path.Combine(slnxDir, "MyModernApp.slnx"), "<Solution />");

        var (solutionName, projectsPath) = BuildHelper.CheckSolutionPath(slnxDir);

        Assert.Equal("MyModernApp", solutionName);
        Assert.Equal(Path.Combine(slnxDir, "MyModernApp"), projectsPath);
    }

    [Fact]
    public void PropertiesBuilder_InsufficientArgs_ThrowsMmaCliException_DoesNotExitProcess()
    {
        var ex = Assert.Throws<MmaCliException>(() =>
            PropertiesBuilder.New(["g", "p", "Product"], Mappers.AutoMapper, _tempRoot));

        Assert.Equal("invalid_arguments", ex.Code);
        Assert.Equal(2, ex.ExitCode);
    }

    [Fact]
    public void RelationsBuilder_InsufficientArgs_ThrowsMmaCliException_DoesNotExitProcess()
    {
        var ex = Assert.Throws<MmaCliException>(() =>
            RelationsBuilder.New(["g", "r", "Parent", "Child"], _tempRoot));

        Assert.Equal("invalid_arguments", ex.Code);
        Assert.Equal(2, ex.ExitCode);
    }
}
