using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Roslyn;

public sealed class DbContextSyntaxRewriter
{
    public OperationResult<string> RegisterEntityInDbContext(
        string sourceText,
        string entityName,
        string entitySetName,
        string? dbSetDeclarationCode = null,
        string? applyConfigStatementCode = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySetName);

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<string>.Failure(parsed.Error!);
        }

        var root = parsed.Value;
        var classDecl = RoslynSyntaxHelper.FindPrimaryClass(root, "ApplicationDbContext");
        if (classDecl is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Could not locate DbContext class declaration.");
        }

        var onModelCreating = FindOnModelCreating(classDecl);
        if (onModelCreating?.Body is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Could not locate 'OnModelCreating' method with block body in DbContext.");
        }

        var newLine = RoslynSyntaxHelper.DetectNewLine(sourceText);
        var workingClass = classDecl;
        var changed = false;

        if (!HasDbSet(workingClass, entityName, entitySetName))
        {
            var rawDbSet = string.IsNullOrWhiteSpace(dbSetDeclarationCode)
                ? $"public virtual DbSet<{entityName}> {entitySetName} {{ get; set; }}"
                : RoslynSyntaxHelper.NormalizeLineEndings(dbSetDeclarationCode.Trim(), newLine);

            var dbSetMember = SyntaxFactory.ParseMemberDeclaration(rawDbSet);
            if (dbSetMember is null || dbSetMember.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                return OperationResult<string>.Failure(
                    ErrorCodes.TEMPLATE_ERROR,
                    $"Could not parse DbSet declaration '{rawDbSet}'.");
            }

            var (insertIdx, indent) = FindDbSetInsertLocation(workingClass);
            workingClass = RoslynSyntaxHelper.InsertMemberAt(workingClass, insertIdx, dbSetMember, indent, newLine);
            changed = true;
        }

        var currentOnModelCreating = FindOnModelCreating(workingClass)!;
        var currentBody = currentOnModelCreating.Body!;
        var paramName = currentOnModelCreating.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText ?? "modelBuilder";

        var rawApply = string.IsNullOrWhiteSpace(applyConfigStatementCode)
            ? $"{paramName}.ApplyConfiguration(new {entityName}Config());"
            : RoslynSyntaxHelper.NormalizeLineEndings(applyConfigStatementCode.Trim(), newLine);

        var applyStmt = SyntaxFactory.ParseStatement(rawApply);
        if (applyStmt is EmptyStatementSyntax || applyStmt.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Could not parse ApplyConfiguration statement '{rawApply}'.");
        }

        if (!string.IsNullOrWhiteSpace(applyConfigStatementCode)
            && !string.Equals(paramName, "modelBuilder", StringComparison.Ordinal))
        {
            applyStmt = AdaptModelBuilderParameter(applyStmt, paramName);
        }

        var alreadyApplied = currentBody.Statements.Any(s =>
            RoslynSyntaxHelper.AreStatementsTokenEquivalent(s, applyStmt)
            || IsApplyConfigurationForEntity(s, entityName));

        if (!alreadyApplied)
        {
            var (stmtIdx, stmtIndent) = FindApplyConfigurationInsertLocation(currentBody);
            var updatedBody = RoslynSyntaxHelper.InsertStatementAt(currentBody, stmtIdx, applyStmt, stmtIndent, newLine);
            workingClass = workingClass.ReplaceNode(currentOnModelCreating, currentOnModelCreating.WithBody(updatedBody));
            changed = true;
        }

        if (!changed)
        {
            return OperationResult<string>.Success(sourceText);
        }

        var updatedRoot = root.ReplaceNode(classDecl, workingClass);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    public OperationResult<string> RegisterEntityInMappingProfile(
        string sourceText,
        string entityName,
        string? customConfigCode = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        var parsed = RoslynSyntaxHelper.ParseAndValidate(sourceText);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return OperationResult<string>.Failure(parsed.Error!);
        }

        var root = parsed.Value;
        var classDecl = RoslynSyntaxHelper.FindPrimaryClass(root, "MappingProfile");
        if (classDecl is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Could not locate MappingProfile class declaration.");
        }

        var ctor = classDecl.Members
                       .OfType<ConstructorDeclarationSyntax>()
                       .FirstOrDefault(c => c.ParameterList.Parameters.Count == 0 && c.Body is not null)
                   ?? classDecl.Members
                       .OfType<ConstructorDeclarationSyntax>()
                       .FirstOrDefault(c => c.Body is not null);

        if (ctor?.Body is null)
        {
            return OperationResult<string>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                "Could not locate constructor with block body in MappingProfile.");
        }

        var newLine = RoslynSyntaxHelper.DetectNewLine(sourceText);
        var statementsResult = ParseMappingStatements(entityName, customConfigCode, newLine);
        if (!statementsResult.IsSuccess || statementsResult.Value is null)
        {
            return OperationResult<string>.Failure(statementsResult.Error!);
        }

        var parsedStatements = statementsResult.Value;
        var workingBody = ctor.Body;
        var indent = RoslynSyntaxHelper.FindBlockStatementIndent(workingBody, "            ");
        var changed = false;
        var previousIndexInserted = -2;

        for (var i = 0; i < parsedStatements.Count; i++)
        {
            var candidate = parsedStatements[i];
            if (workingBody.Statements.Any(s => RoslynSyntaxHelper.AreStatementsTokenEquivalent(s, candidate)))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(customConfigCode) && i == previousIndexInserted + 1)
            {
                // Preserve relative spacing between consecutive statements from customConfigCode (e.g. Config.scriban)
                // while normalizing line endings to match the target MappingProfile file.
                var preservedLeading = RoslynSyntaxHelper.NormalizeTriviaNewLines(candidate.GetLeadingTrivia(), newLine);
                var prepared = candidate
                    .WithLeadingTrivia(preservedLeading)
                    .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(newLine)));
                workingBody = workingBody.WithStatements(workingBody.Statements.Add(prepared));
            }
            else
            {
                var cleanCandidate = candidate.WithoutTrivia();
                workingBody = RoslynSyntaxHelper.InsertStatementAt(
                    workingBody,
                    workingBody.Statements.Count,
                    cleanCandidate,
                    indent,
                    newLine);
            }

            previousIndexInserted = i;
            changed = true;
        }

        if (!changed)
        {
            return OperationResult<string>.Success(sourceText);
        }

        var updatedCtor = ctor.WithBody(workingBody);
        var updatedClass = classDecl.ReplaceNode(ctor, updatedCtor);
        var updatedRoot = root.ReplaceNode(classDecl, updatedClass);
        return RoslynSyntaxHelper.ValidateRewrittenSource(updatedRoot);
    }

    private static OperationResult<IReadOnlyList<StatementSyntax>> ParseMappingStatements(
        string entityName,
        string? customConfigCode,
        string newLine)
    {
        if (string.IsNullOrWhiteSpace(customConfigCode))
        {
            var defaults = new[]
            {
                SyntaxFactory.ParseStatement(
                    $"CreateMap<{entityName}, {entityName}ReadModel>().IgnoreAllPropertiesWithAnInaccessibleSetter().IgnoreAllSourcePropertiesWithAnInaccessibleSetter();"),
                SyntaxFactory.ParseStatement(
                    $"CreateMap<{entityName}, {entityName}ModifyModel>().IgnoreAllPropertiesWithAnInaccessibleSetter().IgnoreAllSourcePropertiesWithAnInaccessibleSetter();")
            };
            return OperationResult<IReadOnlyList<StatementSyntax>>.Success(defaults);
        }

        var normalized = RoslynSyntaxHelper.NormalizeLineEndings(customConfigCode, newLine);
        var wrapped = $"{{{newLine}{normalized}{newLine}}}";
        if (SyntaxFactory.ParseStatement(wrapped) is not BlockSyntax block
            || block.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)
            || block.Statements.Count == 0)
        {
            return OperationResult<IReadOnlyList<StatementSyntax>>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Could not parse MappingProfile config statements '{customConfigCode}'.");
        }

        return OperationResult<IReadOnlyList<StatementSyntax>>.Success(block.Statements.ToArray());
    }

    private static MethodDeclarationSyntax? FindOnModelCreating(ClassDeclarationSyntax classDecl) =>
        classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m =>
                string.Equals(m.Identifier.ValueText, "OnModelCreating", StringComparison.Ordinal)
                && m.Body is not null);

    private static bool HasDbSet(ClassDeclarationSyntax classDecl, string entityName, string entitySetName)
    {
        foreach (var prop in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (string.Equals(prop.Identifier.ValueText, entitySetName, StringComparison.Ordinal))
            {
                return true;
            }

            if (TryGetDbSetEntityTypeName(prop.Type, out var dbSetEntityName)
                && string.Equals(dbSetEntityName, entityName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static (int InsertIndex, string Indent) FindDbSetInsertLocation(ClassDeclarationSyntax classDecl)
    {
        var lastDbSetIndex = -1;
        for (var i = 0; i < classDecl.Members.Count; i++)
        {
            if (classDecl.Members[i] is PropertyDeclarationSyntax prop && TryGetDbSetEntityTypeName(prop.Type, out _))
            {
                lastDbSetIndex = i;
            }
        }

        if (lastDbSetIndex >= 0)
        {
            var indent = RoslynSyntaxHelper.ExtractLineIndent(classDecl.Members[lastDbSetIndex].GetLeadingTrivia());
            if (string.IsNullOrEmpty(indent))
            {
                indent = RoslynSyntaxHelper.FindChildMemberIndent(classDecl, "        ");
            }

            return (lastDbSetIndex + 1, indent);
        }

        for (var i = 0; i < classDecl.Members.Count; i++)
        {
            if (classDecl.Members[i] is MethodDeclarationSyntax method
                && string.Equals(method.Identifier.ValueText, "OnModelCreating", StringComparison.Ordinal))
            {
                return (i, RoslynSyntaxHelper.FindChildMemberIndent(classDecl, "        "));
            }
        }

        return (classDecl.Members.Count, RoslynSyntaxHelper.FindChildMemberIndent(classDecl, "        "));
    }

    private static (int InsertIndex, string Indent) FindApplyConfigurationInsertLocation(BlockSyntax body)
    {
        var lastApplyIndex = -1;
        for (var i = 0; i < body.Statements.Count; i++)
        {
            if (IsApplyConfigurationStatement(body.Statements[i]))
            {
                lastApplyIndex = i;
            }
        }

        if (lastApplyIndex >= 0)
        {
            var indent = RoslynSyntaxHelper.ExtractLineIndent(body.Statements[lastApplyIndex].GetLeadingTrivia());
            if (string.IsNullOrEmpty(indent))
            {
                indent = RoslynSyntaxHelper.FindBlockStatementIndent(body, "            ");
            }

            return (lastApplyIndex + 1, indent);
        }

        return (body.Statements.Count, RoslynSyntaxHelper.FindBlockStatementIndent(body, "            "));
    }

    private static bool TryGetDbSetEntityTypeName(TypeSyntax typeSyntax, out string entityName)
    {
        var unwrapped = typeSyntax switch
        {
            QualifiedNameSyntax q => q.Right,
            _ => typeSyntax
        };

        if (unwrapped is GenericNameSyntax generic
            && string.Equals(generic.Identifier.ValueText, "DbSet", StringComparison.Ordinal)
            && generic.TypeArgumentList.Arguments.Count == 1)
        {
            var argType = generic.TypeArgumentList.Arguments[0];
            var leaf = argType switch
            {
                QualifiedNameSyntax q => q.Right,
                _ => argType
            };

            if (leaf is SimpleNameSyntax simple)
            {
                entityName = simple.Identifier.ValueText;
                return true;
            }
        }

        entityName = string.Empty;
        return false;
    }

    private static bool IsApplyConfigurationStatement(StatementSyntax statement) =>
        statement is ExpressionStatementSyntax
        {
            Expression: InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ApplyConfiguration" }
            }
        };

    private static bool IsApplyConfigurationForEntity(StatementSyntax statement, string entityName)
    {
        if (statement is not ExpressionStatementSyntax
            {
                Expression: InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ApplyConfiguration" },
                    ArgumentList.Arguments.Count: >= 1
                } invocation
            })
        {
            return false;
        }

        var expectedConfigName = $"{entityName}Config";
        var firstArg = invocation.ArgumentList.Arguments[0].Expression;
        if (firstArg is ObjectCreationExpressionSyntax creation)
        {
            var typeLeaf = creation.Type switch
            {
                QualifiedNameSyntax q => q.Right,
                _ => creation.Type
            };

            if (typeLeaf is SimpleNameSyntax simple
                && string.Equals(simple.Identifier.ValueText, expectedConfigName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static StatementSyntax AdaptModelBuilderParameter(StatementSyntax statement, string targetParamName)
    {
        if (statement is ExpressionStatementSyntax
            {
                Expression: InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax receiver,
                        Name.Identifier.ValueText: "ApplyConfiguration"
                    } memberAccess
                } invocation
            } exprStmt
            && string.Equals(receiver.Identifier.ValueText, "modelBuilder", StringComparison.Ordinal))
        {
            var updatedReceiver = SyntaxFactory.IdentifierName(targetParamName)
                .WithTriviaFrom(receiver);
            return exprStmt.WithExpression(
                invocation.WithExpression(memberAccess.WithExpression(updatedReceiver)));
        }

        return statement;
    }
}
