using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SimpleVPN.Desktop.Services;

namespace SimpleVPN.Desktop
{
    public partial class MainWindow : Window
    {
        private readonly LicensingService _lic = new();
        private readonly SingBox _sb = new();
        private CancellationTokenSource? _cts;

        private readonly string _appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleVPN");
        private readonly string _statePath;

        private const int HeartbeatSeconds = 7;
        private const int StaleSeconds     = 60;

        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(_appDir);
            _statePath = Path.Combine(_appDir, "active.json");

            var state = LoadState();
            if (!string.IsNullOrWhiteSpace(state.code))
                RedeemTextBox.Text = state.code;
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_sb.IsRunning) { Logger.Info("Already connected."); return; }

                var code = GetCode();
                if (string.IsNullOrWhiteSpace(code)) { Logger.Info("Enter redeem code first."); return; }

                await _lic.RedeemAsync(code, staleWindowSeconds: StaleSeconds);
                var vless = await _lic.GetFreshVlessForCurrentCodeAsync();

                SaveState(new AppState { code = code, lastConnectUtc = DateTime.UtcNow });

                _cts = new CancellationTokenSource();
                await _sb.StartAsync(vless, _cts.Token);

                await _lic.StartHeartbeatAsync(heartbeatIntervalSeconds: HeartbeatSeconds, staleWindowSeconds: StaleSeconds);
                Logger.Info("Connected.");
            }
            catch (Exception ex)
            {
                Logger.Info("Connect failed: " + ex.Message);
                try { await _lic.StopHeartbeatAsync(markInactive: true); } catch { }
                _sb.Stop();
            }
        }

        private async void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _lic.StopHeartbeatAsync(markInactive: true);
                _sb.Stop();
                Logger.Info("Disconnected.");
            }
            catch (Exception ex) { Logger.Info("Disconnect error: " + ex.Message); }
        }

        private string GetCode()
        {
            var t = RedeemTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) return t;
            var state = LoadState();
            return state.code ?? "";
        }

        protected override async void OnClosed(EventArgs e)
        {
            if (_sb.IsRunning)
            {
                try { await _lic.StopHeartbeatAsync(markInactive: true); } catch { }
                _sb.Stop();
            }
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        private record AppState { public string? code { get; set; } public DateTime? lastConnectUtc { get; set; } }

        private AppState LoadState()
        {
            try
            {
                if (File.Exists(_statePath))
                    return JsonSerializer.Deserialize<AppState>(File.ReadAllText(_statePath)) ?? new AppState();
            } catch { }
            return new AppState();
        }

        private void SaveState(AppState s)
        {
            try { File.WriteAllText(_statePath, JsonSerializer.Serialize(s)); } catch { }
        }
    }
}


