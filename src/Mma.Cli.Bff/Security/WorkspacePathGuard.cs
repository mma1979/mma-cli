using Microsoft.Data.Sqlite;
using Mma.Cli.Bff.Services;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Bff.Security;

public sealed class WorkspacePathGuard
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly IReadOnlyList<string> _allowedRoots;

    public WorkspacePathGuard(BffSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalized = new List<string>();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);

        foreach (var raw in options.AllowedRoots)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.IndexOf('\0') >= 0)
            {
                continue;
            }

            try
            {
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw.Trim()));
                if (seen.Add(full))
                {
                    normalized.Add(full);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Ignore invalid configured root entries and fall back to user profile if empty.
            }
        }

        if (normalized.Count == 0)
        {
            var fallback = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            normalized.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrWhiteSpace(fallback) ? "." : fallback)));
        }

        _allowedRoots = normalized;
    }

    public IReadOnlyList<string> AllowedRoots => _allowedRoots;

    public OperationResult<string> ValidateDirectoryPath(string? candidatePath)
    {
        var resolved = ValidatePathWithinRoots(candidatePath, out var errorCode);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResult<string>.Failure(
                errorCode ?? BffErrorCodes.PATH_OUTSIDE_ALLOWLIST,
                resolved.Error?.Message ?? "Path is outside the allowed workspace roots.");
        }

        return resolved;
    }

    public OperationResult<string> ValidatePathWithinRoots(string? candidatePath, out string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || candidatePath.IndexOf('\0') >= 0)
        {
            errorCode = ErrorCodes.VALIDATION_ERROR;
            return OperationResult<string>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "Path cannot be empty or contain null characters.");
        }

        string canonical;
        try
        {
            canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errorCode = ErrorCodes.VALIDATION_ERROR;
            return OperationResult<string>.Failure(ErrorCodes.VALIDATION_ERROR, $"Invalid path '{candidatePath}': {ex.Message}");
        }

        var matchedRoot = FindContainingRoot(canonical);
        if (matchedRoot is null)
        {
            errorCode = BffErrorCodes.PATH_OUTSIDE_ALLOWLIST;
            return OperationResult<string>.Failure(
                BffErrorCodes.PATH_OUTSIDE_ALLOWLIST,
                $"Path '{canonical}' is outside configured allowed workspace roots.");
        }

        if (!VerifyNoSymlinkEscape(canonical, matchedRoot, out var escapedTarget))
        {
            errorCode = BffErrorCodes.PATH_OUTSIDE_ALLOWLIST;
            return OperationResult<string>.Failure(
                BffErrorCodes.PATH_OUTSIDE_ALLOWLIST,
                $"Path '{canonical}' resolves via symbolic link outside allowed workspace roots ('{escapedTarget}').");
        }

        errorCode = null;
        return OperationResult<string>.Success(canonical);
    }

    public OperationResult ValidateSqlitePathIfLocal(string? serverOrPath, string? connectionString)
    {
        string? dataSource = null;

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var builder = new SqliteConnectionStringBuilder(connectionString);
                if (builder.Mode == SqliteOpenMode.Memory && string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult.Success();
                }

                dataSource = builder.DataSource;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                return OperationResult.Failure(ErrorCodes.VALIDATION_ERROR, "Invalid SQLite connection string.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(serverOrPath))
        {
            dataSource = serverOrPath.Trim();
        }

        if (string.IsNullOrWhiteSpace(dataSource)
            || string.Equals(dataSource.Trim(), ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Success();
        }

        var pathCheck = ValidatePathWithinRoots(dataSource, out var errorCode);
        if (!pathCheck.IsSuccess)
        {
            return OperationResult.Failure(
                errorCode ?? BffErrorCodes.PATH_OUTSIDE_ALLOWLIST,
                pathCheck.Error?.Message ?? "SQLite database path is outside configured allowed workspace roots.");
        }

        return OperationResult.Success();
    }

    private string? FindContainingRoot(string canonicalPath)
    {
        foreach (var root in _allowedRoots)
        {
            if (IsPathWithinRoot(canonicalPath, root))
            {
                return root;
            }
        }

        return null;
    }

    private static bool IsPathWithinRoot(string canonicalPath, string root)
    {
        if (string.Equals(canonicalPath, root, PathComparison))
        {
            return true;
        }

        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return canonicalPath.StartsWith(rootPrefix, PathComparison);
    }

    private bool VerifyNoSymlinkEscape(string canonicalPath, string matchedRoot, out string? escapedTarget)
    {
        escapedTarget = null;
        var current = canonicalPath;

        while (!string.IsNullOrEmpty(current) && current.Length > matchedRoot.Length)
        {
            try
            {
                var dirInfo = new DirectoryInfo(current);
                var fileInfo = new FileInfo(current);
                var linkTarget = dirInfo.LinkTarget ?? fileInfo.LinkTarget;

                if (linkTarget is not null)
                {
                    var resolvedInfo = dirInfo.Exists
                        ? dirInfo.ResolveLinkTarget(returnFinalTarget: true)
                        : fileInfo.Exists
                            ? fileInfo.ResolveLinkTarget(returnFinalTarget: true)
                            : dirInfo.ResolveLinkTarget(returnFinalTarget: true) ?? fileInfo.ResolveLinkTarget(returnFinalTarget: true);

                    var rawTargetPath = resolvedInfo?.FullName
                        ?? Path.GetFullPath(linkTarget, Path.GetDirectoryName(current) ?? matchedRoot);
                    var normalizedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawTargetPath));

                    if (FindContainingRoot(normalizedTarget) is null)
                    {
                        escapedTarget = normalizedTarget;
                        return false;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                escapedTarget = current;
                return false;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, PathComparison))
            {
                break;
            }

            current = Path.TrimEndingDirectorySeparator(parent);
        }

        return true;
    }
}
