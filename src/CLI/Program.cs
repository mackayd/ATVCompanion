using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace CLI
{
    internal sealed class AppConfig
    {
        public string? Ip { get; set; }
        public string? Mac { get; set; }
        public string? DeviceId { get; set; }  // username for Digest
        public string? AuthKey { get; set; }   // password for Digest
    }

    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length == 0 || HasHelp(args))
            {
                PrintHelp();
                return 0;
            }

            var verb = args[0].ToLowerInvariant();
            var rest = args.Length > 1 ? args[1..] : Array.Empty<string>();

            try
            {
                return verb switch
                {
                    "wake"    => await RunWake(rest),
                    "standby" => await RunStandby(rest),
                    _         => Fail($"Unknown command: {verb}")
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        static bool HasHelp(string[] a) =>
            Array.Exists(a, s => s is "-h" or "--help" or "/?");

        static void PrintHelp()
        {
            Console.WriteLine(
@"ATV Companion CLI

Usage:
  CLI.exe wake [--mac <MAC>] [--bcast <IP>] [--port <PORT>]
  CLI.exe standby [--ip <IP>] [--user <DEVICE_ID>] [--pass <AUTH_KEY>]

Notes:
  - Missing flags are loaded from %ProgramData%\ATVCompanion\Config.json (AppConfig.json is also accepted).
  - 'standby' posts https://<ip>:1926/6/input/key { ""key"": ""Standby"" } with Digest auth.
");
        }

        static string? Flag(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        static string ProgramDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ATVCompanion");

        static string[] CandidateConfigPaths()
        {
            // Primary: Config.json (what the UI writes)
            // Fallbacks: AppConfig.json, plus local folder for dev runs
            var list = new[]
            {
                Path.Combine(ProgramDataDir, "Config.json"),
                Path.Combine(ProgramDataDir, "AppConfig.json"),
                Path.Combine(AppContext.BaseDirectory, "Config.json"),
                Path.Combine(AppContext.BaseDirectory, "AppConfig.json")
            };
            return list;
        }

        static AppConfig? LoadConfig()
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            foreach (var path in CandidateConfigPaths())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    // Accept snake_case keys too (device_id/auth_key) via case-insensitive matching
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, opts);
                    if (cfg != null)
                    {
                        // Also try to extract snake_case manually if standard props are empty.
                        // (Cheap fallback for very old files)
                        if ((cfg.DeviceId == null || cfg.AuthKey == null) &&
                            json.IndexOf("device_id", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("device_id", out var d)) cfg.DeviceId = d.GetString();
                            if (root.TryGetProperty("auth_key", out var a)) cfg.AuthKey = a.GetString();
                            if (root.TryGetProperty("ip", out var ip)) cfg.Ip = ip.GetString();
                            if (root.TryGetProperty("mac", out var mac)) cfg.Mac = mac.GetString();
                        }
                        return cfg;
                    }
                }
                catch
                {
                    // ignore and try next candidate
                }
            }
            return null;
        }

        static async Task<int> RunWake(string[] args)
        {
            var cfg = LoadConfig();
            var mac = Flag(args, "--mac") ?? cfg?.Mac;
            if (string.IsNullOrWhiteSpace(mac))
                return Fail("Missing --mac <MAC> and no saved MAC in config.");

            var bcast = Flag(args, "--bcast");
            if (string.IsNullOrWhiteSpace(bcast))
            {
                var ip = cfg?.Ip;
                if (!string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip, out var ipAddr))
                {
                    var bytes = ipAddr.GetAddressBytes();
                    if (bytes.Length == 4) { bytes[3] = 255; bcast = new IPAddress(bytes).ToString(); }
                }
                bcast ??= "255.255.255.255";
            }

            var portStr = Flag(args, "--port");
            int port = 9;
            if (!string.IsNullOrWhiteSpace(portStr) && !int.TryParse(portStr, out port))
                return Fail("Invalid --port value.");

            SendMagicPacket(mac!, bcast!, port);
            Console.WriteLine("Wake signal sent.");
            return 0;
        }

        static void SendMagicPacket(string mac, string broadcast, int port)
        {
            static byte[] ParseMac(string s)
            {
                var clean = s.Replace(":", "").Replace("-", "").Replace(".", "").Trim();
                if (clean.Length != 12) throw new ArgumentException("MAC must be 12 hex digits.");
                var bytes = new byte[6];
                for (int i = 0; i < 6; i++)
                    bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
                return bytes;
            }

            var macBytes = ParseMac(mac);
            var packet = new byte[6 + 16 * 6];
            for (int i = 0; i < 6; i++) packet[i] = 0xFF;
            for (int i = 0; i < 16; i++)
                Buffer.BlockCopy(macBytes, 0, packet, 6 + i * 6, 6);

            using var client = new UdpClient();
            client.EnableBroadcast = true;
            client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse(broadcast), port));
        }

        static async Task<int> RunStandby(string[] args)
        {
            var cfg = LoadConfig();

            var ip   = Flag(args, "--ip")   ?? cfg?.Ip;
            var user = Flag(args, "--user") ?? cfg?.DeviceId;
            var pass = Flag(args, "--pass") ?? cfg?.AuthKey;

            if (string.IsNullOrWhiteSpace(ip))
                return Fail("Missing --ip <IP> and no saved IP in config.");
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                return Fail("Missing credentials. Provide --user/--pass or pair in the UI first.");

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, __, ___, ____) => true
            };

            var cache = new CredentialCache();
            cache.Add(new Uri($"https://{ip}:1926/"), "Digest", new NetworkCredential(user, pass));
            handler.Credentials = cache;
            handler.PreAuthenticate = true;

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };

            var uri = $"https://{ip}:1926/6/input/key";
            var content = new StringContent(JsonSerializer.Serialize(new { key = "Standby" }), Encoding.UTF8, "application/json");

            using var resp = await http.PostAsync(uri, content);
            if (!resp.IsSuccessStatusCode)
            {
                var raw = await resp.Content.ReadAsStringAsync();
                return Fail($"Standby failed: {(int)resp.StatusCode} {resp.ReasonPhrase} - {raw}");
            }

            Console.WriteLine("Standby sent.");
            return 0;
        }

        static int Fail(string msg)
        {
            Console.Error.WriteLine(msg);
            Console.Error.WriteLine("Tip: run with --help for usage.");
            return 1;
        }
    }
}
