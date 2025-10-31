using System.Timers;
using System.Windows;

namespace SimpleVPN.Desktop;

public partial class MainWindow : Window
{
    private readonly FirebaseClient _fb = new();
    private readonly System.Timers.Timer _hb = new(AppSecrets.HeartbeatPeriod.TotalMilliseconds);
    private string? _activeCode;
    private string? _vlessPath; // e.g., "configs/u08"
    private string? _currentVless; // last fetched string

    public MainWindow()
    {
        InitializeComponent();
        _hb.Elapsed += Hb_Elapsed;
    }

    private void Log(string msg) =>
        Dispatcher.Invoke(() => StatusText.Text = msg);

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConnectBtn.IsEnabled = false;
            DisconnectBtn.IsEnabled = false;

            await _fb.EnsureAnonAsync();

            var code = RedeemBox.Text.Trim();
            if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(_activeCode))
            {
                Log("Enter redeem code first.");
                return;
            }
            if (!string.IsNullOrEmpty(code))
            {
                // redeem/refresh grant & lock
                await RedeemAndLockAsync(code);
                _activeCode = code;
            }
            else
            {
                // no new code typed — verify still locked to me
                if (string.IsNullOrEmpty(_activeCode))
                {
                    Log("No redeem code set.");
                    return;
                }
                await EnsureStillLockedAsync(_activeCode);
            }

            // Always fetch latest vless (rotates when blocked)
            var vless = await FetchLatestVlessAsync();
            _currentVless = vless;
            SingBox.WriteConfigFromVless(vless);
            SingBox.Start();

            _hb.Start();
            Log($"Connected as {_fb.Uid?.Substring(0, 6)}…");
        }
        catch (Exception ex)
        {
            Log("Connect failed: " + ex.Message);
            SingBox.Stop();
        }
        finally
        {
            ConnectBtn.IsEnabled = true;
            DisconnectBtn.IsEnabled = true;
        }
    }

    private async void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DisconnectBtn.IsEnabled = false;
            _hb.Stop();
            SingBox.Stop();
            if (!string.IsNullOrEmpty(_activeCode))
                await ReleaseLockAsync(_activeCode);
            Log("Disconnected.");
        }
        catch (Exception ex)
        {
            Log("Disconnect err: " + ex.Message);
        }
        finally
        {
            DisconnectBtn.IsEnabled = true;
        }
    }

    // —— Firestore flows ——

    private async Task RedeemAndLockAsync(string code)
    {
        // 1) read code doc
        var codePath = $"{AppSecrets.CodesCol}/{Uri.EscapeDataString(code)}";
        var doc = await _fb.GetDocAsync(codePath);
        if (doc is null) throw new Exception("Code not found.");

        var active = doc.TryGetValue("active", out var a) && a is bool b && b;
        if (!active) throw new Exception("Code disabled.");

        // expire check (optional – you have expiresAt in screenshots)
        if (doc.TryGetValue("expiresAt", out var ex) && ex is DateTimeOffset ts && ts < DateTimeOffset.UtcNow)
            throw new Exception("Code expired.");

        // lock check
        var lockMap = doc.TryGetValue("lock", out var l) ? l as Dictionary<string, object> : null;
        var lockedBy = lockMap != null && lockMap.TryGetValue("lockedBy", out var lb) ? lb as string : null;

        if (!string.IsNullOrEmpty(lockedBy) && lockedBy != _fb.Uid)
            throw new Exception("Code already in use on another device.");

        // figure out vless doc
        var vp = doc.TryGetValue("vlessPath", out var vpv) ? vpv as string : null;
        _vlessPath = string.IsNullOrWhiteSpace(vp) ? $"{AppSecrets.ConfigsCol}/{AppSecrets.DefaultConfigDoc}" : vp;

        // 2) acquire lock + write grant
        var now = DateTimeOffset.UtcNow;
        await _fb.PatchDocAsync(codePath, new()
        {
            ["lock"] = FirebaseClient.Map(new()
            {
                ["lockedBy"] = FirebaseClient.Str(_fb.Uid!),
                ["platform"] = FirebaseClient.Str("windows"),
                ["lockAt"] = FirebaseClient.Ts(now),
                ["lastSeen"] = FirebaseClient.Ts(now)
            })
        }, updateMask: new[] { "lock" });

        var grantPath = $"{AppSecrets.GrantsCol}/{_fb.Uid}";
        await _fb.SetDocAsync(grantPath, new()
        {
            ["code"] = FirebaseClient.Str(code),
            ["vlessPath"] = FirebaseClient.Str(_vlessPath!),
            ["updatedAt"] = FirebaseClient.Ts(now)
        });

        Log("Redeemed & locked.");
    }

    private async Task EnsureStillLockedAsync(string code)
    {
        var codePath = $"{AppSecrets.CodesCol}/{Uri.EscapeDataString(code)}";
        var doc = await _fb.GetDocAsync(codePath, "lock,vlessPath,active,expiresAt");
        if (doc is null) throw new Exception("Code not found.");
        var lockMap = doc.TryGetValue("lock", out var l) ? l as Dictionary<string, object> : null;
        var lockedBy = lockMap != null && lockMap.TryGetValue("lockedBy", out var lb) ? lb as string : null;
        if (lockedBy != _fb.Uid) throw new Exception("Lost lock to another device.");

        var vp = doc.TryGetValue("vlessPath", out var vpv) ? vpv as string : null;
        _vlessPath = string.IsNullOrWhiteSpace(vp) ? $"{AppSecrets.ConfigsCol}/{AppSecrets.DefaultConfigDoc}" : vp;
    }

    private async Task<string> FetchLatestVlessAsync()
    {
        // _vlessPath can be like "configs/u08" or "configs/current"
        var path = _vlessPath ?? $"{AppSecrets.ConfigsCol}/{AppSecrets.DefaultConfigDoc}";
        var doc = await _fb.GetDocAsync(path, "vless");
        if (doc is null || !doc.TryGetValue("vless", out var v) || v is not string s || !s.StartsWith("vless://"))
            throw new Exception("VLESS missing/invalid.");
        return s;
    }

    private async void Hb_Elapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            // refresh lastSeen + pingy lightweight connection doc
            var now = DateTimeOffset.UtcNow;
            var connPath = $"{AppSecrets.ConnsCol}/{_fb.Uid}";
            await _fb.SetDocAsync(connPath, new()
            {
                ["lastSeen"] = FirebaseClient.Ts(now),
                ["platform"] = FirebaseClient.Str("windows"),
                ["status"] = FirebaseClient.Str("online")
            });

            if (!string.IsNullOrEmpty(_activeCode))
            {
                var codePath = $"{AppSecrets.CodesCol}/{Uri.EscapeDataString(_activeCode)}";
                await _fb.PatchDocAsync(codePath, new()
                {
                    ["lock"] = FirebaseClient.Map(new()
                    {
                        ["lockedBy"] = FirebaseClient.Str(_fb.Uid!),
                        ["lastSeen"] = FirebaseClient.Ts(now),
                        ["platform"] = FirebaseClient.Str("windows")
                    })
                }, updateMask: new[] { "lock" });
            }
        }
        catch
        {
            // swallow: heartbeat must be resilient
        }
    }

    private async Task ReleaseLockAsync(string code)
    {
        try
        {
            var codePath = $"{AppSecrets.CodesCol}/{Uri.EscapeDataString(code)}";
            // only clear if it's ours
            var doc = await _fb.GetDocAsync(codePath, "lock");
            var lockMap = doc?["lock"] as Dictionary<string, object>;
            var lockedBy = lockMap != null && lockMap.TryGetValue("lockedBy", out var lb) ? lb as string : null;
            if (lockedBy == _fb.Uid)
            {
                await _fb.PatchDocAsync(codePath, new()
                {
                    ["lock"] = FirebaseClient.Map(new()) // empty map = clear
                }, updateMask: new[] { "lock" });
            }
        }
        catch { /* ignore */ }
    }
}
