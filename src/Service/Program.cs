using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ATVCompanion.Core.Interfaces;
using ATVCompanion.Core.Philips;
using ATVCompanion.Core.Models;

namespace Service
{
    internal class AppSettings
    {
        public string? TvHost { get; set; }
        public int? TvPort { get; set; }
        public string? TvManufacturer { get; set; }
        public string? WakeMac { get; set; }
        public string? WakeBroadcast { get; set; }
        public int? WakePort { get; set; }
    }

    internal static class Program
    {
        private static ITvPlugin? _plugin;
        private static WakeHint? _wake;
        private static volatile bool _running = true;

        static async Task<int> Main(string[] args)
        {
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; _running = false; };

            var settings = LoadSettings();
            _plugin = CreatePlugin(settings);
            _wake = settings.WakeMac is not null
                ? new WakeHint(settings.WakeMac, settings.WakeBroadcast, settings.WakePort ?? 9)
                : null;

            // Subscribe to Windows events
            try
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                SystemEvents.SessionSwitch += OnSessionSwitch;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SystemEvents hookup failed: {ex.Message}");
            }

            Console.WriteLine("ATVCompanion Service started. Press Ctrl+C to exit.");
            while (_running)
            {
                await Task.Delay(250);
            }

            try
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            catch { /* ignore */ }

            // On exit, optionally send standby (best-effort)
            try { if (_plugin is not null) await _plugin.PowerOffAsync(CancellationToken.None); } catch { }

            return 0;
        }

        private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                _ = TryWakeAsync();
            }
        }

        private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
            {
                _ = TryWakeAsync();
            }
        }

        private static async Task TryWakeAsync()
        {
            try
            {
                if (_plugin is not null)
                {
                    await _plugin.WakeAsync(_wake!, CancellationToken.None);
                    Console.WriteLine($"Wake attempted at {DateTime.Now:T}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Wake failed: {ex.Message}");
            }
        }

        private static ITvPlugin CreatePlugin(AppSettings s)
        {
            // For now default to Philips/JointSPACE. We'll make this pluggable later.
            var host = s.TvHost ?? "192.168.1.100";
            var port = s.TvPort ?? 1925;
            return new PhilipsJointSpacePlugin(host, port);
        }

        private static AppSettings LoadSettings()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var cfg = JsonSerializer.Deserialize<AppSettings>(json, opts);
                    if (cfg is not null) return cfg;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse appsettings.json: {ex.Message}");
                }
            }
            return new AppSettings();
        }
    }
}
