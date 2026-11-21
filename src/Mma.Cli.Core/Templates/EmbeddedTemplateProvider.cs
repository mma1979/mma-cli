using System.Text;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Domain;

namespace Mma.Cli.Core.Templates;

public sealed class EmbeddedTemplateProvider : ITemplateProvider
{
    private readonly ILogger<EmbeddedTemplateProvider>? _logger;

    public EmbeddedTemplateProvider(ILogger<EmbeddedTemplateProvider>? logger = null)
    {
        _logger = logger;
    }

    public string GetTemplate(string solutionPath, MapperKind mapper, string templateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);

        var mapperFolder = mapper.ToFolderName();

        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            var customTemplate = TryLoadExternalOverride(solutionPath, mapper, mapperFolder, templateName, ".scriban")
                ?? TryLoadExternalOverride(solutionPath, mapper, mapperFolder, templateName, ".cs");

            if (customTemplate is not null)
            {
                return customTemplate;
            }
        }

        var embeddedTemplate = TryLoadEmbeddedScriban(mapperFolder, templateName)
            ?? TryLoadEmbeddedScriban("Common", templateName);

        if (embeddedTemplate is not null)
        {
            return embeddedTemplate;
        }

        return DefaultTemplates.GetDefault(mapper, templateName);
    }

    private string? TryLoadExternalOverride(
        string solutionPath,
        MapperKind mapper,
        string mapperFolder,
        string templateName,
        string extension)
    {
        var candidates = new[]
        {
            Path.Combine(solutionPath, ".mma", "templates", mapperFolder, $"{templateName}{extension}"),
            Path.Combine(solutionPath, ".mma", "templates", mapperFolder.ToLowerInvariant(), $"{templateName}{extension}")
        };

        foreach (var templatePath in candidates.Distinct(StringComparer.Ordinal))
        {
            try
            {
                if (File.Exists(templatePath))
                {
                    return File.ReadAllText(templatePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(
                    ex,
                    "Failed to load external template '{TemplateName}' for {Mapper} from '{TemplatePath}'. Falling back to default.",
                    templateName,
                    mapper,
                    templatePath);
            }
        }

        return null;
    }

    private static string? TryLoadEmbeddedScriban(string folderName, string templateName)
    {
        var resourceName = $"Mma.Cli.Core.Templates.{folderName}.{templateName}.scriban";
        using var stream = typeof(EmbeddedTemplateProvider).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
