using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Roslyn;

public sealed class ModelSyntaxRewriter
{
    public bool HasProperty(string sourceText, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return false;
        }

        var classDecl = RoslynSyntaxHelper.FindPrimaryClass(parsed.Value);
        return classDecl is not null && HasProperty(classDecl, propertyName);
    }

    public OperationResult<string> AddProperty(
        string sourceText,
        string propertyName,
        string csharpType,
        bool isNullable = false,
        bool isVirtual = false,
        string? beforePropertyName = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(csharpType);

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<string>.Failure(parsed.Error!);
        }

        var root = parsed.Value;
        var classDecl = RoslynSyntaxHelper.FindPrimaryClass(root);
        if (classDecl is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Could not locate class declaration in model source.");
        }

        if (HasProperty(classDecl, propertyName))
        {
            return OperationResult<string>.Success(sourceText);
        }

        var nullableSuffix = isNullable && !csharpType.EndsWith("?", StringComparison.Ordinal) ? "?" : string.Empty;
        var virtualPrefix = isVirtual ? "virtual " : string.Empty;
        var propCode = $"public {virtualPrefix}{csharpType}{nullableSuffix} {propertyName} {{get; set;}}";

        if (SyntaxFactory.ParseMemberDeclaration(propCode) is not PropertyDeclarationSyntax newProp
            || newProp.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Could not parse property declaration '{propCode}'.");
        }

        var insertIndex = FindPropertyInsertIndex(classDecl, beforePropertyName);
        var indent = RoslynSyntaxHelper.FindChildMemberIndent(classDecl, "        ");
        var newLine = RoslynSyntaxHelper.DetectNewLine(sourceText);

        var updatedClass = RoslynSyntaxHelper.InsertMemberAt(classDecl, insertIndex, newProp, indent, newLine);
        var updatedRoot = root.ReplaceNode(classDecl, updatedClass);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    public OperationResult<string> RemoveProperty(
        string sourceText,
        string propertyName,
        bool requireVirtual = false)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<string>.Failure(parsed.Error!);
        }

        var root = parsed.Value;
        var classDecl = RoslynSyntaxHelper.FindPrimaryClass(root);
        if (classDecl is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Could not locate class declaration in model source.");
        }

        var targetIndex = -1;
        for (var i = 0; i < classDecl.Members.Count; i++)
        {
            if (classDecl.Members[i] is PropertyDeclarationSyntax prop
                && string.Equals(prop.Identifier.ValueText, propertyName, StringComparison.Ordinal)
                && (!requireVirtual || prop.Modifiers.Any(SyntaxKind.VirtualKeyword)))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
        {
            return OperationResult<string>.Success(sourceText);
        }

        var updatedClass = RoslynSyntaxHelper.RemoveMemberAt(classDecl, targetIndex);
        var updatedRoot = root.ReplaceNode(classDecl, updatedClass);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    private static bool HasProperty(ClassDeclarationSyntax classDecl, string propertyName) =>
        classDecl.Members
            .OfType<PropertyDeclarationSyntax>()
            .Any(p => string.Equals(p.Identifier.ValueText, propertyName, StringComparison.Ordinal));

    private static int FindPropertyInsertIndex(ClassDeclarationSyntax classDecl, string? beforePropertyName)
    {
        if (!string.IsNullOrWhiteSpace(beforePropertyName))
        {
            for (var i = 0; i < classDecl.Members.Count; i++)
            {
                if (classDecl.Members[i] is PropertyDeclarationSyntax prop
                    && string.Equals(prop.Identifier.ValueText, beforePropertyName, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        var lastPropertyIndex = -1;
        for (var i = 0; i < classDecl.Members.Count; i++)
        {
            if (classDecl.Members[i] is PropertyDeclarationSyntax)
            {
                lastPropertyIndex = i;
            }
        }

        if (lastPropertyIndex >= 0)
        {
            return lastPropertyIndex + 1;
        }

        for (var i = 0; i < classDecl.Members.Count; i++)
        {
            if (classDecl.Members[i] is ConstructorDeclarationSyntax or MethodDeclarationSyntax)
            {
                return i;
            }
        }

        return classDecl.Members.Count;
    }
}
