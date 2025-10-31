using System;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop
{
    public sealed class Heartbeat
    {
        readonly FirestoreGateway _gw;
        System.Threading.Timer _timer;
        string _redeemId;

        public Heartbeat(FirestoreGateway gw) => _gw = gw;

        public void Start(string redeemId)
        {
            _redeemId = redeemId;
            _timer?.Dispose();
            _timer = new System.Threading.Timer(async _ => {
                try { await _gw.HeartbeatAsync(_redeemId, CancellationToken.None); } catch { }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            _redeemId = null;
        }

        public async Task<bool> PingAsync(string redeemId, CancellationToken ct)
            => await _gw.HeartbeatAsync(redeemId, ct);
    }
}
