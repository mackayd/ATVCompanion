using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Core.Config;
using Core;

namespace UI
{
    public partial class MainWindow
    {
        /// <summary>
        /// Call this from your constructor or Loaded handler to (re)bind UI state.
        /// Safe to call repeatedly.
        /// </summary>
        private async Task ApplyStateAsync()
        {
            await Task.Yield();
            try
            {
                var cfg = ConfigStore.Load();
                bool paired = cfg != null && !string.IsNullOrWhiteSpace(cfg.DeviceId) && !string.IsNullOrWhiteSpace(cfg.AuthKey);
                CreateTasksButton.IsEnabled = paired;
            }
            catch
            {
                // If anything goes wrong, keep the button disabled to be safe.
                CreateTasksButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// Helper to persist the configuration after successful pairing.
        /// Call this at the end of your pairing flow.
        /// </summary>
        private void SavePairedConfig(string ip, string mac, string deviceId, string authKey)
        {
            var cfg = new AppConfig
            {
                Ip = ip,
                Mac = mac,
                DeviceId = deviceId,
                AuthKey = authKey
            };
            ConfigStore.Save(cfg);
            _ = ApplyStateAsync();
        }
    }
}