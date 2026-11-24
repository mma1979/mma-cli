using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Exceptions;
using Mma.Cli.Shared.Solutions;

using Xunit;

namespace Mma.Cli.Shared.Tests;

public sealed class SolutionTemplateCatalogTests : IDisposable
{
    private readonly string _tempDir;

    public SolutionTemplateCatalogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mma-tpl-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void Resolve_ReturnsAutoMapper_ByDefaultAndForAutoMapper()
    {
        var explicitAuto = SolutionTemplateCatalog.Resolve(Mappers.AutoMapper);
        var unknown = SolutionTemplateCatalog.Resolve("unknown");
        var missing = SolutionTemplateCatalog.Resolve(null);

        Assert.Equal("Mma.Cli.Solutions.AutoMapper", explicitAuto.PackageId);
        Assert.Equal("mma-sln-automapper", explicitAuto.ShortName);
        Assert.Equal(explicitAuto, unknown);
        Assert.Equal(explicitAuto, missing);
    }

    [Fact]
    public void Resolve_ReturnsMapster_CaseInsensitively()
    {
        var mapster = SolutionTemplateCatalog.Resolve("mapster");

        Assert.Equal("Mma.Cli.Solutions.Mapster", mapster.PackageId);
        Assert.Equal("mma-sln-mapster", mapster.ShortName);
    }

    [Fact]
    public void SolutionBuilder_NewArgs_ParsesOptionalFlagsSafely()
    {
        var minimal = SolutionBuilder.New(["new", "Acme.Minimal"]);
        Assert.Equal("Acme.Minimal", minimal.SolutionName);
        Assert.Equal(Mappers.AutoMapper, minimal.Mapper);
        Assert.Null(minimal.TemplateSource);
        Assert.Null(minimal.TemplateVersion);

        var full = SolutionBuilder.New([
            "new", "Acme.Full",
            Flags.MapperFlag, "mapster",
            Flags.TemplateSourceFlag, "https://pkgs.example.com/v3/index.json",
            Flags.TemplateVersionFlag, "10.0.0-preview.2"
        ]);
        Assert.Equal("Acme.Full", full.SolutionName);
        Assert.Equal(Mappers.Mapster, full.Mapper);
        Assert.Equal("https://pkgs.example.com/v3/index.json", full.TemplateSource);
        Assert.Equal("10.0.0-preview.2", full.TemplateVersion);
    }

    [Fact]
    public void BuildInstallArguments_UsesLocalNupkg_WhenSourceFolderContainsPackage()
    {
        var nupkgPath = Path.Combine(_tempDir, "Mma.Cli.Solutions.AutoMapper.10.0.0-preview.1.nupkg");
        File.WriteAllText(nupkgPath, "fake-nupkg");

        var args = DotnetTemplateRunner.BuildInstallArguments(
            "Mma.Cli.Solutions.AutoMapper",
            "10.0.0-preview.1",
            _tempDir,
            force: true,
            customHivePath: Path.Combine(_tempDir, "hive"));

        Assert.Equal("new", args[0]);
        Assert.Equal("install", args[1]);
        Assert.Equal(Path.GetFullPath(nupkgPath), args[2]);
        Assert.Contains("--force", args);
        Assert.Contains("--debug:custom-hive", args);
        Assert.DoesNotContain("--nuget-source", args);
    }

    [Fact]
    public void BuildInstallArguments_UsesNugetSourceFlag_WhenSourceIsUrl()
    {
        var args = DotnetTemplateRunner.BuildInstallArguments(
            "Mma.Cli.Solutions.Mapster",
            "10.0.0-preview.1",
            "https://api.nuget.org/v3/index.json",
            force: false);

        Assert.Equal(
            ["new", "install", "Mma.Cli.Solutions.Mapster::10.0.0-preview.1", "--nuget-source", "https://api.nuget.org/v3/index.json"],
            args);
    }

    [Fact]
    public void CreateSolutionDirectory_Throws_WhenTargetDirectoryIsNotEmpty()
    {
        var existingDir = Path.Combine(_tempDir, "Acme.Existing");
        Directory.CreateDirectory(existingDir);
        File.WriteAllText(Path.Combine(existingDir, "README.md"), "occupied");

        var ex = Assert.Throws<MmaCliException>(() =>
            SolutionBuilder.New("Acme.Existing", Mappers.AutoMapper).CreateSolutionDirectory(_tempDir));

        Assert.Equal("directory_not_empty", ex.Code);
    }

    [Fact]
    public async Task ScaffoldFromTemplateAsync_ThrowsActionableError_WhenInstallFails()
    {
        var failingRunner = new FailingInstallRunner();
        var builder = SolutionBuilder.New("Acme.Offline", Mappers.AutoMapper)
            .CreateSolutionDirectory(_tempDir);

        var ex = await Assert.ThrowsAsync<MmaCliException>(() =>
            builder.ScaffoldFromTemplateAsync(failingRunner));

        Assert.Equal("template_install_failed", ex.Code);
        Assert.Contains(Flags.TemplateSourceFlag, ex.Message);
        Assert.Contains(SolutionTemplateCatalog.TemplateSourceEnvVar, ex.Message);
    }

    private sealed class FailingInstallRunner : IDotnetTemplateRunner
    {
        public Task<bool> IsTemplateInstalledAsync(string shortName, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<TemplateCommandResult> InstallPackageAsync(
            string packageId,
            string? version = null,
            string? nugetSource = null,
            bool force = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TemplateCommandResult(false, "", "Unable to load the service index for source.", 1));

        public Task<TemplateCommandResult> InstantiateAsync(
            string shortName,
            string solutionName,
            string outputDirectory,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Instantiate should not be called when install fails.");
    }
}
