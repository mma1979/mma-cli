using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace Mma.Cli.Shared.Helpers
{
    public static class ConfigHelper
    {
        public static string GetSetting(string solutionPath, string key, string defaultValue)
        {
            try
            {
                // 1. Try Local Config
                var localConfigPath = Path.Combine(solutionPath, ".mma", "config.json");
                if (TryReadSetting(localConfigPath, key, out var localValue))
                {
                    return localValue;
                }

                // 2. Try Global Config
                var globalConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mma");
                var globalConfigPath = Path.Combine(globalConfigDir, "config.json");
                if (TryReadSetting(globalConfigPath, key, out var globalValue))
                {
                    return globalValue;
                }
            }
            catch (Exception ex)
            {
                Output.Warning($"Failed to load configuration '{key}': {ex.Message}. Using default.");
            }

            return defaultValue;
        }

        private static bool TryReadSetting(string configPath, string key, out string value)
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JObject.Parse(json);
                var token = config[key];
                if (token is not null)
                {
                    value = token.ToString();
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }
    }
}
