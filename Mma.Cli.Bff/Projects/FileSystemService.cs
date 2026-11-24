using Mma.Cli.Shared.Exceptions;

namespace Mma.Cli.Bff.Projects;

public sealed class FileSystemService
{
    public BrowseFsResponse Browse(string? requestedPath)
    {
        var target = string.IsNullOrWhiteSpace(requestedPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : requestedPath.Trim();

        if (string.IsNullOrWhiteSpace(target))
        {
            target = Directory.GetCurrentDirectory();
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(target);
        }
        catch (Exception ex)
        {
            throw new MmaCliException("invalid_path", $"The path '{requestedPath}' is invalid.", exitCode: 2, ex);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new MmaCliException("path_not_found", $"Directory '{fullPath}' does not exist.", exitCode: 66);
        }

        var directories = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(fullPath))
            {
                try
                {
                    var attrs = File.GetAttributes(dir);
                    if ((attrs & FileAttributes.Hidden) != 0 || (attrs & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    directories.Add(Path.GetFileName(dir));
                }
                catch
                {
                    // Skip unreadable entries.
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new MmaCliException("access_denied", $"Access denied to '{fullPath}'.", exitCode: 1, ex);
        }

        directories.Sort(StringComparer.OrdinalIgnoreCase);

        var hasSln = Directory.EnumerateFiles(fullPath, "*.sln", SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFiles(fullPath, "*.slnx", SearchOption.TopDirectoryOnly).Any();
        var isMmaSolution = Directory.Exists(Path.Combine(fullPath, ".mma"));
        var parentPath = Directory.GetParent(fullPath)?.FullName;

        return new BrowseFsResponse(fullPath, parentPath, directories, hasSln, isMmaSolution);
    }

    public MkdirResponse CreateDirectory(MkdirRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ParentPath) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new MmaCliException("invalid_request", "Both parentPath and name are required.", exitCode: 2);
        }

        var name = request.Name.Trim();
        if (name is "." or ".." ||
            name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new MmaCliException("invalid_folder_name", $"Folder name '{name}' contains invalid characters.", exitCode: 2);
        }

        var parentFull = Path.GetFullPath(request.ParentPath.Trim());
        if (!Directory.Exists(parentFull))
        {
            throw new MmaCliException("path_not_found", $"Parent directory '{parentFull}' does not exist.", exitCode: 66);
        }

        var createdPath = Path.Combine(parentFull, name);
        Directory.CreateDirectory(createdPath);
        return new MkdirResponse(createdPath);
    }
}
