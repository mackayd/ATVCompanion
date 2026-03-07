using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.IO;

namespace CLI
{
    internal sealed class AppConfig
    {
        public string? Ip { get; set; }
        public string? Mac { get; set; }
        public string? DeviceId { get; set; }  // username for Digest
        public string? AuthKey { get; set; }   // password for Digest (Philips) / PSK (Sony)

        [JsonPropertyName("manufacturer")]
        public string? Manufacturer { get; set; }
    }

    internal static class Program
    {
        private static readonly JsonSerializerOptions ConfigJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly string[] ConfigPaths =
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CompanDroid",
                "Config.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CompanDroid",
                "AppConfig.json"),
            // Backward compatibility with older app name/location.
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ATVCompanion",
                "Config.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ATVCompanion",
                "AppConfig.json"),
            Path.Combine(AppContext.BaseDirectory, "Config.json"),
            Path.Combine(AppContext.BaseDirectory, "AppConfig.json")
        };

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
@"CompanDroid CLI

Usage:
  CLI.exe wake [--mac <MAC>] [--bcast <IP>] [--port <PORT>]
  CLI.exe standby [--brand <philips|sony>] [--ip <IP>] [--user <DEVICE_ID>] [--pass <AUTH_KEY>] [--psk <PSK>]

Notes:
  - Missing flags are loaded from %ProgramData%\CompanDroid\Config.json (legacy ATVCompanion paths are also accepted).
  - Philips standby posts https://<ip>:1926/6/input/key { ""key"": ""Standby"" } with Digest auth.
  - Sony standby posts JSON-RPC setPowerStatus to http://<ip>/sony/system using PSK.
");
        }

        static string? Flag(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        static string NormalizeBrand(string? brand)
        {
            if (string.Equals(brand, "sony", StringComparison.OrdinalIgnoreCase)) return "Sony";
            return "Philips";
        }

        static AppConfig? LoadConfig()
        {
            foreach (var path in ConfigPaths)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    // Accept snake_case keys too (device_id/auth_key/manufacturer)
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, ConfigJsonOptions);
                    if (cfg != null)
                    {
                        if ((cfg.DeviceId == null || cfg.AuthKey == null || cfg.Manufacturer == null) &&
                            json.IndexOf("device_id", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("device_id", out var d)) cfg.DeviceId = d.GetString();
                            if (root.TryGetProperty("auth_key", out var a)) cfg.AuthKey = a.GetString();
                            if (root.TryGetProperty("ip", out var ip)) cfg.Ip = ip.GetString();
                            if (root.TryGetProperty("mac", out var mac)) cfg.Mac = mac.GetString();
                            if (root.TryGetProperty("manufacturer", out var m)) cfg.Manufacturer = m.GetString();
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

        static Task<int> RunWake(string[] args)
        {
            var cfg = LoadConfig();
            var mac = Flag(args, "--mac") ?? cfg?.Mac;
            if (string.IsNullOrWhiteSpace(mac))
                return Task.FromResult(Fail("Missing --mac <MAC> and no saved MAC in config."));

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
                return Task.FromResult(Fail("Invalid --port value."));

            SendMagicPacket(mac!, bcast!, port);
            Console.WriteLine("Wake signal sent.");
            return Task.FromResult(0);
        }

        static void SendMagicPacket(string mac, string broadcast, int port)
        {
            static byte[] ParseMac(string s)
            {
                Span<char> clean = stackalloc char[12];
                var cleanLen = 0;
                foreach (var ch in s)
                {
                    if (ch is ':' or '-' or '.' || char.IsWhiteSpace(ch)) continue;
                    if (cleanLen >= clean.Length) throw new ArgumentException("MAC must be 12 hex digits.");
                    clean[cleanLen++] = ch;
                }

                if (cleanLen != 12) throw new ArgumentException("MAC must be 12 hex digits.");

                var bytes = new byte[6];
                for (int i = 0; i < 6; i++)
                {
                    if (!byte.TryParse(clean.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                        throw new ArgumentException("MAC contains invalid hex characters.");
                }
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
            var brand = NormalizeBrand(Flag(args, "--brand") ?? cfg?.Manufacturer);
            var ip = Flag(args, "--ip") ?? cfg?.Ip;

            if (string.IsNullOrWhiteSpace(ip))
                return Fail("Missing --ip <IP> and no saved IP in config.");

            if (brand == "Sony")
            {
                var psk = Flag(args, "--psk") ?? Flag(args, "--pass") ?? cfg?.AuthKey;
                if (string.IsNullOrWhiteSpace(psk))
                    return Fail("Missing Sony PSK. Provide --psk (or --pass) or pair Sony in the UI first.");

                var ok = await SonyPowerOffAsync(ip!, psk!);
                if (!ok)
                    return Fail("Sony standby failed. Check IP control settings, PSK, and network reachability.");

                Console.WriteLine("Standby sent.");
                return 0;
            }

            // Philips (default)
            var user = Flag(args, "--user") ?? cfg?.DeviceId;
            var pass = Flag(args, "--pass") ?? cfg?.AuthKey;

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                return Fail("Missing Philips credentials. Provide --user/--pass or pair in the UI first.");

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

        static async Task<bool> SonyPowerOffAsync(string ip, string psk)
        {
            try
            {
                using var http = new HttpClient
                {
                    BaseAddress = new Uri($"http://{ip}/"),
                    Timeout = TimeSpan.FromSeconds(8)
                };

                var req = new HttpRequestMessage(HttpMethod.Post, "sony/system")
                {
                    Content = new StringContent(
                        "{\"method\":\"setPowerStatus\",\"id\":1,\"params\":[{\"status\":false}],\"version\":\"1.0\"}",
                        Encoding.UTF8,
                        "application/json")
                };

                req.Headers.Add("X-Auth-PSK", psk);
                using var resp = await http.SendAsync(req);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        static int Fail(string msg)
        {
            Console.Error.WriteLine(msg);
            Console.Error.WriteLine("Tip: run with --help for usage.");
            return 1;
        }
    }
}
