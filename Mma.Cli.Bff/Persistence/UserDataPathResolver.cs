using Microsoft.Data.Sqlite;

namespace Mma.Cli.Bff.Persistence;

public static class UserDataPathResolver
{
    public const string DataDirEnvVar = "MMA_CLI_DATA_DIR";
    public const string DatabaseFileName = "mma_cli.db";

    public static string ResolveDatabasePath(IConfiguration configuration)
    {
        var configuredDir = configuration[DataDirEnvVar]
            ?? Environment.GetEnvironmentVariable(DataDirEnvVar);

        if (!string.IsNullOrWhiteSpace(configuredDir))
        {
            var dir = Path.GetFullPath(configuredDir.Trim());
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, DatabaseFileName);
        }

        var configuredConnString = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(configuredConnString))
        {
            var builder = new SqliteConnectionStringBuilder(configuredConnString);
            if (!string.IsNullOrWhiteSpace(builder.DataSource))
            {
                var fullPath = Path.GetFullPath(builder.DataSource);
                var parent = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }
                return fullPath;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        }

        var dataDir = Path.Combine(localAppData, "mma-cli");
        Directory.CreateDirectory(dataDir);
        return Path.Combine(dataDir, DatabaseFileName);
    }

    public static string BuildConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
}
