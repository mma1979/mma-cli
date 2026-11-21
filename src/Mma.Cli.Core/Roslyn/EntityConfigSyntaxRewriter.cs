using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Roslyn;

public sealed class EntityConfigSyntaxRewriter
{
    public OperationResult<string> AddConfigurationStatement(string sourceText, string statementCode)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(statementCode);

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<string>.Failure(parsed.Error!);
        }

        var root = parsed.Value;
        var configureMethod = FindConfigureMethod(root);
        if (configureMethod?.Body is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Could not locate 'Configure' method with block body in entity configuration.");
        }

        var newLine = RoslynSyntaxHelper.DetectNewLine(sourceText);
        var normalizedStatement = RoslynSyntaxHelper.NormalizeLineEndings(statementCode.Trim(), newLine);
        var parsedStatement = SyntaxFactory.ParseStatement(normalizedStatement);
        if (parsedStatement is EmptyStatementSyntax
            || parsedStatement.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Invalid configuration statement '{statementCode}'.");
        }

        var body = configureMethod.Body;
        if (body.Statements.Any(s => RoslynSyntaxHelper.AreStatementsTokenEquivalent(s, parsedStatement)))
        {
            return OperationResult<string>.Success(sourceText);
        }

        var indent = RoslynSyntaxHelper.FindBlockStatementIndent(body, "\t\t\t");
        var updatedBody = RoslynSyntaxHelper.InsertStatementAt(body, body.Statements.Count, parsedStatement, indent, newLine);
        var updatedRoot = root.ReplaceNode(body, updatedBody);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    public OperationResult<string> RemovePropertyConfiguration(string sourceText, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<string>.Failure(parsed.Error!);
        }

        var root = parsed.Value;
        var configureMethod = FindConfigureMethod(root);
        if (configureMethod?.Body is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Could not locate 'Configure' method with block body in entity configuration.");
        }

        var working = configureMethod.Body;
        var changed = false;

        for (var i = working.Statements.Count - 1; i >= 0; i--)
        {
            if (IsPropertyConfigStatement(working.Statements[i], propertyName))
            {
                working = RoslynSyntaxHelper.RemoveStatementAt(working, i);
                changed = true;
            }
        }

        if (!changed)
        {
            return OperationResult<string>.Success(sourceText);
        }

        var updatedRoot = root.ReplaceNode(configureMethod.Body, working);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    public bool HasRelationConfiguration(
        string sourceText,
        string childrenSetName,
        string parentEntity,
        string foreignKey)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if (string.IsNullOrWhiteSpace(childrenSetName)
            || string.IsNullOrWhiteSpace(parentEntity)
            || string.IsNullOrWhiteSpace(foreignKey))
        {
            return false;
        }

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return false;
        }

        var configureMethod = FindConfigureMethod(parsed.Value);
        return configureMethod?.Body is not null
               && configureMethod.Body.Statements.Any(s =>
                   IsRelationStatement(s, childrenSetName, parentEntity, foreignKey));
    }

    public OperationResult<string> RemoveRelationConfiguration(
        string sourceText,
        string childrenSetName,
        string parentEntity,
        string foreignKey)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(childrenSetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentEntity);
        ArgumentException.ThrowIfNullOrWhiteSpace(foreignKey);

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<string>.Failure(parsed.Error!);
        }

        var root = parsed.Value;
        var configureMethod = FindConfigureMethod(root);
        if (configureMethod?.Body is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Could not locate 'Configure' method with block body in entity configuration.");
        }

        var working = configureMethod.Body;
        var changed = false;

        for (var i = working.Statements.Count - 1; i >= 0; i--)
        {
            if (IsRelationStatement(working.Statements[i], childrenSetName, parentEntity, foreignKey))
            {
                working = RoslynSyntaxHelper.RemoveStatementAt(working, i);
                changed = true;
            }
        }

        if (!changed)
        {
            return OperationResult<string>.Success(sourceText);
        }

        var updatedRoot = root.ReplaceNode(configureMethod.Body, working);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    private static MethodDeclarationSyntax? FindConfigureMethod(CompilationUnitSyntax root)
    {
        var classDecl = RoslynSyntaxHelper.FindPrimaryClass(root);
        return classDecl?.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m =>
                string.Equals(m.Identifier.ValueText, "Configure", StringComparison.Ordinal)
                && m.Body is not null);
    }

    private static bool IsPropertyConfigStatement(StatementSyntax statement, string propertyName)
    {
        if (statement is not ExpressionStatementSyntax exprStmt)
        {
            return false;
        }

        foreach (var (methodName, arguments) in UnwrapFluentChain(exprStmt.Expression))
        {
            if (string.Equals(methodName, "Property", StringComparison.Ordinal)
                && arguments.Count == 1
                && MatchesTargetMember(arguments[0], propertyName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRelationStatement(
        StatementSyntax statement,
        string childrenSetName,
        string parentEntity,
        string foreignKey)
    {
        if (statement is not ExpressionStatementSyntax exprStmt)
        {
            return false;
        }

        var hasMany = false;
        var withOne = false;
        var hasForeignKey = false;

        foreach (var (methodName, arguments) in UnwrapFluentChain(exprStmt.Expression))
        {
            if (arguments.Count != 1)
            {
                continue;
            }

            if (string.Equals(methodName, "HasMany", StringComparison.Ordinal)
                && MatchesTargetMember(arguments[0], childrenSetName))
            {
                hasMany = true;
            }
            else if (string.Equals(methodName, "WithOne", StringComparison.Ordinal)
                     && MatchesTargetMember(arguments[0], parentEntity))
            {
                withOne = true;
            }
            else if (string.Equals(methodName, "HasForeignKey", StringComparison.Ordinal)
                     && MatchesTargetMember(arguments[0], foreignKey))
            {
                hasForeignKey = true;
            }
        }

        return hasMany && withOne && hasForeignKey;
    }

    private static IEnumerable<(string MethodName, SeparatedSyntaxList<ArgumentSyntax> Arguments)> UnwrapFluentChain(
        ExpressionSyntax expression)
    {
        var current = expression;
        while (current is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                yield return (memberAccess.Name.Identifier.ValueText, invocation.ArgumentList.Arguments);
                current = memberAccess.Expression;
            }
            else
            {
                break;
            }
        }
    }

    private static bool MatchesTargetMember(ArgumentSyntax argument, string targetName)
    {
        var expr = argument.Expression;
        if (expr is LambdaExpressionSyntax lambda)
        {
            var bodyExpr = lambda.ExpressionBody;
            if (bodyExpr is null
                && lambda.Block?.Statements.Count == 1
                && lambda.Block.Statements[0] is ReturnStatementSyntax ret)
            {
                bodyExpr = ret.Expression;
            }

            return bodyExpr is MemberAccessExpressionSyntax memberAccess
                   && string.Equals(memberAccess.Name.Identifier.ValueText, targetName, StringComparison.Ordinal);
        }

        if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return string.Equals(literal.Token.ValueText, targetName, StringComparison.Ordinal);
        }

        return false;
    }
}
