using System;
using System.IO;
using System.Text.Json;

namespace Core.Config
{
    /// <summary>
    /// Static storage helper for reading/writing AppConfig to %ProgramData%\CompanDroid\config.json
    /// (falls back to legacy %ProgramData%\ATVCompanion\config.json when loading).
    /// </summary>
    public static class ConfigStore
    {
        public static readonly string ProgramDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CompanDroid");

        public static readonly string ConfigPath = Path.Combine(ProgramDataDir, "config.json");

        private static readonly string LegacyProgramDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ATVCompanion");

        private static readonly string LegacyConfigPath = Path.Combine(LegacyProgramDataDir, "config.json");

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
                var path = File.Exists(ConfigPath)
                    ? ConfigPath
                    : (File.Exists(LegacyConfigPath) ? LegacyConfigPath : null);

                if (path is null)
                {
                    return new AppConfig();
                }

                var json = File.ReadAllText(path);
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
