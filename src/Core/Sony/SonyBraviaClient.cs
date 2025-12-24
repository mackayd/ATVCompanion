using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Sony
{
    internal sealed class SonyBraviaClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _host;
        private readonly int _port;
        private readonly string? _psk; // X-Auth-PSK value

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public SonyBraviaClient(string host, int port = 80, string? psk = null, HttpMessageHandler? handler = null)
        {
            _host = host;
            _port = port;
            _psk  = psk;

            _http = handler is null ? new HttpClient() : new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(5);
            if (!string.IsNullOrWhiteSpace(psk))
            {
                _http.DefaultRequestHeaders.Remove("X-Auth-PSK");
                _http.DefaultRequestHeaders.Add("X-Auth-PSK", psk);
            }
        }

        public void Dispose() => _http.Dispose();

        private string BaseUrl(string path) => $"http://{_host}:{_port}{path}";

        private static StringContent JsonBody(object body)
        {
            var json = JsonSerializer.Serialize(body, _json);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private async Task<JsonDocument?> PostJsonAsync(string path, object payload, CancellationToken ct)
        {
            using var resp = await _http.PostAsync(BaseUrl(path), JsonBody(payload), ct).ConfigureAwait(false);
            var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"{(int)resp.StatusCode} {resp.ReasonPhrase} - {content}");

            if (string.IsNullOrWhiteSpace(content))
                return null;

            return JsonDocument.Parse(content);
        }

        // Basic probe: ask power status
        public async Task<bool> ProbeAsync(CancellationToken ct)
        {
            try
            {
                using var doc = await PostJsonAsync("/sony/system",
                    new
                    {
                        method  = "getPowerStatus",
                        @params = Array.Empty<object>(),
                        id      = 1,
                        version = "1.0"
                    }, ct);

                return doc is not null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetPowerAsync(CancellationToken ct)
        {
            using var doc = await PostJsonAsync("/sony/system",
                new
                {
                    method  = "getPowerStatus",
                    @params = Array.Empty<object>(),
                    id      = 1,
                    version = "1.0"
                }, ct);

            // Expected: { "result": [ { "status": "active"/"standby" } ], ... }
            if (doc?.RootElement.TryGetProperty("result", out var result) == true &&
                result.ValueKind == JsonValueKind.Array &&
                result.GetArrayLength() > 0)
            {
                var obj = result[0];
                if (obj.TryGetProperty("status", out var status))
                {
                    return status.GetString() ?? "unknown";
                }
            }

            return "unknown";
        }

        public async Task PowerOffAsync(CancellationToken ct)
        {
            // JSON-RPC to set power status false
            await PostJsonAsync("/sony/system",
                new
                {
                    method  = "setPowerStatus",
                    @params = new object[]
                    {
                        new { status = false }
                    },
                    id      = 1,
                    version = "1.0"
                }, ct);
        }

        public async Task SendKeyAsync(string key, CancellationToken ct)
        {
            // Map a few friendly names to JSON-RPC or IRCC if needed later
            // For now, we handle Standby via setPowerStatus false, others throw.
            if (string.Equals(key, "Standby", StringComparison.OrdinalIgnoreCase))
            {
                await PowerOffAsync(ct);
                return;
            }

            throw new NotSupportedException($"Sony key '{key}' not implemented.");
        }

        public async Task LaunchAppAsync(string appIdOrUri, CancellationToken ct)
        {
            // Basic example using appControl. Many TVs expect a specific "uri".
            await PostJsonAsync("/sony/appControl",
                new
                {
                    method  = "setActiveApp",
                    @params = new object[]
                    {
                        new { uri = appIdOrUri }
                    },
                    id      = 1,
                    version = "1.0"
                }, ct);
        }
    }
}
