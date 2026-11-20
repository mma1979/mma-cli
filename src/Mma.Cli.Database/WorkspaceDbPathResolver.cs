namespace Mma.Cli.Database;

public static class WorkspaceDbPathResolver
{
    public static string Resolve(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var envDir = Environment.GetEnvironmentVariable("MMA_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            return Path.GetFullPath(Path.Combine(envDir.Trim(), "workspace.db"));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                userProfile = ".";
            }

            localAppData = Path.Combine(userProfile, ".local", "share");
        }

        return Path.Combine(localAppData, "mma-cli", "workspace.db");
    }
}
