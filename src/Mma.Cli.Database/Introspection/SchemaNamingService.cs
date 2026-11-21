using System.Text;
using System.Text.RegularExpressions;
using Mma.Cli.Core.Domain;

namespace Mma.Cli.Database.Introspection;

public sealed partial class SchemaNamingService
{
    private static readonly Dictionary<string, string> IrregularPlurals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["People"] = "Person",
        ["Children"] = "Child",
        ["Women"] = "Woman",
        ["Men"] = "Man",
        ["Statuses"] = "Status",
        ["Aliases"] = "Alias",
        ["Buses"] = "Bus",
        ["Quizzes"] = "Quiz",
        ["Indices"] = "Index",
        ["Matrices"] = "Matrix",
        ["Vertices"] = "Vertex",
        ["Analyses"] = "Analysis",
        ["Diagnoses"] = "Diagnosis",
        ["Crises"] = "Crisis",
        ["Theses"] = "Thesis",
        ["Bases"] = "Basis",
        ["Movies"] = "Movie",
        ["Cookies"] = "Cookie",
        ["Zombies"] = "Zombie",
        ["Caches"] = "Cache",
        ["Niches"] = "Niche",
        ["Aches"] = "Ache",
        ["Leaves"] = "Leaf",
        ["Shelves"] = "Shelf",
        ["Halves"] = "Half",
        ["Knives"] = "Knife",
        ["Wives"] = "Wife",
        ["Series"] = "Series",
        ["Species"] = "Species",
        ["News"] = "News",
        ["Data"] = "Data"
    };

    private static readonly HashSet<string> InvariantSingularSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ss",
        "us",
        "is",
        "sis"
    };

    private static readonly string[] EsDroppingUsWords =
    [
        "Bonuses",
        "Campuses",
        "Censuses",
        "Circuses",
        "Focuses",
        "Viruses"
    ];

    [GeneratedRegex(@"\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParenthesizedArgsRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiWhitespaceRegex();

    public string ToEntityName(string rawTableName, IReadOnlyDictionary<string, string>? overrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTableName);

        var trimmed = rawTableName.Trim();
        if (TryFindOverride(trimmed, overrides, out var overrideName))
        {
            return EnsureValidIdentifier(overrideName, "Entity");
        }

        var pascal = ToPascalCase(trimmed, "Entity");
        var singular = SingularizePascalName(pascal);
        return EnsureValidIdentifier(singular, "Entity");
    }

    public string ToPropertyName(string rawColumnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawColumnName);

        var pascal = ToPascalCase(rawColumnName.Trim(), "Property");
        return EnsureValidIdentifier(pascal, "Property");
    }

    public string MapSqlType(DatabaseProviderKind provider, string? rawSqlType)
    {
        if (string.IsNullOrWhiteSpace(rawSqlType))
        {
            return "string";
        }

        var collapsed = MultiWhitespaceRegex().Replace(rawSqlType.Trim(), " ").ToLowerInvariant();
        if (collapsed.StartsWith('[') && collapsed.EndsWith(']') && collapsed.Length > 2)
        {
            collapsed = collapsed[1..^1].Trim();
        }

        if (provider == DatabaseProviderKind.MySql
            && (collapsed.StartsWith("tinyint(1)", StringComparison.Ordinal)
                || collapsed.StartsWith("bit(1)", StringComparison.Ordinal)))
        {
            return "bool";
        }

        var stripped = ParenthesizedArgsRegex().Replace(collapsed, string.Empty);
        stripped = MultiWhitespaceRegex().Replace(stripped, " ").Trim();

        if (stripped.EndsWith(" zerofill", StringComparison.Ordinal))
        {
            stripped = stripped[..^" zerofill".Length].Trim();
        }

        if (stripped.EndsWith(" unsigned", StringComparison.Ordinal))
        {
            var baseUnsigned = stripped[..^" unsigned".Length].Trim();
            if (baseUnsigned is "bigint" or "int8")
            {
                return "long";
            }

            if (baseUnsigned is "int" or "integer" or "int4" or "mediumint" or "smallint" or "tinyint" or "int2")
            {
                return "int";
            }

            stripped = baseUnsigned;
        }

        if (provider == DatabaseProviderKind.SqlServer && stripped is "timestamp" or "rowversion")
        {
            return "byte[]";
        }

        return stripped switch
        {
            "uniqueidentifier" or "uuid" or "guid" => "Guid",

            "int" or "integer" or "int4" or "smallint" or "tinyint" or "int2" or "mediumint"
                or "year" or "serial" or "smallserial" or "serial2" or "serial4" => "int",

            "bigint" or "int8" or "bigserial" or "serial8" => "long",

            "bit" or "boolean" or "bool" => "bool",

            "decimal" or "numeric" or "money" or "smallmoney" or "double" or "double precision" or "float8" => "decimal",

            "real" or "float" or "float4" or "single" => "float",

            "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "date"
                or "timestamp" or "timestamptz" or "timestamp with time zone" or "timestamp without time zone" => "DateTime",

            "time" or "timetz" or "time with time zone" or "time without time zone" or "interval" => "TimeSpan",

            "varbinary" or "binary" or "image" or "bytea" or "blob"
                or "tinyblob" or "mediumblob" or "longblob" => "byte[]",

            _ => "string"
        };
    }

    public bool IsExcluded(string? name, IReadOnlyList<string>? patterns)
    {
        if (string.IsNullOrWhiteSpace(name) || patterns is null || patterns.Count == 0)
        {
            return false;
        }

        var candidate = name.AsSpan().Trim();
        for (var i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (MatchesGlob(candidate, pattern.AsSpan().Trim()))
            {
                return true;
            }
        }

        return false;
    }

    internal static int? TryExtractMaxLength(string? rawSqlType, string clrType)
    {
        if (string.IsNullOrWhiteSpace(rawSqlType)
            || (!string.Equals(clrType, "string", StringComparison.Ordinal)
                && !string.Equals(clrType, "byte[]", StringComparison.Ordinal)))
        {
            return null;
        }

        var openIdx = rawSqlType.IndexOf('(');
        var closeIdx = rawSqlType.IndexOf(')', openIdx + 1);
        if (openIdx < 0 || closeIdx <= openIdx + 1)
        {
            return null;
        }

        var inside = rawSqlType.AsSpan(openIdx + 1, closeIdx - openIdx - 1).Trim();
        if (inside.Contains(',') || inside.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(inside, out var len) && len > 0 ? len : null;
    }

    private static bool TryFindOverride(
        string rawTableName,
        IReadOnlyDictionary<string, string>? overrides,
        out string value)
    {
        if (overrides is null || overrides.Count == 0)
        {
            value = string.Empty;
            return false;
        }

        if (overrides.TryGetValue(rawTableName, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            value = exact.Trim();
            return true;
        }

        foreach (var kvp in overrides)
        {
            if (string.Equals(kvp.Key, rawTableName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(kvp.Value))
            {
                value = kvp.Value.Trim();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string ToPascalCase(string raw, string fallback)
    {
        var sb = new StringBuilder(raw.Length);
        var tokenStart = -1;

        for (var i = 0; i <= raw.Length; i++)
        {
            var isAlnum = i < raw.Length && char.IsLetterOrDigit(raw[i]);
            if (isAlnum)
            {
                if (tokenStart < 0)
                {
                    tokenStart = i;
                }
                continue;
            }

            if (tokenStart >= 0)
            {
                AppendNormalizedToken(sb, raw.AsSpan(tokenStart, i - tokenStart));
                tokenStart = -1;
            }
        }

        return sb.Length > 0 ? sb.ToString() : fallback;
    }

    private static void AppendNormalizedToken(StringBuilder sb, ReadOnlySpan<char> token)
    {
        if (token.IsEmpty)
        {
            return;
        }

        var hasLetter = false;
        var allUpper = true;
        foreach (var ch in token)
        {
            if (char.IsLetter(ch))
            {
                hasLetter = true;
                if (!char.IsUpper(ch))
                {
                    allUpper = false;
                    break;
                }
            }
        }

        sb.Append(char.ToUpperInvariant(token[0]));
        for (var i = 1; i < token.Length; i++)
        {
            sb.Append(hasLetter && allUpper ? char.ToLowerInvariant(token[i]) : token[i]);
        }
    }

    private static string SingularizePascalName(string pascal)
    {
        if (pascal.Length <= 2)
        {
            return pascal;
        }

        var lastWordStart = FindLastPascalWordStart(pascal);
        var prefix = pascal[..lastWordStart];
        var lastWord = pascal[lastWordStart..];

        if (IrregularPlurals.TryGetValue(lastWord, out var irregularSingular))
        {
            return prefix + irregularSingular;
        }

        foreach (var esUsWord in EsDroppingUsWords)
        {
            if (lastWord.Equals(esUsWord, StringComparison.OrdinalIgnoreCase))
            {
                return prefix + lastWord[..^2];
            }
        }

        foreach (var suffix in InvariantSingularSuffixes)
        {
            if (lastWord.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return pascal;
            }
        }

        if (lastWord.Length > 3 && lastWord.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
        {
            var beforeIes = lastWord[^4];
            if (!IsVowel(beforeIes))
            {
                return prefix + lastWord[..^3] + "y";
            }
        }

        if (lastWord.Length > 4 && lastWord.EndsWith("sses", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + lastWord[..^2];
        }

        if (lastWord.Length > 3 && (lastWord.EndsWith("xes", StringComparison.OrdinalIgnoreCase)
                                    || lastWord.EndsWith("ches", StringComparison.OrdinalIgnoreCase)
                                    || lastWord.EndsWith("shes", StringComparison.OrdinalIgnoreCase)))
        {
            return prefix + lastWord[..^2];
        }

        if (lastWord.Length > 4 && lastWord.EndsWith("zzes", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + lastWord[..^2];
        }

        if (lastWord.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            && !lastWord.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + lastWord[..^1];
        }

        return pascal;
    }

    private static int FindLastPascalWordStart(string pascal)
    {
        for (var i = pascal.Length - 1; i > 0; i--)
        {
            if (char.IsUpper(pascal[i]) && char.IsLower(pascal[i - 1]))
            {
                return i;
            }
        }

        return 0;
    }

    private static bool IsVowel(char ch) =>
        char.ToLowerInvariant(ch) is 'a' or 'e' or 'i' or 'o' or 'u';

    private static string EnsureValidIdentifier(string candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return fallback;
        }

        if (NamingHelper.IsValidIdentifier(candidate))
        {
            return candidate;
        }

        var prefixed = $"_{candidate}";
        if (NamingHelper.IsValidIdentifier(prefixed))
        {
            return prefixed;
        }

        var sanitized = new StringBuilder(candidate.Length + 1);
        foreach (var ch in candidate)
        {
            sanitized.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        var cleaned = sanitized.ToString();
        if (cleaned.Length == 0 || char.IsDigit(cleaned[0]) || !NamingHelper.IsValidIdentifier(cleaned))
        {
            cleaned = $"_{cleaned}";
        }

        return NamingHelper.IsValidIdentifier(cleaned) ? cleaned : fallback;
    }

    private static bool MatchesGlob(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern)
    {
        var ti = 0;
        var pi = 0;
        var starIdx = -1;
        var matchIdx = 0;

        while (ti < text.Length)
        {
            if (pi < pattern.Length
                && (pattern[pi] == '?'
                    || char.ToUpperInvariant(pattern[pi]) == char.ToUpperInvariant(text[ti])))
            {
                ti++;
                pi++;
            }
            else if (pi < pattern.Length && pattern[pi] == '*')
            {
                starIdx = pi;
                matchIdx = ti;
                pi++;
            }
            else if (starIdx != -1)
            {
                pi = starIdx + 1;
                matchIdx++;
                ti = matchIdx;
            }
            else
            {
                return false;
            }
        }

        while (pi < pattern.Length && pattern[pi] == '*')
        {
            pi++;
        }

        return pi == pattern.Length;
    }
}
