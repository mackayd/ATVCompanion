using System;
using System.IO;
using System.Text.Json;

namespace Core.Config
{
    /// <summary>
    /// Static storage helper for reading/writing AppConfig to %ProgramData%\ATVCompanion\config.json
    /// </summary>
    public static class ConfigStore
    {
        public static readonly string ProgramDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ATVCompanion");

        public static readonly string ConfigPath = Path.Combine(ProgramDataDir, "config.json");

        public static void Save(AppConfig config)
        {
            Directory.CreateDirectory(ProgramDataDir);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return new AppConfig();
                }

                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                return cfg ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }
    }
}