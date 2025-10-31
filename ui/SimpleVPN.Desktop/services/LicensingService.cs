using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop.Services
{
    public sealed class LicensingService
    {
        private readonly Firebase _fb = new Firebase();
        private readonly string _appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleVPN");
        private readonly string _statePath;
        private string? _code;
        private string _deviceId = "";
        private Timer? _hb;

        public LicensingService()
        {
            Directory.CreateDirectory(_appDir);
            _statePath = Path.Combine(_appDir, "redeem.json");
            LoadState();
            _deviceId = LoadDeviceId();
        }

        public string? TryGetSavedCode() => _code;

        public async Task RedeemAsync(string code, int staleWindowSeconds)
        {
            _code = code; SaveState();

            var doc = await _fb.GetDocAsync($"codes/{code}") ?? throw new Exception("Code not found.");
            if (Firebase.TryGetBool(doc, "active", out var active) && !active) throw new Exception("Code inactive.");

            string owner = "";
            Firebase.TryGetString(doc, "lock.ownerDeviceId", out owner);
            bool lockActive = false;
            Firebase.TryGetBool(doc, "lock.active", out lockActive);
            long lastSeen = Firebase.TryGetSeconds(doc, "lock.lastSeen");

            var now = Firebase.NowSeconds();
            bool canBind = string.IsNullOrEmpty(owner) || owner == _deviceId || !lockActive || (now - lastSeen) > staleWindowSeconds;
            if (!canBind) throw new Exception("Code in use on another device.");

            await _fb.PatchDocAsync($"codes/{code}", "lock", Firebase.BuildLockFields(_deviceId, true, now));
        }

        public async Task<string> GetFreshVlessForCurrentCodeAsync()
        {
            if (string.IsNullOrWhiteSpace(_code)) throw new Exception("No code saved.");
            var codeDoc = await _fb.GetDocAsync($"codes/{_code}") ?? throw new Exception("Code missing.");
            if (!Firebase.TryGetString(codeDoc, "vlessPath", out var path) || string.IsNullOrWhiteSpace(path))
                throw new Exception("vlessPath not set.");
            var cfgDoc = await _fb.GetDocAsync(path) ?? throw new Exception("Config missing.");
            if (!Firebase.TryGetString(cfgDoc, "vless", out var vless) || string.IsNullOrWhiteSpace(vless))
                throw new Exception("Empty vless.");

            // Cache silently (never shown to user)
            File.WriteAllText(Path.Combine(_appDir, "last.vless"), vless);
            return vless;
        }

        public async Task StartHeartbeatAsync(int heartbeatIntervalSeconds, int staleWindowSeconds)
        {
            if (string.IsNullOrWhiteSpace(_code)) return;
            _hb?.Dispose();
            _hb = new Timer(async _ =>
            {
                try
                {
                    var now = Firebase.NowSeconds();
                    await _fb.PatchDocAsync($"codes/{_code}", "lock", Firebase.BuildLockFields(_deviceId, true, now));
                }
                catch { /* ignore */ }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(heartbeatIntervalSeconds));
            await Task.CompletedTask;
        }

        public async Task StopHeartbeatAsync(bool markInactive)
        {
            _hb?.Dispose(); _hb = null;
            if (string.IsNullOrWhiteSpace(_code)) return;
            var now = Firebase.NowSeconds();
            await _fb.PatchDocAsync($"codes/{_code}", "lock", Firebase.BuildLockFields(_deviceId, markInactive, now));
        }

        // ---------- local state ----------
        private void LoadState()
        {
            try
            {
                if (!File.Exists(_statePath)) return;
                var s = JsonSerializer.Deserialize<State>(File.ReadAllText(_statePath));
                _code = s?.Code;
            }
            catch { }
        }
        private void SaveState()
        {
            try { File.WriteAllText(_statePath, JsonSerializer.Serialize(new State { Code = _code })); } catch { }
        }

        private static string LoadDeviceId()
        {
            try
            {
                var val = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")?
                    .GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrWhiteSpace(val)) return "win-" + val;
            }
            catch { }

            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleVPN", "device.json");
            try
            {
                if (File.Exists(path))
                {
                    var s = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
                    if (!string.IsNullOrWhiteSpace(s?.DeviceId)) return s.DeviceId!;
                }
            } catch { }

            var id = "win-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllText(path, JsonSerializer.Serialize(new State { DeviceId = id })); } catch { }
            return id;
        }

        private sealed class State
        {
            public string? Code { get; set; }
            public string? DeviceId { get; set; }
        }
    }
}
