internal static class CatalogMappingHelpers
{
    public const int MaxDiscoveredTables = 500;

    private static readonly string[] AccentColors =
    [
        "blue",
        "indigo",
        "cyan",
        "emerald",
        "amber",
        "rose",
        "slate",
    ];

    private static readonly HashSet<string> ValidReferentialActions = new(StringComparer.Ordinal)
    {
        "CASCADE",
        "SET NULL",
        "SET DEFAULT",
        "RESTRICT",
        "NO ACTION",
    };

    public static string GetAccentColor(int index) =>
        AccentColors[index % AccentColors.Length];

    public static (double X, double Y) GetGridPosition(int index) =>
        (60 + (index % 4) * 340, 60 + (index / 4) * 300);

    public static string NormalizeReferentialAction(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "NO ACTION";
        }

        var normalized = raw.Replace('_', ' ').Trim().ToUpperInvariant();
        return ValidReferentialActions.Contains(normalized) ? normalized : "NO ACTION";
    }

    public static string StripOuterCheckWrapper(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        var trimmed = expression.Trim();
        if (trimmed.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[5..].Trim();
        }

        while (trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')' && HasMatchingOuterParens(trimmed))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    public static string StripSingleOuterParens(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        var trimmed = expression.Trim();
        while (trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')' && HasMatchingOuterParens(trimmed))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static bool HasMatchingOuterParens(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;

            if (depth == 0 && i < text.Length - 1)
            {
                return false;
            }
        }

        return depth == 0;
    }
}
