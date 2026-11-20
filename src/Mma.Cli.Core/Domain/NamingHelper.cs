using System.Text.RegularExpressions;

namespace Mma.Cli.Core.Domain;

public static partial class NamingHelper
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while"
    };

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SolutionNameRegex();

    public static string Pluralize(string componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return componentName;
        }

        if (componentName.EndsWith("s", StringComparison.Ordinal))
        {
            return $"{componentName}es";
        }

        if (componentName.EndsWith("y", StringComparison.Ordinal))
        {
            return $"{componentName[..^1]}ies";
        }

        return $"{componentName}s";
    }

    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return string.Create(name.Length, name, static (span, src) =>
        {
            span[0] = char.ToLowerInvariant(src[0]);
            src.AsSpan(1).CopyTo(span[1..]);
        });
    }

    public static bool IsValidIdentifier(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return IdentifierRegex().IsMatch(name) && !CSharpKeywords.Contains(name);
    }

    public static bool IsValidSolutionName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !SolutionNameRegex().IsMatch(name))
        {
            return false;
        }

        foreach (var segment in name.Split('.'))
        {
            if (!IsValidIdentifier(segment))
            {
                return false;
            }
        }

        return true;
    }
}
