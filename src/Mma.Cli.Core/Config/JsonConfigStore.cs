using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Mma.Cli.Core.Config;

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<JsonConfigStore>? _logger;
    private readonly string _userConfigDir;

    public JsonConfigStore(ILogger<JsonConfigStore>? logger = null, string? userConfigDir = null)
    {
        _logger = logger;
        _userConfigDir = string.IsNullOrWhiteSpace(userConfigDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mma")
            : userConfigDir;
    }

    public string GetSetting(string solutionPath, string key, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return defaultValue;
        }

        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            var localConfigPath = Path.Combine(solutionPath, ".mma", "config.json");
            if (TryReadSetting(localConfigPath, key, out var localValue))
            {
                return localValue;
            }
        }

        var globalConfigPath = Path.Combine(_userConfigDir, "config.json");
        if (TryReadSetting(globalConfigPath, key, out var globalValue))
        {
            return globalValue;
        }

        return defaultValue;
    }

    private bool TryReadSetting(string configPath, string key, out string value)
    {
        value = string.Empty;
        if (!File.Exists(configPath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(configPath);
            using var doc = JsonDocument.Parse(stream, DocumentOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty(key, out var element))
            {
                return false;
            }

            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return false;
            }

            value = element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.ToString();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to read configuration key '{Key}' from '{ConfigPath}'. Using fallback.", key, configPath);
            return false;
        }
    }
}
