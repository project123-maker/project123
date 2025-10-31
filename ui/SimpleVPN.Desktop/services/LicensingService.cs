// ui/SimpleVPN.Desktop/services/LicensingService.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop.Services
{
    // Minimal 1-code-1-device flow
    public sealed class LicensingService
    {
        private readonly Firebase _fb;
        private string? _code;
        private CancellationTokenSource? _hbCts;

        public LicensingService(Firebase fb) => _fb = fb;

        public async Task AcquireOrResumeLockAsync(string code, CancellationToken ct = default)
        {
            _code = code;
            await _fb.SignInAnonymouslyAsync(ct);

            var dev = Firebase.DeviceId();
            var path = $"codes/{code}";
            var doc = await _fb.GetDocAsync(path, ct);
            if (doc is null) throw new Exception("Code not found.");

            // check lock
            var lockedTo = Firebase.GetString(doc.Value, "lock");
            var owner = Firebase.GetString(doc.Value, "owner");

            if (string.IsNullOrEmpty(lockedTo) || lockedTo == dev)
            {
                // lock (or refresh)
                var fields = new Dictionary<string, object>
                {
                    ["lock"] = dev,
                    ["owner"] = owner ?? _fb.Uid,
                    ["lastSeen"] = DateTime.UtcNow
                };
                await _fb.PatchDocAsync(path, fields, updateMask: new[] { "lock", "owner", "lastSeen" }, ct);
            }
            else
            {
                throw new Exception("Code is locked on another device.");
            }
        }

        public async Task<string> FetchVlessAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_code)) throw new InvalidOperationException("No code.");
            var path = $"codes/{_code}";
            var doc = await _fb.GetDocAsync(path, ct);
            if (doc is null) throw new Exception("Code not found.");
            var vless = Firebase.GetString(doc.Value, "vless");
            if (string.IsNullOrWhiteSpace(vless)) throw new Exception("No VLESS in code document.");
            return vless!;
        }

        public void StartHeartbeat()
        {
            _hbCts?.Cancel();
            _hbCts = new CancellationTokenSource();
            var ct = _hbCts.Token;

            _ = Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(_code)) return;
                var path = $"codes/{_code}";
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await _fb.PatchDocAsync(path, new Dictionary<string, object>
                        {
                            ["lastSeen"] = DateTime.UtcNow
                        }, updateMask: new[] { "lastSeen" }, ct);
                    }
                    catch { /* ignore transient */ }
                    await Task.Delay(7000, ct);
                }
            }, ct);
        }

        public async Task SafeReleaseAsync()
        {
            try
            {
                _hbCts?.Cancel();
                if (string.IsNullOrEmpty(_code)) return;
                var path = $"codes/{_code}";
                var doc = await _fb.GetDocAsync(path);
                if (doc is null) return;

                var dev = Firebase.DeviceId();
                var lockedTo = Firebase.GetString(doc.Value, "lock");
                if (lockedTo == dev)
                {
                    await _fb.PatchDocAsync(path, new Dictionary<string, object>
                    {
                        ["lock"] = ""
                    }, updateMask: new[] { "lock" });
                }
            }
            catch { /* ignore */ }
        }
    }
}
