using ATVCompanion.Core.Interfaces;
using ATVCompanion.Core.Models;
using ATVCompanion.Core.Networking;

namespace ATVCompanion.Core.Philips;

public sealed class PhilipsJointSpacePlugin : ITvPlugin
{
    private readonly string _host;
    private readonly int _port;
    private JointSpaceClient _client;

    public PhilipsJointSpacePlugin(string host, int port = 1926)
    {
        _host = host;
        _port = port;
        _client = new JointSpaceClient(_host, _port);
    }

    public string Manufacturer => "Philips";
    public string ModelHint => "Android/Google TV (JointSPACE v6)";
    public string Host => _host;
    public int? Port => _port;

    public async Task<bool> DiscoverAsync(CancellationToken ct = default)
    {
        if (await _client.ProbeAsync(ct)) return true;
        var alt = new JointSpaceClient(_host, 1925);
        return await alt.ProbeAsync(ct);
    }

    public Task<PairingResult> PairAsync(CancellationToken ct = default)
        => Task.FromResult(PairingResult.SuccessResult()); // TODO: implement PIN pairing if needed

    public async Task<bool> WakeAsync(WakeHint hint, CancellationToken ct = default)
    {
        WolClient.Wake(hint.Mac, hint.Port, hint.BroadcastIp);
        await Task.Delay(2500, ct);
        return true;
    }

    public async Task<TvState> GetStateAsync(CancellationToken ct = default)
        => new(PowerStatus.Unknown);

    public async Task<bool> PowerOffAsync(CancellationToken ct = default)
    {
        try { await _client.SendKeyAsync("Standby", ct); return true; } catch { return false; }
    }

    public async Task<bool> SendKeyAsync(string key, CancellationToken ct = default)
    {
        try { await _client.SendKeyAsync(key, ct); return true; } catch { return false; }
    }

    public Task<bool> LaunchAppAsync(string appId, CancellationToken ct = default)
        => Task.FromResult(false); // TODO
}