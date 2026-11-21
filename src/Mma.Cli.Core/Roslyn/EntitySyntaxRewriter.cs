using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Roslyn;

public sealed class EntitySyntaxRewriter
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
        string entityName,
        string propertyName,
        string csharpType,
        bool isNullable = false,
        bool isVirtual = false,
        bool isPrivateSetter = true)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(csharpType);

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<string>.Failure(parsed.Error!);
        }

        var root = parsed.Value;
        var classDecl = RoslynSyntaxHelper.FindPrimaryClass(root, entityName);
        if (classDecl is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Could not locate entity class '{entityName}'.");
        }

        if (HasProperty(classDecl, propertyName))
        {
            return OperationResult<string>.Success(sourceText);
        }

        var nullableSuffix = isNullable && !csharpType.EndsWith("?", StringComparison.Ordinal) ? "?" : string.Empty;
        var virtualPrefix = isVirtual ? "virtual " : string.Empty;
        var setterAccess = isPrivateSetter ? "private " : string.Empty;
        var propCode = $"public {virtualPrefix}{csharpType}{nullableSuffix} {propertyName} {{get; {setterAccess}set;}}";

        if (SyntaxFactory.ParseMemberDeclaration(propCode) is not PropertyDeclarationSyntax newProp
            || newProp.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Could not parse entity property declaration '{propCode}'.");
        }

        var insertIndex = FindEntityPropertyInsertIndex(classDecl);
        var indent = RoslynSyntaxHelper.FindChildMemberIndent(classDecl, "        ");
        var newLine = RoslynSyntaxHelper.DetectNewLine(sourceText);

        var updatedClass = RoslynSyntaxHelper.InsertMemberAt(classDecl, insertIndex, newProp, indent, newLine);
        var updatedRoot = root.ReplaceNode(classDecl, updatedClass);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    public OperationResult<string> AddConstructorAndUpdateAssignment(
        string sourceText,
        string entityName,
        string assignmentStatement)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentStatement);

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<string>.Failure(parsed.Error!);
        }

        var root = parsed.Value;
        var classDecl = RoslynSyntaxHelper.FindPrimaryClass(root, entityName);
        if (classDecl is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Could not locate entity class '{entityName}'.");
        }

        var parsedStatement = SyntaxFactory.ParseStatement(assignmentStatement.Trim());
        if (parsedStatement is EmptyStatementSyntax
            || parsedStatement.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Invalid assignment statement '{assignmentStatement}'.");
        }

        var hasParamCtor = classDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .Any(c => c.ParameterList.Parameters.Count > 0 && c.Body is not null);
        var hasUpdateMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .Any(m => string.Equals(m.Identifier.ValueText, "Update", StringComparison.Ordinal) && m.Body is not null);

        if (!hasParamCtor && !hasUpdateMethod)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Entity '{entityName}' does not contain a parameterized constructor or Update method.");
        }

        var newLine = RoslynSyntaxHelper.DetectNewLine(sourceText);
        var modifiedAny = false;
        var updatedMembers = new SyntaxList<MemberDeclarationSyntax>();

        foreach (var member in classDecl.Members)
        {
            if (member is ConstructorDeclarationSyntax ctor
                && ctor.ParameterList.Parameters.Count > 0
                && ctor.Body is not null)
            {
                if (!ctor.Body.Statements.Any(s => RoslynSyntaxHelper.AreStatementsTokenEquivalent(s, parsedStatement)))
                {
                    var insertIdx = FindAssignmentIndex(ctor.Body, "CreatedDate");
                    if (insertIdx < 0)
                    {
                        insertIdx = ctor.Body.Statements.Count;
                    }

                    var indent = RoslynSyntaxHelper.FindBlockStatementIndent(ctor.Body, "            ");
                    var updatedBody = RoslynSyntaxHelper.InsertStatementAt(ctor.Body, insertIdx, parsedStatement, indent, newLine);
                    updatedMembers = updatedMembers.Add(ctor.WithBody(updatedBody));
                    modifiedAny = true;
                    continue;
                }
            }
            else if (member is MethodDeclarationSyntax method
                     && string.Equals(method.Identifier.ValueText, "Update", StringComparison.Ordinal)
                     && method.Body is not null)
            {
                if (!method.Body.Statements.Any(s => RoslynSyntaxHelper.AreStatementsTokenEquivalent(s, parsedStatement)))
                {
                    var insertIdx = FindAssignmentIndex(method.Body, "ModifiedDate");
                    if (insertIdx < 0)
                    {
                        insertIdx = FindFirstReturnIndex(method.Body);
                    }

                    if (insertIdx < 0)
                    {
                        insertIdx = method.Body.Statements.Count;
                    }

                    var indent = RoslynSyntaxHelper.FindBlockStatementIndent(method.Body, "            ");
                    var updatedBody = RoslynSyntaxHelper.InsertStatementAt(method.Body, insertIdx, parsedStatement, indent, newLine);
                    updatedMembers = updatedMembers.Add(method.WithBody(updatedBody));
                    modifiedAny = true;
                    continue;
                }
            }

            updatedMembers = updatedMembers.Add(member);
        }

        if (!modifiedAny)
        {
            return OperationResult<string>.Success(sourceText);
        }

        var updatedClass = classDecl.WithMembers(updatedMembers);
        var updatedRoot = root.ReplaceNode(classDecl, updatedClass);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    public OperationResult<string> RemovePropertyAndAssignments(
        string sourceText,
        string propertyName,
        string? assignmentPrefix = null)
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
                "Could not locate entity class declaration.");
        }

        var workingClass = classDecl;
        var changed = false;

        for (var i = workingClass.Members.Count - 1; i >= 0; i--)
        {
            if (workingClass.Members[i] is PropertyDeclarationSyntax prop
                && string.Equals(prop.Identifier.ValueText, propertyName, StringComparison.Ordinal))
            {
                workingClass = RoslynSyntaxHelper.RemoveMemberAt(workingClass, i);
                changed = true;
            }
        }

        var rewrittenMembers = new SyntaxList<MemberDeclarationSyntax>();
        foreach (var member in workingClass.Members)
        {
            if (member is ConstructorDeclarationSyntax ctor && ctor.Body is not null)
            {
                var (updatedBody, bodyChanged) = RemoveMatchingAssignments(ctor.Body, propertyName, assignmentPrefix);
                if (bodyChanged)
                {
                    rewrittenMembers = rewrittenMembers.Add(ctor.WithBody(updatedBody));
                    changed = true;
                    continue;
                }
            }
            else if (member is MethodDeclarationSyntax method
                     && string.Equals(method.Identifier.ValueText, "Update", StringComparison.Ordinal)
                     && method.Body is not null)
            {
                var (updatedBody, bodyChanged) = RemoveMatchingAssignments(method.Body, propertyName, assignmentPrefix);
                if (bodyChanged)
                {
                    rewrittenMembers = rewrittenMembers.Add(method.WithBody(updatedBody));
                    changed = true;
                    continue;
                }
            }

            rewrittenMembers = rewrittenMembers.Add(member);
        }

        if (!changed)
        {
            return OperationResult<string>.Success(sourceText);
        }

        workingClass = workingClass.WithMembers(rewrittenMembers);
        var updatedRoot = root.ReplaceNode(classDecl, workingClass);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    private static bool HasProperty(ClassDeclarationSyntax classDecl, string propertyName) =>
        classDecl.Members
            .OfType<PropertyDeclarationSyntax>()
            .Any(p => string.Equals(p.Identifier.ValueText, propertyName, StringComparison.Ordinal));

    private static int FindEntityPropertyInsertIndex(ClassDeclarationSyntax classDecl)
    {
        for (var i = 0; i < classDecl.Members.Count; i++)
        {
            if (classDecl.Members[i] is FieldDeclarationSyntax field
                && field.Declaration.Variables.Any(v =>
                    string.Equals(v.Identifier.ValueText, "_Validator", StringComparison.Ordinal)))
            {
                return i;
            }
        }

        var lastPublicPropIdx = -1;
        var lastAnyPropIdx = -1;
        for (var i = 0; i < classDecl.Members.Count; i++)
        {
            if (classDecl.Members[i] is PropertyDeclarationSyntax prop)
            {
                lastAnyPropIdx = i;
                if (prop.Modifiers.Any(SyntaxKind.PublicKeyword))
                {
                    lastPublicPropIdx = i;
                }
            }
        }

        if (lastPublicPropIdx >= 0)
        {
            return lastPublicPropIdx + 1;
        }

        if (lastAnyPropIdx >= 0)
        {
            return lastAnyPropIdx + 1;
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

    private static int FindAssignmentIndex(BlockSyntax body, string targetIdentifier)
    {
        for (var i = 0; i < body.Statements.Count; i++)
        {
            if (body.Statements[i] is ExpressionStatementSyntax exprStmt
                && exprStmt.Expression is AssignmentExpressionSyntax assignment
                && TargetsProperty(assignment.Left, targetIdentifier))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindFirstReturnIndex(BlockSyntax body)
    {
        for (var i = 0; i < body.Statements.Count; i++)
        {
            if (body.Statements[i] is ReturnStatementSyntax)
            {
                return i;
            }
        }

        return -1;
    }

    private static (BlockSyntax Body, bool Changed) RemoveMatchingAssignments(
        BlockSyntax body,
        string propertyName,
        string? assignmentPrefix)
    {
        var working = body;
        var changed = false;

        for (var i = working.Statements.Count - 1; i >= 0; i--)
        {
            if (IsAssignmentToProperty(working.Statements[i], propertyName, assignmentPrefix))
            {
                working = RoslynSyntaxHelper.RemoveStatementAt(working, i);
                changed = true;
            }
        }

        return (working, changed);
    }

    private static bool IsAssignmentToProperty(StatementSyntax statement, string propertyName, string? assignmentPrefix)
    {
        if (!string.IsNullOrWhiteSpace(assignmentPrefix))
        {
            var trimmed = statement.ToString().Trim();
            if (trimmed.StartsWith(assignmentPrefix.Trim(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (statement is not ExpressionStatementSyntax exprStmt
            || exprStmt.Expression is not AssignmentExpressionSyntax assignment)
        {
            return false;
        }

        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && !assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            return false;
        }

        return TargetsProperty(assignment.Left, propertyName);
    }

    private static bool TargetsProperty(ExpressionSyntax left, string propertyName) =>
        left switch
        {
            IdentifierNameSyntax id => string.Equals(id.Identifier.ValueText, propertyName, StringComparison.Ordinal),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } ma
                => string.Equals(ma.Name.Identifier.ValueText, propertyName, StringComparison.Ordinal),
            _ => false
        };
}
