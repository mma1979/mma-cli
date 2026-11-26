using Mma.Cli.Shared.Consts;

namespace Mma.Cli.Shared.Solutions;

public static class SolutionTemplateCatalog
{
    public const string DefaultTemplateVersion = "10.0.0-preview.4";
    public const string TemplateSourceEnvVar = "MMA_TEMPLATE_SOURCE";
    public const string TemplateVersionEnvVar = "MMA_TEMPLATE_VERSION";

    public static readonly SolutionTemplateDescriptor AutoMapper = new(
        Mapper: Mappers.AutoMapper,
        PackageId: "Mma.Cli.Solutions.AutoMapper",
        ShortName: "mma-sln-automapper",
        DefaultVersion: DefaultTemplateVersion);

    public static readonly SolutionTemplateDescriptor Mapster = new(
        Mapper: Mappers.Mapster,
        PackageId: "Mma.Cli.Solutions.Mapster",
        ShortName: "mma-sln-mapster",
        DefaultVersion: DefaultTemplateVersion);

    public static SolutionTemplateDescriptor Resolve(string? mapper) =>
        string.Equals(mapper?.Trim(), Mappers.Mapster, StringComparison.OrdinalIgnoreCase)
            ? Mapster
            : AutoMapper;

    public static string? ResolveTemplateSource(string? explicitSource)
    {
        if (!string.IsNullOrWhiteSpace(explicitSource))
        {
            return explicitSource.Trim();
        }

        var envSource = Environment.GetEnvironmentVariable(TemplateSourceEnvVar);
        return string.IsNullOrWhiteSpace(envSource) ? null : envSource.Trim();
    }

    public static string? ResolveTemplateVersion(string? explicitVersion)
    {
        if (!string.IsNullOrWhiteSpace(explicitVersion))
        {
            return explicitVersion.Trim();
        }

        var envVersion = Environment.GetEnvironmentVariable(TemplateVersionEnvVar);
        return string.IsNullOrWhiteSpace(envVersion) ? null : envVersion.Trim();
    }
}
