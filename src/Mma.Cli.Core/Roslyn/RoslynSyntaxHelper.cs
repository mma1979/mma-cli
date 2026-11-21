using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Roslyn;

public static class RoslynSyntaxHelper
{
    public static string DetectNewLine(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return sourceText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    public static OperationResult<CompilationUnitSyntax> ParseAndValidate(string sourceText, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            var location = string.IsNullOrWhiteSpace(filePath) ? string.Empty : $" in '{filePath}'";
            var details = string.Join(Environment.NewLine, errors.Select(d => d.ToString()));
            return OperationResult<CompilationUnitSyntax>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Invalid C# syntax{location}: {errors[0].GetMessage()}",
                details);
        }

        return OperationResult<CompilationUnitSyntax>.Success(tree.GetCompilationUnitRoot());
    }

    internal static OperationResult<string> ValidateRewrittenSource(SyntaxNode root, string? filePath = null)
    {
        var updatedText = root.ToFullString();
        var validation = ParseAndValidate(updatedText, filePath);
        if (!validation.IsSuccess)
        {
            return OperationResult<string>.Failure(validation.Error!);
        }

        return OperationResult<string>.Success(updatedText);
    }

    internal static ClassDeclarationSyntax? FindPrimaryClass(CompilationUnitSyntax root, string? preferredName = null)
    {
        var topLevelClasses = new List<ClassDeclarationSyntax>();

        foreach (var member in root.Members)
        {
            switch (member)
            {
                case ClassDeclarationSyntax cls:
                    topLevelClasses.Add(cls);
                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    foreach (var nsMember in ns.Members)
                    {
                        if (nsMember is ClassDeclarationSyntax nsCls)
                        {
                            topLevelClasses.Add(nsCls);
                        }
                    }

                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var exact = topLevelClasses.FirstOrDefault(
                c => string.Equals(c.Identifier.ValueText, preferredName, StringComparison.Ordinal));
            if (exact is not null)
            {
                return exact;
            }
        }

        return topLevelClasses.FirstOrDefault()
               ?? root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
    }

    public static string ExtractLineIndent(SyntaxTriviaList leadingTrivia)
    {
        var startIdx = 0;
        for (var i = leadingTrivia.Count - 1; i >= 0; i--)
        {
            var trivia = leadingTrivia[i];
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
                || trivia.IsDirective)
            {
                startIdx = i + 1;
                break;
            }
        }

        if (startIdx < leadingTrivia.Count && leadingTrivia[startIdx].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            return leadingTrivia[startIdx].ToString();
        }

        return string.Empty;
    }

    public static string FindChildMemberIndent(ClassDeclarationSyntax classDecl, string fallbackIndent = "        ")
    {
        ArgumentNullException.ThrowIfNull(classDecl);

        foreach (var prop in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (StartsOnOwnLine(prop))
            {
                var indent = ExtractLineIndent(prop.GetLeadingTrivia());
                if (!string.IsNullOrEmpty(indent))
                {
                    return indent;
                }
            }
        }

        foreach (var member in classDecl.Members)
        {
            if (StartsOnOwnLine(member))
            {
                var indent = ExtractLineIndent(member.GetLeadingTrivia());
                if (!string.IsNullOrEmpty(indent))
                {
                    return indent;
                }
            }
        }

        var classIndent = ExtractLineIndent(classDecl.GetLeadingTrivia());
        if (!string.IsNullOrEmpty(classIndent))
        {
            return classIndent + (classIndent.Contains('\t', StringComparison.Ordinal) ? "\t" : "    ");
        }

        return fallbackIndent;
    }

    public static string FindBlockStatementIndent(BlockSyntax block, string fallbackIndent = "            ")
    {
        ArgumentNullException.ThrowIfNull(block);

        foreach (var stmt in block.Statements)
        {
            if (StartsOnOwnLine(stmt))
            {
                var indent = ExtractLineIndent(stmt.GetLeadingTrivia());
                if (!string.IsNullOrEmpty(indent))
                {
                    return indent;
                }
            }
        }

        var braceIndent = ExtractLineIndent(block.OpenBraceToken.LeadingTrivia);
        if (string.IsNullOrEmpty(braceIndent) && block.Parent is not null)
        {
            braceIndent = ExtractLineIndent(block.Parent.GetLeadingTrivia());
        }

        if (!string.IsNullOrEmpty(braceIndent))
        {
            return braceIndent + (braceIndent.Contains('\t', StringComparison.Ordinal) ? "\t" : "    ");
        }

        return fallbackIndent;
    }

    public static SyntaxTriviaList StripEmptyLineBeforeRemoval(SyntaxTriviaList triviaList)
    {
        if (triviaList.Count == 0)
        {
            return triviaList;
        }

        if (triviaList[0].IsKind(SyntaxKind.EndOfLineTrivia))
        {
            return SyntaxFactory.TriviaList(triviaList.Skip(1));
        }

        if (triviaList.Count >= 2
            && triviaList[0].IsKind(SyntaxKind.WhitespaceTrivia)
            && triviaList[1].IsKind(SyntaxKind.EndOfLineTrivia))
        {
            return SyntaxFactory.TriviaList(triviaList.Skip(2));
        }

        return triviaList;
    }

    internal static ClassDeclarationSyntax InsertMemberAt(
        ClassDeclarationSyntax classDecl,
        int insertIndex,
        MemberDeclarationSyntax newMember,
        string indent,
        string newLine)
    {
        var members = classDecl.Members;
        var clampedIndex = Math.Clamp(insertIndex, 0, members.Count);

        var precededByNewLine = clampedIndex == 0
            ? EndsWithLineBreak(classDecl.OpenBraceToken.TrailingTrivia)
            : EndsWithLineBreak(members[clampedIndex - 1].GetTrailingTrivia());

        var leading = precededByNewLine
            ? SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indent))
            : SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(newLine), SyntaxFactory.Whitespace(indent));

        var trailing = SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(newLine));
        var preparedMember = newMember.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);

        var closeBrace = classDecl.CloseBraceToken;
        if (members.Count == 0
            && !EndsWithLineBreak(classDecl.OpenBraceToken.TrailingTrivia)
            && string.IsNullOrEmpty(ExtractLineIndent(closeBrace.LeadingTrivia)))
        {
            var classIndent = ExtractLineIndent(classDecl.GetLeadingTrivia());
            if (!string.IsNullOrEmpty(classIndent))
            {
                closeBrace = closeBrace.WithLeadingTrivia(SyntaxFactory.Whitespace(classIndent));
            }
        }

        var updatedMembers = members.Insert(clampedIndex, preparedMember);
        if (!precededByNewLine && clampedIndex + 1 < updatedMembers.Count)
        {
            var nextMember = updatedMembers[clampedIndex + 1];
            if (string.IsNullOrEmpty(ExtractLineIndent(nextMember.GetLeadingTrivia())))
            {
                updatedMembers = updatedMembers.Replace(
                    nextMember,
                    nextMember.WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indent))));
            }
        }

        return classDecl
            .WithMembers(updatedMembers)
            .WithCloseBraceToken(closeBrace);
    }

    internal static ClassDeclarationSyntax RemoveMemberAt(ClassDeclarationSyntax classDecl, int index)
    {
        var members = classDecl.Members;
        var target = members[index];
        var preserved = ExtractPreservedTriviaOnRemoval(target.GetLeadingTrivia());

        var updatedMembers = members.RemoveAt(index);
        var closeBrace = classDecl.CloseBraceToken;

        if (index < updatedMembers.Count)
        {
            var next = updatedMembers[index];
            var nextLeading = next.GetLeadingTrivia();

            if (preserved.Count == 0 && index == 0 && EndsWithLineBreak(classDecl.OpenBraceToken.TrailingTrivia))
            {
                nextLeading = StripEmptyLineBeforeRemoval(nextLeading);
            }

            if (preserved.Count > 0)
            {
                nextLeading = SyntaxFactory.TriviaList(preserved.Concat(nextLeading));
            }

            if (!nextLeading.Equals(next.GetLeadingTrivia()))
            {
                updatedMembers = updatedMembers.Replace(next, next.WithLeadingTrivia(nextLeading));
            }
        }
        else if (preserved.Count > 0)
        {
            var closeLeading = SyntaxFactory.TriviaList(preserved.Concat(closeBrace.LeadingTrivia));
            closeBrace = closeBrace.WithLeadingTrivia(closeLeading);
        }

        return classDecl
            .WithMembers(updatedMembers)
            .WithCloseBraceToken(closeBrace);
    }

    internal static BlockSyntax InsertStatementAt(
        BlockSyntax block,
        int insertIndex,
        StatementSyntax newStatement,
        string indent,
        string newLine)
    {
        var statements = block.Statements;
        var clampedIndex = Math.Clamp(insertIndex, 0, statements.Count);

        var precededByNewLine = clampedIndex == 0
            ? EndsWithLineBreak(block.OpenBraceToken.TrailingTrivia)
            : EndsWithLineBreak(statements[clampedIndex - 1].GetTrailingTrivia());

        var leading = precededByNewLine
            ? SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indent))
            : SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(newLine), SyntaxFactory.Whitespace(indent));

        var trailing = SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(newLine));
        var prepared = newStatement.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);

        var closeBrace = block.CloseBraceToken;
        if (statements.Count == 0
            && !EndsWithLineBreak(block.OpenBraceToken.TrailingTrivia)
            && string.IsNullOrEmpty(ExtractLineIndent(closeBrace.LeadingTrivia)))
        {
            var blockIndent = ExtractLineIndent(block.OpenBraceToken.LeadingTrivia);
            if (!string.IsNullOrEmpty(blockIndent))
            {
                closeBrace = closeBrace.WithLeadingTrivia(SyntaxFactory.Whitespace(blockIndent));
            }
        }

        var updatedStatements = statements.Insert(clampedIndex, prepared);
        if (!precededByNewLine && clampedIndex + 1 < updatedStatements.Count)
        {
            var nextStmt = updatedStatements[clampedIndex + 1];
            if (string.IsNullOrEmpty(ExtractLineIndent(nextStmt.GetLeadingTrivia())))
            {
                updatedStatements = updatedStatements.Replace(
                    nextStmt,
                    nextStmt.WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indent))));
            }
        }

        return block
            .WithStatements(updatedStatements)
            .WithCloseBraceToken(closeBrace);
    }

    internal static BlockSyntax RemoveStatementAt(BlockSyntax block, int index)
    {
        var statements = block.Statements;
        var target = statements[index];
        var preserved = ExtractPreservedTriviaOnRemoval(target.GetLeadingTrivia());

        var updatedStatements = statements.RemoveAt(index);
        var closeBrace = block.CloseBraceToken;

        if (index < updatedStatements.Count)
        {
            var next = updatedStatements[index];
            var nextLeading = next.GetLeadingTrivia();

            if (preserved.Count == 0 && index == 0 && EndsWithLineBreak(block.OpenBraceToken.TrailingTrivia))
            {
                nextLeading = StripEmptyLineBeforeRemoval(nextLeading);
            }

            if (preserved.Count > 0)
            {
                nextLeading = SyntaxFactory.TriviaList(preserved.Concat(nextLeading));
            }

            if (!nextLeading.Equals(next.GetLeadingTrivia()))
            {
                updatedStatements = updatedStatements.Replace(next, next.WithLeadingTrivia(nextLeading));
            }
        }
        else if (preserved.Count > 0)
        {
            var closeLeading = SyntaxFactory.TriviaList(preserved.Concat(closeBrace.LeadingTrivia));
            closeBrace = closeBrace.WithLeadingTrivia(closeLeading);
        }

        return block
            .WithStatements(updatedStatements)
            .WithCloseBraceToken(closeBrace);
    }

    internal static bool AreStatementsTokenEquivalent(StatementSyntax left, StatementSyntax right)
    {
        using var e1 = left.DescendantTokens().GetEnumerator();
        using var e2 = right.DescendantTokens().GetEnumerator();

        while (true)
        {
            var m1 = e1.MoveNext();
            var m2 = e2.MoveNext();
            if (m1 != m2)
            {
                return false;
            }

            if (!m1)
            {
                return true;
            }

            var t1 = e1.Current;
            var t2 = e2.Current;
            if (t1.RawKind != t2.RawKind || !string.Equals(t1.ValueText, t2.ValueText, StringComparison.Ordinal))
            {
                return false;
            }
        }
    }

    internal static string NormalizeLineEndings(string text, string newLine) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newLine, StringComparison.Ordinal);

    internal static SyntaxTriviaList NormalizeTriviaNewLines(SyntaxTriviaList triviaList, string newLine)
    {
        if (triviaList.Count == 0)
        {
            return triviaList;
        }

        var normalized = new List<SyntaxTrivia>(triviaList.Count);
        foreach (var trivia in triviaList)
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                normalized.Add(SyntaxFactory.EndOfLine(newLine));
            }
            else
            {
                normalized.Add(trivia);
            }
        }

        return SyntaxFactory.TriviaList(normalized);
    }

    private static bool StartsOnOwnLine(SyntaxNode node)
    {
        var leading = node.GetLeadingTrivia();
        if (leading.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia)
                             || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                             || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
                             || t.IsDirective))
        {
            return true;
        }

        var firstToken = node.GetFirstToken();
        if (firstToken.IsKind(SyntaxKind.None))
        {
            return false;
        }

        var prevToken = firstToken.GetPreviousToken(includeZeroWidth: false);
        return !prevToken.IsKind(SyntaxKind.None) && EndsWithLineBreak(prevToken.TrailingTrivia);
    }

    private static bool EndsWithLineBreak(SyntaxTriviaList trailingTrivia)
    {
        if (trailingTrivia.Count == 0)
        {
            return false;
        }

        for (var i = trailingTrivia.Count - 1; i >= 0; i--)
        {
            var trivia = trailingTrivia[i];
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsDirective)
            {
                return true;
            }

            if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                return false;
            }
        }

        return false;
    }

    private static SyntaxTriviaList ExtractPreservedTriviaOnRemoval(SyntaxTriviaList leadingTrivia)
    {
        if (leadingTrivia.Count == 0)
        {
            return SyntaxTriviaList.Empty;
        }

        // Preserve everything up through the last preprocessor directive or blank-line boundary
        // preceding non-trivia comments (so section headers & #regions are never swallowed).
        var splitIndex = 0;
        var lastEolIndex = -1;
        var seenContentBeforeBlankLine = false;

        for (var i = 0; i < leadingTrivia.Count; i++)
        {
            var trivia = leadingTrivia[i];
            if (trivia.IsDirective)
            {
                splitIndex = i + 1;
                lastEolIndex = i;
                seenContentBeforeBlankLine = false;
                continue;
            }

            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                seenContentBeforeBlankLine = true;
                continue;
            }

            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                if (lastEolIndex >= 0 && seenContentBeforeBlankLine && IsWhitespaceOnlyRange(leadingTrivia, lastEolIndex + 1, i))
                {
                    splitIndex = i + 1;
                }

                lastEolIndex = i;
            }
        }

        if (splitIndex == 0)
        {
            return SyntaxTriviaList.Empty;
        }

        return SyntaxFactory.TriviaList(leadingTrivia.Take(splitIndex));
    }

    private static bool IsWhitespaceOnlyRange(SyntaxTriviaList list, int startInclusive, int endExclusive)
    {
        for (var i = startInclusive; i < endExclusive; i++)
        {
            if (!list[i].IsKind(SyntaxKind.WhitespaceTrivia))
            {
                return false;
            }
        }

        return true;
    }
}
