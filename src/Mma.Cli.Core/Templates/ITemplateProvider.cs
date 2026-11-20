using Mma.Cli.Core.Domain;

namespace Mma.Cli.Core.Templates;

public interface ITemplateProvider
{
    string GetTemplate(string solutionPath, MapperKind mapper, string templateName);
}
