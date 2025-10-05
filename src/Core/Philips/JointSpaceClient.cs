
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ATVCompanion.Core.Philips;

public sealed class JointSpaceClient
{
    private readonly HttpClient _http;
    private readonly string _base; // e.g. http://ip:1926/6

    public JointSpaceClient(string host, int port = 1926, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _base = $"http://{host}:{port}/6";
    }

    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            using var r = await _http.GetAsync($"{_base}/system", ct);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task SendKeyAsync(string key, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { key });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var r = await _http.PostAsync($"{_base}/input/key", content, ct);
        r.EnsureSuccessStatusCode();
    }
}
