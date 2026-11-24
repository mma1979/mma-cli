namespace Mma.Cli.Shared.Solutions;

public interface IDotnetTemplateRunner
{
    Task<bool> IsTemplateInstalledAsync(
        string shortName,
        CancellationToken cancellationToken = default);

    Task<TemplateCommandResult> InstallPackageAsync(
        string packageId,
        string? version = null,
        string? nugetSource = null,
        bool force = false,
        CancellationToken cancellationToken = default);

    Task<TemplateCommandResult> InstantiateAsync(
        string shortName,
        string solutionName,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}
