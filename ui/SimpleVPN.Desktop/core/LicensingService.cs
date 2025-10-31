using Google.Cloud.Firestore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop
{
    public sealed class LicensingService
    {
        private readonly FirestoreService _fs;
        private readonly AppSettings _settings;
        private const int HeartbeatSec = 7;
        private const int StaleSec = 45;

        public LicensingService(FirestoreService fs, AppSettings settings)
        { _fs = fs; _settings = settings; }

        public async Task<(bool Success, string Message)> AcquireOrResumeLockAsync(string code, CancellationToken ct)
        {
            var doc = _fs.CodeDoc(code, _settings.CodesCollection);
            var me = LocalState.DeviceId();

            string message = "Locked";
            bool ok = false;

            await _fs.RunTransaction(async t =>
            {
                var snap = await t.GetSnapshotAsync(doc, ct);
                if (!snap.Exists) { ok = false; message = "Code not found."; return; }

                var cd = snap.ConvertTo<CodeDoc>();
                if (!cd.active) { ok = false; message = "Code inactive."; return; }
                var now = Timestamp.FromDateTime(DateTime.UtcNow);

                if (cd.@lock == null)
                {
                    // take new lock
                    cd.@lock = new LockInfo { deviceId = me, platform = _settings.Platform, since = now, lastSeen = now };
                    t.Update(doc, new { @lock = cd.@lock });
                    ok = true; message = "Lock acquired."; return;
                }

                if (cd.@lock.deviceId == me)
                {
                    // resume
                    t.Update(doc, new { @lock = new { deviceId = me, platform = _settings.Platform, since = cd.@lock.since, lastSeen = now } });
                    ok = true; message = "Resumed."; return;
                }

                // other device holds it — allow takeover if stale
                var age = (now.ToDateTime() - cd.@lock.lastSeen.ToDateTime()).TotalSeconds;
                if (age > StaleSec)
                {
                t.Update(doc, new
                {
    lock = new
    {
        lockedBy = me,
        platform = _settings.Platform,
        lockAt = now
    }
});

            ok = true; message = "Took over stale lock."; return;
                }

                ok = false; message = "In use on another device.";
            }, ct);

            return (ok, message);
        }

        public async Task<string> FetchVlessAsync(string code, CancellationToken ct)
        {
            var doc = _fs.CodeDoc(code, _settings.CodesCollection);
            var snap = await doc.GetSnapshotAsync(ct);
            if (!snap.Exists) throw new InvalidOperationException("Code not found.");

            var data = snap.ToDictionary();
            if (!data.ContainsKey("vlessPath")) throw new InvalidOperationException("Missing vlessPath.");

            string vlessPath = data["vlessPath"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(vlessPath)) throw new InvalidOperationException("Empty vlessPath.");

            // Example: "configs/u08"
            var parts = vlessPath.Split('/');
            if (parts.Length != 2) throw new InvalidOperationException($"Invalid vlessPath: {vlessPath}");

            var cfgDoc = _fs.CodeDoc(parts[1], parts[0]); // parts[0] = "configs", parts[1] = "u08"
            var vlessSnap = await cfgDoc.GetSnapshotAsync(ct);

            if (!vlessSnap.Exists || !vlessSnap.ContainsField("vless"))
                throw new InvalidOperationException("VLESS not found in config doc.");

            var vless = vlessSnap.GetValue<string>("vless");
            if (string.IsNullOrWhiteSpace(vless))
                throw new InvalidOperationException("Empty vless config.");

            return vless.Trim();
        }


        public async Task StartHeartbeatAsync(string code, CancellationToken ct, Func<Task>? onStaleKick = null)
        {
            var doc = _fs.CodeDoc(code, _settings.CodesCollection);
            var me = LocalState.DeviceId();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(HeartbeatSec), ct);
                    await _fs.RunTransaction(async t =>
                    {
                        var snap = await t.GetSnapshotAsync(doc, ct);
                        if (!snap.Exists) return; // nothing to do

                        var cd = snap.ConvertTo<CodeDoc>();
                        var now = Timestamp.FromDateTime(DateTime.UtcNow);

                        if (cd.@lock == null) return; // released server-side
                        if (cd.@lock.deviceId != me) throw new InvalidOperationException("Lock stolen");

                        t.Update(doc, new { @lock = new { deviceId = me, platform = _settings.Platform, since = cd.@lock.since, lastSeen = now } });
                    }, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception)
                {
                    if (onStaleKick != null) await onStaleKick();
                    break;
                }
            }
        }

        public async Task SafeReleaseAsync(string code)
        {
            try
            {
                var doc = _fs.CodeDoc(code, _settings.CodesCollection);
                var me = LocalState.DeviceId();
                await _fs.RunTransaction(async t =>
                {
                    var snap = await t.GetSnapshotAsync(doc);
                    if (!snap.Exists) return;
                    var cd = snap.ConvertTo<CodeDoc>();
                    if (cd.@lock?.deviceId == me)
                        t.Update(doc, new { @lock = (LockInfo?)null });
                }, default);
            }
            catch { /* swallow */ }
        }
    }
}
