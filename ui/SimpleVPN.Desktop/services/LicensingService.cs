// File: services/LicensingService.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop.Services
{
    public sealed class LicensingService : IDisposable
    {
        private readonly Firebase _fb;
        private readonly string _deviceId;
        private string? _activeCode;
        private CancellationTokenSource? _hbCts;

        public LicensingService(Firebase fb)
        {
            _fb = fb;
            _deviceId = Firebase.DeviceId();
        }

        public async Task AcquireOrResumeLockAsync(string code, CancellationToken ct = default)
        {
            // 1) ensure auth
            await _fb.SignInAnonymouslyAsync(ct);
            var uid = _fb.Uid;

            // 2) read code doc
            var path = $"codes/{code}";
            var doc = await _fb.GetDocAsync(path, ct);
            if (doc is null)
                throw new InvalidOperationException("Invalid code.");

            // read current lock (if any)
            var fields = doc.Value.GetProperty("fields");
            string? owner = Firebase.GetString(doc.Value, "owner");
            var lockMap = Firebase.GetMap(doc.Value, "lock");

            string? lockedDevice = null;
            bool? lockActive = null;

            if (lockMap is JsonElement m)
            {
                lockedDevice = Firebase.GetString(new JsonElement { }, ""); // placeholder not used
                // manual pull:
                try
                {
                    var mv = m; // fields of map lock
                    if (mv.TryGetProperty("deviceId", out var did) && did.TryGetProperty("stringValue", out var s1))
                        lockedDevice = s1.GetString();
                    if (mv.TryGetProperty("active", out var act) && act.TryGetProperty("booleanValue", out var b1))
                        lockActive = b1.GetBoolean();
                }
                catch { /* ignore */ }
            }

            // 3) decide: take or reuse
            var take = false;
            if (owner is null || owner == uid)
            {
                // same owner or unowned ⇒ ok to take
                if (lockedDevice is null || lockedDevice == _deviceId || lockActive == false)
                    take = true;
            }

            if (!take)
                throw new InvalidOperationException("Code is locked by another device.");

            // 4) write/merge lock
            var payload = new Dictionary<string, object>
            {
                ["owner"] = uid,
                ["lock"] = new Dictionary<string, object>
                {
                    ["deviceId"] = _deviceId,
                    ["platform"] = "windows",
                    ["active"] = true,
                    ["lastSeen"] = DateTime.UtcNow
                }
            };

            await _fb.PatchDocAsync(path, payload, ct: ct);
            _activeCode = code;
        }

        public async Task<string> FetchVlessAsync(CancellationToken ct = default)
        {
            if (_activeCode is null) throw new InvalidOperationException("No active code.");

            var doc = await _fb.GetDocAsync($"codes/{_activeCode}", ct);
            if (doc is null) throw new InvalidOperationException("Code vanished.");

            // Prefer fields.vless.stringValue
            var vless = Firebase.GetString(doc.Value, "vless");
            if (string.IsNullOrWhiteSpace(vless))
                throw new InvalidOperationException("No VLESS config on this code.");
            return vless!;
        }

        public void StartHeartbeat(TimeSpan? interval = null)
        {
            if (_activeCode is null) return;
            _hbCts?.Cancel();
            _hbCts = new CancellationTokenSource();

            var iv = interval ?? TimeSpan.FromSeconds(7);
            _ = Task.Run(async () =>
            {
                var ct = _hbCts!.Token;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await _fb.PatchDocAsync($"codes/{_activeCode}", new Dictionary<string, object>
                        {
                            ["lock"] = new Dictionary<string, object>
                            {
                                ["deviceId"] = _deviceId,
                                ["platform"] = "windows",
                                ["active"] = true,
                                ["lastSeen"] = DateTime.UtcNow
                            }
                        }, ct: ct);
                    }
                    catch { /* swallow; next tick */ }
                    try { await Task.Delay(iv, ct); } catch { break; }
                }
            }, _hbCts.Token);
        }

        public async Task SafeReleaseAsync(CancellationToken ct = default)
        {
            try
            {
                _hbCts?.Cancel();
                if (_activeCode is null) return;

                await _fb.PatchDocAsync($"codes/{_activeCode}", new Dictionary<string, object>
                {
                    ["lock"] = new Dictionary<string, object>
                    {
                        ["deviceId"] = _deviceId,
                        ["platform"] = "windows",
                        ["active"] = false,
                        ["lastSeen"] = DateTime.UtcNow
                    }
                }, ct: ct);
            }
            finally
            {
                _activeCode = null;
            }
        }

        public void Dispose()
        {
            _hbCts?.Cancel();
            _hbCts?.Dispose();
        }
    }
}
