using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Templates;
using Scriban;
using Scriban.Runtime;

namespace Mma.Cli.Core.CodeGen;

public sealed partial class ScribanTemplateRenderer : ITemplateRenderer
{
    private readonly ITemplateProvider _templateProvider;
    private readonly ILogger<ScribanTemplateRenderer>? _logger;

    public ScribanTemplateRenderer(
        ITemplateProvider? templateProvider = null,
        ILogger<ScribanTemplateRenderer>? logger = null)
    {
        _templateProvider = templateProvider ?? new EmbeddedTemplateProvider();
        _logger = logger;
    }

    [GeneratedRegex(@"\$(SolutionName|EntityVarName|EntitySetName|EntityName|PK)", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyTokenRegex();

    public OperationResult<string> Render(string templateSource, TemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (templateSource is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Template source cannot be null.");
        }

        try
        {
            var normalizedSource = TranslateLegacyTokensOutsideScribanTags(templateSource);
            var parsed = Template.Parse(normalizedSource);
            if (parsed.HasErrors)
            {
                var details = string.Join(Environment.NewLine, parsed.Messages.Select(m => m.ToString()));
                return OperationResult<string>.Failure(
                    ErrorCodes.TEMPLATE_ERROR,
                    $"Template parse error: {parsed.Messages.FirstOrDefault()?.Message ?? "Invalid Scriban syntax."}",
                    details);
            }

            var context = new TemplateContext
            {
                MemberRenamer = member => member.Name
            };
            context.PushGlobal(BuildScriptObject(model));

            var output = parsed.Render(context);
            return OperationResult<string>.Success(output);
        }
        catch (Exception ex) when (ex is Scriban.Syntax.ScriptRuntimeException or InvalidOperationException)
        {
            _logger?.LogError(ex, "Scriban template runtime error for entity '{EntityName}'.", model.EntityName);
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Template rendering failed: {ex.Message}",
                ex.ToString());
        }
    }

    public OperationResult<string> RenderTemplate(
        string solutionPath,
        MapperKind mapper,
        string templateName,
        TemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(templateName))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Template name cannot be empty.");
        }

        string templateSource;
        try
        {
            templateSource = _templateProvider.GetTemplate(solutionPath, mapper, templateName);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to resolve template '{TemplateName}' for {Mapper}.", templateName, mapper);
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Failed to resolve template '{templateName}': {ex.Message}",
                ex.ToString());
        }

        return Render(templateSource, model);
    }

    internal static string TranslateLegacyTokensOutsideScribanTags(string source)
    {
        if (!source.Contains('$', StringComparison.Ordinal))
        {
            return source;
        }

        if (!source.Contains("{{", StringComparison.Ordinal))
        {
            return ReplaceLegacyTokens(source);
        }

        var sb = new StringBuilder(source.Length);
        var index = 0;

        while (index < source.Length)
        {
            var tagStart = source.IndexOf("{{", index, StringComparison.Ordinal);
            if (tagStart < 0)
            {
                sb.Append(ReplaceLegacyTokens(source[index..]));
                break;
            }

            if (tagStart > index)
            {
                sb.Append(ReplaceLegacyTokens(source[index..tagStart]));
            }

            var tagEnd = FindScribanTagEnd(source, tagStart + 2);
            if (tagEnd < 0)
            {
                // Unclosed Scriban tag: keep verbatim so Scriban reports a syntax error.
                sb.Append(source[tagStart..]);
                break;
            }

            sb.Append(source, tagStart, tagEnd - tagStart);
            index = tagEnd;
        }

        return sb.ToString();
    }

    private static int FindScribanTagEnd(string source, int startAfterOpen)
    {
        var i = startAfterOpen;
        char? quote = null;

        while (i < source.Length)
        {
            var ch = source[i];
            if (quote.HasValue)
            {
                if (ch == '\\' && quote.Value != '`' && i + 1 < source.Length)
                {
                    i += 2;
                    continue;
                }

                if (ch == quote.Value)
                {
                    quote = null;
                }

                i++;
                continue;
            }

            if (ch is '"' or '\'' or '`')
            {
                quote = ch;
                i++;
                continue;
            }

            if (ch == '}' && i + 1 < source.Length && source[i + 1] == '}')
            {
                return i + 2;
            }

            i++;
        }

        return -1;
    }

    private static string ReplaceLegacyTokens(string segment) =>
        LegacyTokenRegex().Replace(segment, static match => match.Groups[1].Value switch
        {
            "SolutionName" => "{{ solution_name }}",
            "EntityVarName" => "{{ entity_var_name }}",
            "EntitySetName" => "{{ entity_set_name }}",
            "EntityName" => "{{ entity_name }}",
            "PK" => "{{ pk }}",
            _ => match.Value
        });

    private static ScriptObject BuildScriptObject(TemplateModel model)
    {
        var root = new ScriptObject();
        var mapperName = model.Mapper.ToFolderName();

        AddDualCased(root, "SolutionName", "solution_name", model.SolutionName);
        AddDualCased(root, "EntityName", "entity_name", model.EntityName);
        AddDualCased(root, "EntityVarName", "entity_var_name", model.EntityVarName);
        AddDualCased(root, "EntitySetName", "entity_set_name", model.EntitySetName);
        AddDualCased(root, "PK", "pk", model.PkType);
        AddDualCased(root, "PkType", "pk_type", model.PkType);
        AddDualCased(root, "Mapper", "mapper", mapperName);

        var properties = new ScriptArray();
        if (model.Properties is not null)
        {
            foreach (var prop in model.Properties)
            {
                properties.Add(BuildPropertyObject(prop));
            }
        }

        AddDualCased(root, "Properties", "properties", properties);

        var relations = new ScriptArray();
        if (model.Relations is not null)
        {
            foreach (var relation in model.Relations)
            {
                relations.Add(BuildRelationObject(relation));
            }
        }

        AddDualCased(root, "Relations", "relations", relations);
        return root;
    }

    private static ScriptObject BuildPropertyObject(ManifestProperty prop)
    {
        var obj = new ScriptObject();
        AddDualCased(obj, "Name", "name", prop.Name);
        AddDualCased(obj, "DataType", "data_type", prop.DataType);
        AddDualCased(obj, "Nullable", "nullable", prop.Nullable);
        AddDualCased(obj, "IsForeignKey", "is_foreign_key", prop.IsForeignKey);
        AddDualCased(obj, "Applied", "applied", prop.Applied);
        AddDualCased(obj, "MaxLength", "max_length", prop.MaxLength);
        AddDualCased(obj, "Required", "required", prop.Required);
        return obj;
    }

    private static ScriptObject BuildRelationObject(ManifestRelation relation)
    {
        var obj = new ScriptObject();
        AddDualCased(obj, "Name", "name", relation.Name);
        AddDualCased(obj, "ParentEntity", "parent_entity", relation.ParentEntity);
        AddDualCased(obj, "ChildEntity", "child_entity", relation.ChildEntity);
        AddDualCased(obj, "ForeignKey", "foreign_key", relation.ForeignKey);
        AddDualCased(obj, "ForeignKeyType", "foreign_key_type", relation.ForeignKeyType);
        AddDualCased(obj, "RelationType", "relation_type", relation.RelationType);
        AddDualCased(obj, "DeleteBehavior", "delete_behavior", relation.DeleteBehavior);
        AddDualCased(obj, "Applied", "applied", relation.Applied);
        return obj;
    }

    private static void AddDualCased(ScriptObject target, string pascalKey, string snakeKey, object? value)
    {
        target[pascalKey] = value;
        target[snakeKey] = value;
    }
}
