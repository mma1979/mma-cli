using System;
using System.IO;

namespace Mma.Cli.Shared.Helpers
{
    public static class TemplateHelper
    {
        public static string GetTemplate(string solutionPath, string mapper, string templateName, string defaultTemplate)
        {
            try
            {
                var templatePath = Path.Combine(solutionPath, ".mma", "templates", mapper, $"{templateName}.cs");

                if (File.Exists(templatePath))
                {
                    return File.ReadAllText(templatePath);
                }
            }
            catch (Exception ex)
            {
                Output.Warning($"Failed to load external template '{templateName}' for {mapper}: {ex.Message}. Using default.");
            }

            return defaultTemplate;
        }
    }
}
