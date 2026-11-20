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

        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            var templatePath = Path.Combine(
                solutionPath,
                ".mma",
                "templates",
                mapper.ToFolderName(),
                $"{templateName}.cs");

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

        return DefaultTemplates.GetDefault(mapper, templateName);
    }
}
