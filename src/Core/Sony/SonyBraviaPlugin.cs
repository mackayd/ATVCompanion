using System.Net.Http;
using System.Text;
using ATVCompanion.Core.Interfaces;
using ATVCompanion.Core.Models;
using ATVCompanion.Core.Networking;

namespace ATVCompanion.Core.Sony;

public sealed class SonyBraviaPlugin : ITvPlugin
{
    private readonly HttpClient _http;
    private readonly string _host;
    private readonly string? _psk;

    public SonyBraviaPlugin(string host, string? psk)
    {
        _http = new HttpClient();
        _host = host;
        _psk = psk;
    }

    public string Manufacturer => "Sony";
    public string ModelHint => "BRAVIA (JSON-RPC / IRCC-IP)";
    public string Host => _host;
    public int? Port => 80;

    public async Task<bool> DiscoverAsync(CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"http://{_host}/sony/system");
            req.Content = new StringContent("{\"method\":\"getPowerStatus\",\"id\":1,\"params\":[],\"version\":\"1.0\"}", Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(_psk)) req.Headers.Add("X-Auth-PSK", _psk);
            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public Task<PairingResult> PairAsync(CancellationToken ct = default)
        => Task.FromResult(string.IsNullOrEmpty(_psk) ? PairingResult.NeedsUserPsk() : PairingResult.SuccessResult());

    public async Task<bool> WakeAsync(WakeHint hint, CancellationToken ct = default)
    {
        WolClient.Wake(hint.Mac, hint.Port, hint.BroadcastIp);
        await Task.Delay(2500, ct);
        return true;
    }

    public Task<TvState> GetStateAsync(CancellationToken ct = default) => Task.FromResult(new TvState(PowerStatus.Unknown));

    public async Task<bool> PowerOffAsync(CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"http://{_host}/sony/system");
            req.Content = new StringContent("{\"method\":\"setPowerStatus\",\"id\":1,\"params\":[{\"status\":false}],\"version\":\"1.0\"}", Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(_psk)) req.Headers.Add("X-Auth-PSK", _psk);
            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public Task<bool> SendKeyAsync(string key, CancellationToken ct = default)
        => Task.FromResult(false); // TODO

    public Task<bool> LaunchAppAsync(string appId, CancellationToken ct = default)
        => Task.FromResult(false); // TODO
}