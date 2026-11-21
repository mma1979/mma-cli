using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.CodeGen;

public sealed record TemplateModel(
    string SolutionName,
    string EntityName,
    string EntityVarName,
    string EntitySetName,
    string PkType,
    MapperKind Mapper,
    IReadOnlyList<ManifestProperty>? Properties = null,
    IReadOnlyList<ManifestRelation>? Relations = null);

public interface ITemplateRenderer
{
    OperationResult<string> Render(string templateSource, TemplateModel model);

    OperationResult<string> RenderTemplate(
        string solutionPath,
        MapperKind mapper,
        string templateName,
        TemplateModel model);
}
