using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Net.Sockets;
using Core.Config; // AppConfig + ConfigStore

namespace UI
{
    public partial class MainWindow : Window
    {
        // ===== Logging helpers + UI lookups ===================================

        private TextBox? GetTextBox(string name)
        {
            try { return FindName(name) as TextBox; } catch { return null; }
        }

        private (string ip, string mac) GetIpMac()
        {
            var ip  = GetTextBox("IpBox")?.Text?.Trim()  ?? "";
            var mac = GetTextBox("MacBox")?.Text?.Trim() ?? "";
            return (ip, mac);
        }

        private static void ShowError(string message)
        {
            Log.Error(message);
            MessageBox.Show(message, "ATV Companion", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // ===== Wake-on-LAN =====================================================

        private static void SendMagicPacket(string mac, int port, string? broadcastIp)
        {
            if (string.IsNullOrWhiteSpace(mac))
                throw new ArgumentException("MAC address is required.", nameof(mac));

            var hex = mac.Replace("-", "").Replace(":", "").Trim();
            if (hex.Length != 12)
                throw new ArgumentException("Invalid MAC address format. Expected 6 bytes.", nameof(mac));

            var macBytes = new byte[6];
            for (int i = 0; i < 6; i++)
                macBytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            var packet = new byte[6 + (16 * 6)];
            for (int i = 0; i < 6; i++) packet[i] = 0xFF;
            for (int i = 0; i < 16; i++)
                Buffer.BlockCopy(macBytes, 0, packet, 6 + i * 6, 6);

            var bcast = string.IsNullOrWhiteSpace(broadcastIp) ? "255.255.255.255" : broadcastIp;
            using var client = new UdpClient();
            client.EnableBroadcast = true;
            client.Send(packet, packet.Length,
                new System.Net.IPEndPoint(System.Net.IPAddress.Parse(bcast), port));
        }

        // ===== Http helpers ====================================================

        private static HttpClientHandler NewV6HandlerWithIgnoredSsl()
        {
            return new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = true,
                ServerCertificateCustomValidationCallback = (_, __, ___, ____) => true
            };
        }

        private static HttpClient NewHttpV6(string baseHttps)
        {
            var handler = NewV6HandlerWithIgnoredSsl();
            var http = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseHttps),
                Timeout = TimeSpan.FromSeconds(10)
            };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return http;
        }

        // ===== Philips JointSPACE v6 pairing ===================================

        // Fixed TP-Vision secret (base64) used to HMAC-SHA1(timestamp+pin)
        private const string SecretB64 =
            "ZmVay1EQVFOaZhwQ4Kv81ypLAZNczV9sG4KkseXWn1NEk6cXmPKO/MCa9sryslvLCFMnNe4Z4CPXzToowvhHvA==";

        private sealed class PairReqDevice
        {
            public string device_name { get; set; } = "ATVCompanion";
            public string device_os   { get; set; } = "Windows";
            public string app_name    { get; set; } = "ATVCompanion";
            public string type        { get; set; } = "native";
            public string app_id      { get; set; } = "app.id";
            public string id          { get; set; } = "";
        }

        private sealed class PairRequestBody
        {
            public string[] scope { get; set; } = new[] { "read", "write", "control" };
            public PairReqDevice device { get; set; } = new PairReqDevice();
        }

     

       private static async Task<(bool ok, string? timestamp, string? authKey, string? err)>
    PhilipsPairRequestAsync(string ip, string deviceId, CancellationToken ct = default)
{
    try
    {
        using var http = NewHttpV6($"https://{ip}:1926/");

        var body = new PairRequestBody
        {
            device = new PairReqDevice { id = deviceId }
        };

        var json = JsonSerializer.Serialize(body);
        var res  = await http.PostAsync("6/pair/request",
                     new StringContent(json, Encoding.UTF8, "application/json"), ct);
        var txt  = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            return (false, null, null, $"Pair request rejected: {(int)res.StatusCode} {res.ReasonPhrase} - {TrimHtml(txt)}");

        // Robust parse: timestamp may be number or string depending on TV firmware
        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;

        if (!root.TryGetProperty("timestamp", out var tsEl) ||
            !root.TryGetProperty("auth_key",  out var akEl))
        {
            return (false, null, null, "Unexpected /pair/request response: " + txt);
        }

        string? ts = tsEl.ValueKind switch
        {
            JsonValueKind.String => tsEl.GetString(),
            JsonValueKind.Number => tsEl.GetRawText(), // keep numeric as string
            _ => null
        };

        string? authKey = akEl.ValueKind switch
        {
            JsonValueKind.String => akEl.GetString(),
            JsonValueKind.Number => akEl.GetRawText(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(ts) || string.IsNullOrWhiteSpace(authKey))
            return (false, null, null, "Invalid /pair/request values: " + txt);

        return (true, ts, authKey, null);
    }
    catch (Exception ex)
    {
        return (false, null, null, "Pair request failed: " + ex.Message);
    }
}

        private static string ComputeV6Signature(string timestamp, string pin)
        {
            var secret = Convert.FromBase64String(SecretB64);
            var toSign = Encoding.UTF8.GetBytes(timestamp + pin);
            using var hmac = new HMACSHA1(secret);
            var hash = hmac.ComputeHash(toSign);
            return Convert.ToBase64String(hash);
        }

        private sealed class GrantAuth
        {
            public string auth_AppId     { get; set; } = "1";
            public string pin            { get; set; } = "";
            public string auth_timestamp { get; set; } = "";
            public string auth_signature { get; set; } = "";
        }

        private sealed class GrantBody
        {
            public GrantAuth auth { get; set; } = new GrantAuth();
            public PairReqDevice device { get; set; } = new PairReqDevice();
        }

        private static async Task<(bool ok, string? err)> PhilipsPairGrantAsync(
            string ip, string deviceId, string pin, string timestamp, string authKey, CancellationToken ct = default)
        {
            try
            {
                var handler = NewV6HandlerWithIgnoredSsl();
                // Digest: username=deviceId, password=auth_key returned by /pair/request
                handler.Credentials = new NetworkCredential(deviceId, authKey);

                using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://{ip}:1926/") };
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

                var body = new GrantBody
                {
                    auth = new GrantAuth
                    {
                        pin = pin,
                        auth_timestamp = timestamp,
                        auth_signature = ComputeV6Signature(timestamp, pin)
                    },
                    device = new PairReqDevice { id = deviceId }
                };

                var json = JsonSerializer.Serialize(body);
                var res  = await http.PostAsync("6/pair/grant",
                             new StringContent(json, Encoding.UTF8, "application/json"), ct);
                var txt  = await res.Content.ReadAsStringAsync(ct);

                if (res.IsSuccessStatusCode) return (true, null);
                return (false, $"Grant failed: {(int)res.StatusCode} {res.ReasonPhrase} - {TrimHtml(txt)}");
            }
            catch (Exception ex)
            {
                return (false, "Grant failed: " + ex.Message);
            }
        }

        // ===== Small helpers ===================================================

        private static string TrimHtml(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            return s.Replace("\r", "").Replace("\n", " ").Trim();
        }

        private static string GenerateDeviceId()
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var rng  = new Random();
            var buf = new char[16];
            for (int i = 0; i < buf.Length; i++) buf[i] = alphabet[rng.Next(alphabet.Length)];
            return new string(buf);
        }

        private static string? PromptForPinFromUser(Window owner)
        {
            var w = new Window
            {
                Title = "Enter TV PIN",
                Width = 260,
                Height = 130,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0, Top = 0,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                Owner = owner
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock { Text = "PIN shown on TV:", Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(lbl, 0);
            grid.Children.Add(lbl);

            var tb = new TextBox { Margin = new Thickness(0, 0, 0, 10), MaxLength = 8 };
            Grid.SetRow(tb, 1);
            grid.Children.Add(tb);

            var panel  = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok     = new Button { Content = "OK", Width = 60, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 60, IsCancel = true };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 2);
            grid.Children.Add(panel);

            string? pin = null;
            ok.Click += (_, __) => { pin = tb.Text?.Trim(); w.DialogResult = true; };
            cancel.Click += (_, __) => { pin = null; w.DialogResult = false; };

            w.Content = grid;
            w.Loaded += (_, __) => { tb.Focus(); Keyboard.Focus(tb); };
            w.ShowDialog();

            return pin;
        }

        // ===== Button handlers =================================================

        private async void PairButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (ip, mac) = GetIpMac();
                if (string.IsNullOrWhiteSpace(ip))
                {
                    ShowError("Please enter the TV IP address.");
                    return;
                }

                var cfg = ConfigStore.Load() ?? new AppConfig();
                if (string.IsNullOrWhiteSpace(cfg.DeviceId))
                    cfg.DeviceId = GenerateDeviceId();

                Log.Info($"Starting pairing with {ip} ...");

                // 1) request -> timestamp + auth_key
                var (okReq, ts, authKey, errReq) = await PhilipsPairRequestAsync(ip, cfg.DeviceId!);
                if (!okReq || string.IsNullOrWhiteSpace(ts) || string.IsNullOrWhiteSpace(authKey))
                {
                    ShowError(errReq ?? "Pair request failed.");
                    return;
                }

                // 2) prompt for PIN + grant using Digest(user=deviceId, pass=auth_key)
                var pin = PromptForPinFromUser(this);
                if (string.IsNullOrWhiteSpace(pin))
                {
                    Log.Info("Pairing cancelled.");
                    return;
                }

                var (okGrant, errGrant) = await PhilipsPairGrantAsync(ip, cfg.DeviceId!, pin!, ts!, authKey!);
                if (!okGrant)
                {
                    ShowError("The TV did not accept the pairing request. Is JointSpace enabled?\n\n" + (errGrant ?? "Unknown error"));
                    return;
                }

                // Success: save creds
                cfg.Ip = ip;
                if (!string.IsNullOrWhiteSpace(mac)) cfg.Mac = mac;
                cfg.AuthKey = authKey!;
                ConfigStore.Save(cfg);

                Log.Info("Paired successfully. Credentials saved.");
                MessageBox.Show("Paired successfully.\nCredentials saved for future operations.",
                    "ATV Companion", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError("Pair failed: " + ex.Message);
            }
        }

        private async void WakeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigStore.Load() ?? new AppConfig();
                var (ip, mac) = GetIpMac();

                var macToUse = !string.IsNullOrWhiteSpace(mac) ? mac : cfg.Mac;
                if (string.IsNullOrWhiteSpace(macToUse))
                {
                    ShowError("Please enter a MAC address for Wake-on-LAN.");
                    return;
                }

                var bcast = !string.IsNullOrWhiteSpace(cfg.Ip) ? cfg.Ip : (!string.IsNullOrWhiteSpace(ip) ? ip : "255.255.255.255");
                Log.Info($"Sending Wake-on-LAN to {macToUse} ...");
                SendMagicPacket(macToUse, 9, bcast);
                Log.Info("Wake packet sent.");

                await Task.Delay(1500);
                Log.Info("Wake attempt complete.");
            }
            catch (Exception ex)
            {
                ShowError("Wake failed: " + ex.Message);
            }
        }

        private async void StandbyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigStore.Load();
                if (cfg == null || string.IsNullOrWhiteSpace(cfg.Ip) ||
                    string.IsNullOrWhiteSpace(cfg.DeviceId) || string.IsNullOrWhiteSpace(cfg.AuthKey))
                {
                    ShowError("TV not paired yet. Use Pair first.");
                    return;
                }

                var handler = NewV6HandlerWithIgnoredSsl();
                handler.Credentials = new NetworkCredential(cfg.DeviceId!, cfg.AuthKey!); // Digest
                using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://{cfg.Ip}:1926/") };
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

                var body = JsonSerializer.Serialize(new { key = "Standby" });
                var res  = await http.PostAsync("6/input/key", new StringContent(body, Encoding.UTF8, "application/json"));
                var txt  = await res.Content.ReadAsStringAsync();

                if (res.IsSuccessStatusCode)
                    Log.Info("TV put into standby.");
                else
                    ShowError($"Standby failed: {(int)res.StatusCode} {res.ReasonPhrase}\n{TrimHtml(txt)}");
            }
            catch (Exception ex)
            {
                ShowError("Standby failed: " + ex.Message);
            }
        }
    }
}
