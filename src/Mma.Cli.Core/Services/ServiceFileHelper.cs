using System.Text;
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

    public static bool RemoveLineContaining(List<string> lines, string snippet)
    {
        var idx = lines.FindIndex(l => l.Contains(snippet, StringComparison.Ordinal));
        if (idx < 0)
        {
            return false;
        }

        lines.RemoveAt(idx);
        return true;
    }
}
