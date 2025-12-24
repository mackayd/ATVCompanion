using System.Net.Http;
using System.Text;
using System.Text.Json;
using ATVCompanion.Core.Interfaces;
using ATVCompanion.Core.Models;
using ATVCompanion.Core.Networking;

namespace ATVCompanion.Core.Sony;

public sealed class SonyBraviaPlugin : ITvPlugin
{
    private readonly HttpClient _http;
    private readonly string _host;
    private readonly string? _psk;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SonyBraviaPlugin(string host, string? psk)
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _host = host;
        _psk = psk;
    }

    public string Manufacturer => "Sony";
    public string ModelHint    => "BRAVIA (JSON-RPC / IRCC-IP)";
    public string Host         => _host;
    public int?   Port         => 80; // Sony JSON-RPC default over HTTP

    private string Url(string path) => $"http://{_host}{path}";

    private static StringContent JsonBody(object body)
        => new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

    private HttpRequestMessage NewReq(HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, Url(path));
        if (body is not null) req.Content = JsonBody(body);
        if (!string.IsNullOrWhiteSpace(_psk))
            req.Headers.TryAddWithoutValidation("X-Auth-PSK", _psk);
        return req;
    }

    public async Task<bool> DiscoverAsync(CancellationToken ct = default)
    {
        try
        {
            using var req  = NewReq(HttpMethod.Post, "/sony/system", new {
                method  = "getPowerStatus",
                id      = 1,
                @params = Array.Empty<object>(),
                version = "1.0"
            });
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<PairingResult> PairAsync(CancellationToken ct = default)
    {
        // Sony uses X-Auth-PSK (set on the TV). No network challenge/response like Philips.
        // If we don't have a PSK, ask UI to prompt user and save into AppConfig.AuthKey.
        return Task.FromResult(
            string.IsNullOrWhiteSpace(_psk)
                ? PairingResult.NeedsUserPsk("Enter the TV's PSK (Settings > Network > Home Network > Pre-Shared Key).")
                : PairingResult.SuccessResult()
        );
    }

    public async Task<bool> WakeAsync(WakeHint hint, CancellationToken ct = default)
    {
        // WOL: MAC required; broadcast IP and port optional
        WolClient.Wake(hint.Mac, hint.Port, hint.BroadcastIp);
        await Task.Delay(250, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<TvState> GetStateAsync(CancellationToken ct = default)
    {
        try
        {
            using var req  = NewReq(HttpMethod.Post, "/sony/system", new {
                method  = "getPowerStatus",
                id      = 1,
                @params = Array.Empty<object>(),
                version = "1.0"
            });
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return new TvState(PowerStatus.Unknown);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.Array &&
                result.GetArrayLength() > 0)
            {
                var obj = result[0];
                if (obj.TryGetProperty("status", out var statusProp))
                {
                    var status = statusProp.GetString();
                    var power = status?.Equals("active", StringComparison.OrdinalIgnoreCase) == true
                        ? PowerStatus.On
                        : status?.Equals("standby", StringComparison.OrdinalIgnoreCase) == true
                            ? PowerStatus.Standby
                            : PowerStatus.Unknown;

                    return new TvState(power, null, null);
                }
            }

            return new TvState(PowerStatus.Unknown);
        }
        catch
        {
            return new TvState(PowerStatus.Unknown);
        }
    }

    public async Task<bool> PowerOffAsync(CancellationToken ct = default)
    {
        try
        {
            using var req  = NewReq(HttpMethod.Post, "/sony/system", new {
                method  = "setPowerStatus",
                id      = 1,
                @params = new object[] { new { status = false } },
                version = "1.0"
            });
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SendKeyAsync(string key, CancellationToken ct = default)
    {
        // Minimal implementation: map "Standby" to setPowerStatus(false).
        if (string.Equals(key, "Standby", StringComparison.OrdinalIgnoreCase))
            return await PowerOffAsync(ct).ConfigureAwait(false);

        // TODO: implement IRCC (SOAP) for full remote key support if needed.
        return false;
    }

    public async Task<bool> LaunchAppAsync(string appId, CancellationToken ct = default)
    {
        try
        {
            // Many Bravia models expect a "uri" for appControl setActiveApp (e.g., "com.sony.dtv.osatv.player")
            using var req  = NewReq(HttpMethod.Post, "/sony/appControl", new {
                method  = "setActiveApp",
                id      = 1,
                @params = new object[] { new { uri = appId } },
                version = "1.0"
            });
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
