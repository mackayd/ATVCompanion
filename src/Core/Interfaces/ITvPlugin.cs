using System.Threading;
using System.Threading.Tasks;
using ATVCompanion.Core.Models;

namespace ATVCompanion.Core.Interfaces
{
    public interface ITvPlugin
    {
        string Manufacturer { get; }
        string ModelHint { get; }
        string Host { get; }
        int? Port { get; }

        Task<bool> DiscoverAsync(CancellationToken ct = default);
        Task<PairingResult> PairAsync(CancellationToken ct = default);
        Task<bool> WakeAsync(WakeHint hint, CancellationToken ct = default);
        Task<TvState> GetStateAsync(CancellationToken ct = default);
        Task<bool> PowerOffAsync(CancellationToken ct = default);
        Task<bool> SendKeyAsync(string key, CancellationToken ct = default);
        Task<bool> LaunchAppAsync(string appId, CancellationToken ct = default);
    }
}