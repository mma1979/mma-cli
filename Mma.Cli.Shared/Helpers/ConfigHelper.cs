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
                if (File.Exists(localConfigPath))
                {
                    var json = File.ReadAllText(localConfigPath);
                    var config = JObject.Parse(json);
                    if (config[key] != null)
                    {
                        return config[key].ToString();
                    }
                }

                // 2. Try Global Config
                var globalConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mma");
                var globalConfigPath = Path.Combine(globalConfigDir, "config.json");
                if (File.Exists(globalConfigPath))
                {
                    var json = File.ReadAllText(globalConfigPath);
                    var config = JObject.Parse(json);
                    if (config[key] != null)
                    {
                        return config[key].ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Output.Warning($"Failed to load configuration '{key}': {ex.Message}. Using default.");
            }

            return defaultValue;
        }
    }
}
