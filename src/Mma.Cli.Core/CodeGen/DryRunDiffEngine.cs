using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Services;

namespace Mma.Cli.Core.CodeGen;

public class DryRunDiffEngine : IDryRunDiffEngine
{
    private const int DiffContextLines = 3;
    private const int MaxDpCells = 2_000_000;

    private readonly ILogger<DryRunDiffEngine>? _logger;

    public DryRunDiffEngine(ILogger<DryRunDiffEngine>? logger = null)
    {
        _logger = logger;
    }

    public async Task<OperationResult<ExecutionPlan>> ComputePlanAsync(
        string solutionPath,
        IReadOnlyList<StagedFileChange> changes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<ExecutionPlan>.Failure(ErrorCodes.CANCELLED, "Diff plan computation was cancelled.");
        }

        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return OperationResult<ExecutionPlan>.Failure(ErrorCodes.VALIDATION_ERROR, "Solution path cannot be empty.");
        }

        string fullSolutionPath;
        try
        {
            fullSolutionPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(solutionPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<ExecutionPlan>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid solution path '{solutionPath}': {ex.Message}",
                ex.ToString());
        }

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seenPaths = new HashSet<string>(pathComparer);
        var plannedDiffs = new List<PlannedFileDiff>(changes.Count);

        try
        {
            foreach (var change in changes)
            {
                ct.ThrowIfCancellationRequested();

                if (change is null || string.IsNullOrWhiteSpace(change.FullPath))
                {
                    return OperationResult<ExecutionPlan>.Failure(
                        ErrorCodes.VALIDATION_ERROR,
                        "Staged file change path cannot be empty.");
                }

                if (!Enum.IsDefined(change.Kind))
                {
                    return OperationResult<ExecutionPlan>.Failure(
                        ErrorCodes.VALIDATION_ERROR,
                        $"Unsupported file change kind '{change.Kind}'.");
                }

                if (change.Kind is FileChangeKind.Create or FileChangeKind.Modify && change.NewContent is null)
                {
                    return OperationResult<ExecutionPlan>.Failure(
                        ErrorCodes.VALIDATION_ERROR,
                        $"Staged {change.Kind} for '{change.FullPath}' requires non-null content.");
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(change.FullPath);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return OperationResult<ExecutionPlan>.Failure(
                        ErrorCodes.VALIDATION_ERROR,
                        $"Invalid staged file path '{change.FullPath}': {ex.Message}",
                        ex.ToString());
                }

                if (!seenPaths.Add(fullPath))
                {
                    return OperationResult<ExecutionPlan>.Failure(
                        ErrorCodes.VALIDATION_ERROR,
                        $"Duplicate staged file path '{fullPath}'.");
                }

                var relativePath = Path.GetRelativePath(fullSolutionPath, fullPath).Replace('\\', '/');

                string? originalHash = null;
                var oldText = string.Empty;

                if (change.Kind != FileChangeKind.Create && FileExists(fullPath))
                {
                    var originalBytes = await ReadBytesAsync(fullPath, ct).ConfigureAwait(false);
                    originalHash = ComputeSha256(originalBytes);
                    oldText = DecodeText(originalBytes);
                }

                string? newContent = change.Kind == FileChangeKind.Delete ? null : change.NewContent;
                string? newHash = newContent is null
                    ? null
                    : ComputeSha256(ServiceFileHelper.Utf8NoBom.GetBytes(newContent));
                var newText = newContent ?? string.Empty;

                var unifiedDiff = BuildUnifiedDiff(relativePath, change.Kind, oldText, newText, originalHash, newHash);

                plannedDiffs.Add(new PlannedFileDiff(
                    fullPath,
                    relativePath,
                    change.Kind,
                    unifiedDiff,
                    originalHash,
                    newHash,
                    newContent));
            }

            var planHash = ComputePlanHash(plannedDiffs);
            return OperationResult<ExecutionPlan>.Success(new ExecutionPlan(
                fullSolutionPath,
                planHash,
                plannedDiffs,
                DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<ExecutionPlan>.Failure(ErrorCodes.CANCELLED, "Diff plan computation was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to compute diff plan for '{SolutionPath}'.", fullSolutionPath);
            return OperationResult<ExecutionPlan>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to compute execution plan: {ex.Message}",
                ex.ToString());
        }
    }

    public async Task<OperationResult<GenerationSummary>> CommitAsync(
        ExecutionPlan plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Commit operation was cancelled.");
        }

        var snapshots = new List<(PlannedFileDiff Diff, byte[]? OriginalBytes)>(plan.FileChanges.Count);

        try
        {
            // Phase 1: Validate inputs & detect any drift before mutating anything on disk.
            foreach (var diff in plan.FileChanges)
            {
                ct.ThrowIfCancellationRequested();

                if (diff.Kind is FileChangeKind.Create or FileChangeKind.Modify && diff.NewContent is null)
                {
                    return OperationResult<GenerationSummary>.Failure(
                        ErrorCodes.VALIDATION_ERROR,
                        $"Planned {diff.Kind} for '{diff.FullPath}' is missing content.");
                }

                if (diff.Kind == FileChangeKind.Create)
                {
                    if (FileExists(diff.FullPath))
                    {
                        return OperationResult<GenerationSummary>.Failure(
                            ErrorCodes.MANIFEST_CONFLICT,
                            $"Cannot create '{diff.FullPath}': target file already exists on disk.");
                    }

                    snapshots.Add((diff, null));
                }
                else
                {
                    if (!FileExists(diff.FullPath))
                    {
                        return OperationResult<GenerationSummary>.Failure(
                            ErrorCodes.MANIFEST_CONFLICT,
                            $"Cannot apply {diff.Kind} to '{diff.FullPath}': file no longer exists on disk.");
                    }

                    var currentBytes = await ReadBytesAsync(diff.FullPath, ct).ConfigureAwait(false);
                    var currentHash = ComputeSha256(currentBytes);
                    if (!string.Equals(currentHash, diff.OriginalHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return OperationResult<GenerationSummary>.Failure(
                            ErrorCodes.MANIFEST_CONFLICT,
                            $"Drift detected on '{diff.FullPath}': expected '{diff.OriginalHash}', found '{currentHash}'.");
                    }

                    snapshots.Add((diff, currentBytes));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Commit operation was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed during pre-commit drift check for '{SolutionPath}'.", plan.SolutionPath);
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to validate target files before commit: {ex.Message}",
                ex.ToString());
        }

        // Phase 2: Apply writes sequentially with reverse-order rollback on any error.
        var appliedUndo = new List<(string FullPath, bool ExistedBefore, byte[]? OriginalBytes)>(snapshots.Count);
        var created = new List<string>();
        var modified = new List<string>();
        var deleted = new List<string>();

        try
        {
            foreach (var (diff, originalBytes) in snapshots)
            {
                ct.ThrowIfCancellationRequested();

                switch (diff.Kind)
                {
                    case FileChangeKind.Create:
                        appliedUndo.Add((diff.FullPath, ExistedBefore: false, OriginalBytes: null));
                        await WriteBytesAsync(
                            diff.FullPath,
                            ServiceFileHelper.Utf8NoBom.GetBytes(diff.NewContent!),
                            ct).ConfigureAwait(false);
                        created.Add(diff.FullPath);
                        break;

                    case FileChangeKind.Modify:
                        appliedUndo.Add((diff.FullPath, ExistedBefore: true, OriginalBytes: originalBytes));
                        await WriteBytesAsync(
                            diff.FullPath,
                            ServiceFileHelper.Utf8NoBom.GetBytes(diff.NewContent!),
                            ct).ConfigureAwait(false);
                        modified.Add(diff.FullPath);
                        break;

                    case FileChangeKind.Delete:
                        appliedUndo.Add((diff.FullPath, ExistedBefore: true, OriginalBytes: originalBytes));
                        await DeleteFileAsync(diff.FullPath, ct).ConfigureAwait(false);
                        deleted.Add(diff.FullPath);
                        break;

                    default:
                        return OperationResult<GenerationSummary>.Failure(
                            ErrorCodes.VALIDATION_ERROR,
                            $"Unsupported file change kind '{diff.Kind}'.");
                }
            }

            return OperationResult<GenerationSummary>.Success(new GenerationSummary(
                created,
                modified,
                deleted,
                DryRun: false,
                Plan: plan));
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(appliedUndo).ConfigureAwait(false);
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Commit operation was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Mid-batch commit failure; rolling back {Count} change(s).", appliedUndo.Count);
            await RollbackAsync(appliedUndo).ConfigureAwait(false);
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to commit changes: {ex.Message}",
                ex.ToString());
        }
    }

    protected virtual bool FileExists(string fullPath) => File.Exists(fullPath);

    protected virtual Task<byte[]> ReadBytesAsync(string fullPath, CancellationToken ct) =>
        ServiceFileHelper.ReadFileBytesSharedAsync(fullPath, ct);

    protected virtual async Task WriteBytesAsync(string fullPath, byte[] bytes, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(fullPath, bytes, ct).ConfigureAwait(false);
    }

    protected virtual Task DeleteFileAsync(string fullPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private async Task RollbackAsync(List<(string FullPath, bool ExistedBefore, byte[]? OriginalBytes)> appliedUndo)
    {
        for (var i = appliedUndo.Count - 1; i >= 0; i--)
        {
            var (fullPath, existedBefore, originalBytes) = appliedUndo[i];
            try
            {
                if (!existedBefore)
                {
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                }
                else if (originalBytes is not null)
                {
                    var directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    await File.WriteAllBytesAsync(fullPath, originalBytes, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogError(ex, "Failed to roll back file '{FullPath}'.", fullPath);
            }
        }
    }

    private static string ComputeSha256(byte[] bytes) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string DecodeText(byte[] bytes)
    {
        var stripped = ServiceFileHelper.StripUtf8Bom(bytes);
        return Encoding.UTF8.GetString(stripped.Span);
    }

    private static string ComputePlanHash(IReadOnlyList<PlannedFileDiff> plannedDiffs)
    {
        var sb = new StringBuilder();
        foreach (var diff in plannedDiffs
                     .OrderBy(d => d.RelativePath, StringComparer.Ordinal)
                     .ThenBy(d => (int)d.Kind))
        {
            sb.Append(diff.RelativePath).Append('\0')
              .Append((int)diff.Kind).Append('\0')
              .Append(diff.OriginalHash ?? "<null>").Append('\0')
              .Append(diff.NewHash ?? "<null>").Append('\n');
        }

        return ComputeSha256(ServiceFileHelper.Utf8NoBom.GetBytes(sb.ToString()));
    }

    private static string BuildUnifiedDiff(
        string relativePath,
        FileChangeKind kind,
        string oldText,
        string newText,
        string? originalHash,
        string? newHash)
    {
        if (kind == FileChangeKind.Modify && string.Equals(originalHash, newHash, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        var ops = ComputeDiffOps(oldLines, newLines);
        var changedIndices = new List<int>();
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].Op != EditOpKind.Equal)
            {
                changedIndices.Add(i);
            }
        }

        var oldHeader = kind == FileChangeKind.Create ? "--- /dev/null" : $"--- a/{relativePath}";
        var newHeader = kind == FileChangeKind.Delete ? "+++ /dev/null" : $"+++ b/{relativePath}";

        if (changedIndices.Count == 0)
        {
            if (kind == FileChangeKind.Modify)
            {
                return string.Empty;
            }

            return $"{oldHeader}\n{newHeader}";
        }

        var hunks = GroupHunks(changedIndices, ops.Count, DiffContextLines);
        var sb = new StringBuilder();
        sb.Append(oldHeader).Append('\n')
          .Append(newHeader);

        foreach (var (hunkStart, hunkEnd) in hunks)
        {
            var oldCount = 0;
            var newCount = 0;
            var firstOldIdx = -1;
            var firstNewIdx = -1;

            for (var i = hunkStart; i <= hunkEnd; i++)
            {
                var op = ops[i];
                if (op.Op != EditOpKind.Insert)
                {
                    if (firstOldIdx < 0)
                    {
                        firstOldIdx = op.OldIndex;
                    }

                    oldCount++;
                }

                if (op.Op != EditOpKind.Delete)
                {
                    if (firstNewIdx < 0)
                    {
                        firstNewIdx = op.NewIndex;
                    }

                    newCount++;
                }
            }

            var oldStart = oldCount == 0 ? 0 : firstOldIdx + 1;
            var newStart = newCount == 0 ? 0 : firstNewIdx + 1;

            sb.Append('\n')
              .Append("@@ -").Append(oldStart).Append(',').Append(oldCount)
              .Append(" +").Append(newStart).Append(',').Append(newCount)
              .Append(" @@");

            for (var i = hunkStart; i <= hunkEnd; i++)
            {
                var op = ops[i];
                sb.Append('\n');
                switch (op.Op)
                {
                    case EditOpKind.Equal:
                        sb.Append(' ').Append(oldLines[op.OldIndex]);
                        break;
                    case EditOpKind.Delete:
                        sb.Append('-').Append(oldLines[op.OldIndex]);
                        break;
                    case EditOpKind.Insert:
                        sb.Append('+').Append(newLines[op.NewIndex]);
                        break;
                }
            }
        }

        return sb.ToString();
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        return normalized.Length == 0 ? [string.Empty] : normalized.Split('\n');
    }

    private static List<DiffOp> ComputeDiffOps(string[] oldLines, string[] newLines)
    {
        var prefix = 0;
        var minLen = Math.Min(oldLines.Length, newLines.Length);
        while (prefix < minLen && string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < minLen - prefix
               && string.Equals(oldLines[oldLines.Length - 1 - suffix], newLines[newLines.Length - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        var ops = new List<DiffOp>(oldLines.Length + newLines.Length);
        for (var i = 0; i < prefix; i++)
        {
            ops.Add(new DiffOp(EditOpKind.Equal, i, i));
        }

        var midOldStart = prefix;
        var midOldLen = oldLines.Length - prefix - suffix;
        var midNewStart = prefix;
        var midNewLen = newLines.Length - prefix - suffix;

        if (midOldLen > 0 || midNewLen > 0)
        {
            if (midOldLen == 0)
            {
                for (var j = 0; j < midNewLen; j++)
                {
                    ops.Add(new DiffOp(EditOpKind.Insert, -1, midNewStart + j));
                }
            }
            else if (midNewLen == 0)
            {
                for (var i = 0; i < midOldLen; i++)
                {
                    ops.Add(new DiffOp(EditOpKind.Delete, midOldStart + i, -1));
                }
            }
            else if ((long)midOldLen * midNewLen <= MaxDpCells)
            {
                AppendLcsMiddleOps(ops, oldLines, newLines, midOldStart, midOldLen, midNewStart, midNewLen);
            }
            else
            {
                for (var i = 0; i < midOldLen; i++)
                {
                    ops.Add(new DiffOp(EditOpKind.Delete, midOldStart + i, -1));
                }

                for (var j = 0; j < midNewLen; j++)
                {
                    ops.Add(new DiffOp(EditOpKind.Insert, -1, midNewStart + j));
                }
            }
        }

        for (var k = 0; k < suffix; k++)
        {
            ops.Add(new DiffOp(
                EditOpKind.Equal,
                oldLines.Length - suffix + k,
                newLines.Length - suffix + k));
        }

        return ops;
    }

    private static void AppendLcsMiddleOps(
        List<DiffOp> ops,
        string[] oldLines,
        string[] newLines,
        int oldStart,
        int oldLen,
        int newStart,
        int newLen)
    {
        var dp = new int[oldLen + 1, newLen + 1];
        for (var i = oldLen - 1; i >= 0; i--)
        {
            for (var j = newLen - 1; j >= 0; j--)
            {
                if (string.Equals(oldLines[oldStart + i], newLines[newStart + j], StringComparison.Ordinal))
                {
                    dp[i, j] = dp[i + 1, j + 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }
        }

        var oi = 0;
        var ni = 0;
        while (oi < oldLen && ni < newLen)
        {
            if (string.Equals(oldLines[oldStart + oi], newLines[newStart + ni], StringComparison.Ordinal))
            {
                ops.Add(new DiffOp(EditOpKind.Equal, oldStart + oi, newStart + ni));
                oi++;
                ni++;
            }
            else if (dp[oi + 1, ni] >= dp[oi, ni + 1])
            {
                ops.Add(new DiffOp(EditOpKind.Delete, oldStart + oi, -1));
                oi++;
            }
            else
            {
                ops.Add(new DiffOp(EditOpKind.Insert, -1, newStart + ni));
                ni++;
            }
        }

        while (oi < oldLen)
        {
            ops.Add(new DiffOp(EditOpKind.Delete, oldStart + oi, -1));
            oi++;
        }

        while (ni < newLen)
        {
            ops.Add(new DiffOp(EditOpKind.Insert, -1, newStart + ni));
            ni++;
        }
    }

    private static List<(int Start, int End)> GroupHunks(List<int> changedIndices, int totalOps, int context)
    {
        var hunks = new List<(int Start, int End)>();
        var firstChange = changedIndices[0];
        var lastChange = changedIndices[0];

        for (var i = 1; i < changedIndices.Count; i++)
        {
            var idx = changedIndices[i];
            if (idx - lastChange <= (context * 2) + 1)
            {
                lastChange = idx;
            }
            else
            {
                hunks.Add((
                    Math.Max(0, firstChange - context),
                    Math.Min(totalOps - 1, lastChange + context)));
                firstChange = idx;
                lastChange = idx;
            }
        }

        hunks.Add((
            Math.Max(0, firstChange - context),
            Math.Min(totalOps - 1, lastChange + context)));

        return hunks;
    }

    private enum EditOpKind
    {
        Equal,
        Delete,
        Insert
    }

    private readonly record struct DiffOp(EditOpKind Op, int OldIndex, int NewIndex);
}
