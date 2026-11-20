using System.Text;
using System.Text.RegularExpressions;
using Mma.Cli.Core.Events;

namespace Mma.Cli.Core.Services;

internal static class ServiceFileHelper
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Report(
        IProgress<GenerationEvent>? progress,
        GenerationStage stage,
        string message,
        string? filePath = null) =>
        progress?.Report(GenerationEvent.Create(stage, message, filePath));

    public static async Task<List<string>> ReadLinesAsync(string path, CancellationToken ct)
    {
        var lines = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    public static async Task WriteFileAsync(string path, string content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, Utf8NoBom, ct).ConfigureAwait(false);
    }

    public static Task WriteLinesAsync(string path, IEnumerable<string> lines, CancellationToken ct) =>
        WriteFileAsync(path, string.Join('\n', lines), ct);

    public static void InsertBeforeClosingBraces(List<string> lines, string lineToInsert)
    {
        var insertIndex = Math.Max(0, lines.Count - 2);
        lines.Insert(insertIndex, lineToInsert);
    }

    public static void InsertAtAnchorOffset(List<string> lines, string anchorSnippet, int offset, string lineToInsert)
    {
        var anchorIdx = lines.FindIndex(l => l.Contains(anchorSnippet, StringComparison.Ordinal));
        if (anchorIdx >= 0)
        {
            lines.Insert(Math.Min(anchorIdx + offset, lines.Count), lineToInsert);
        }
    }

    public static bool TryInsertAfterEntityConfigAnchor(List<string> lines, string lineToInsert)
    {
        var anchorIdx = lines.FindIndex(l => l.Contains("builder.HasIndex(e => e.IsDeleted);", StringComparison.Ordinal));
        if (anchorIdx >= 0)
        {
            var cursor = anchorIdx + 1;
            while (cursor < lines.Count && string.IsNullOrWhiteSpace(lines[cursor]))
            {
                cursor++;
            }

            if (cursor < lines.Count && !lines[cursor].TrimStart().StartsWith("}", StringComparison.Ordinal))
            {
                while (cursor < lines.Count && !lines[cursor].TrimEnd().EndsWith(";", StringComparison.Ordinal))
                {
                    if (lines[cursor].TrimStart().StartsWith("}", StringComparison.Ordinal))
                    {
                        lines.Insert(cursor, lineToInsert);
                        return true;
                    }

                    cursor++;
                }

                if (cursor < lines.Count)
                {
                    lines.Insert(cursor + 1, lineToInsert);
                    return true;
                }
            }
            else
            {
                lines.Insert(Math.Min(cursor, lines.Count), lineToInsert);
                return true;
            }
        }

        var configureIdx = lines.FindIndex(l => l.Contains("void Configure(", StringComparison.Ordinal));
        if (configureIdx < 0)
        {
            return false;
        }

        var braceDepth = 0;
        var opened = false;
        for (var i = configureIdx; i < lines.Count; i++)
        {
            foreach (var ch in lines[i])
            {
                if (ch == '{')
                {
                    braceDepth++;
                    opened = true;
                }
                else if (ch == '}')
                {
                    braceDepth--;
                    if (opened && braceDepth == 0)
                    {
                        lines.Insert(i, lineToInsert);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static bool HasPropertyNamed(IEnumerable<string> lines, string propertyName)
    {
        var pattern = BuildPropertyPattern(propertyName);
        return lines.Any(l => Regex.IsMatch(l, pattern, RegexOptions.CultureInvariant));
    }

    public static bool RemovePropertyNamed(List<string> lines, string propertyName)
    {
        var pattern = BuildPropertyPattern(propertyName);
        return RemoveLineMatching(lines, l => Regex.IsMatch(l, pattern, RegexOptions.CultureInvariant));
    }

    public static bool RemoveLineMatching(List<string> lines, Func<string, bool> predicate)
    {
        var idx = lines.FindIndex(l => predicate(l));
        if (idx < 0)
        {
            return false;
        }

        lines.RemoveAt(idx);
        return true;
    }

    public static bool RemoveLineContaining(List<string> lines, string snippet) =>
        RemoveLineMatching(lines, l => l.Contains(snippet, StringComparison.Ordinal));

    private static string BuildPropertyPattern(string propertyName) =>
        $@"^\s*public\s+(?!class\b|partial\b|interface\b|record\b|struct\b|enum\b)(?:virtual\s+)?[A-Za-z_][A-Za-z0-9_<>\[\]\?,\s]*?\s+{Regex.Escape(propertyName)}\s*\{{\s*(?:get|init)\b";
}
