using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SimpleVPN.Desktop
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        readonly FirestoreGateway _gw = new FirestoreGateway();
        readonly SingBox _sb = new SingBox();
        readonly Heartbeat _hb;
        string _redeemCode;
        string _redeemId;
        string _status = "Ready";
        string _minorStatus = "";
        Brush _statusBrush = new SolidColorBrush(Color.FromRgb(158, 173, 200));
        bool _connected;
        string _log = "";
        string _lastLine = "";

        public MainViewModel()
        {
            _hb = new Heartbeat(_gw);
            _sb.OutputReceived += line => { LogText += line + Environment.NewLine; LastLogLine = line; };

            RedeemCommand     = new RelayCommand(async () => await RedeemAsync(), () => true);
            ConnectCommand    = new RelayCommand(async () => await ConnectAsync(), () => !string.IsNullOrWhiteSpace(_redeemId) || LooksLikeVless(RedeemCode));
            DisconnectCommand = new RelayCommand(Disconnect, () => _connected);
            RefreshCommand    = new RelayCommand(async () => await RefreshAsync(), () => !string.IsNullOrWhiteSpace(_redeemId));

            UpdateStatus("Ready", Colors.LightSteelBlue);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void OnChanged([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public string RedeemCode
        {
            get => _redeemCode;
            set { _redeemCode = value; OnChanged(); RaiseCanStates(); }
        }

        public string StatusText
        {
            get => _status;
            private set { _status = value; OnChanged(); }
        }

        public Brush StatusBrush
        {
            get => _statusBrush;
            private set { _statusBrush = value; OnChanged(); }
        }

        public string MinorStatus
        {
            get => _minorStatus;
            private set { _minorStatus = value; OnChanged(); }
        }

        public string LogText
        {
            get => _log;
            private set { _log = value; OnChanged(); }
        }

        public string LastLogLine
        {
            get => _lastLine;
            private set { _lastLine = value; OnChanged(); }
        }

        public RelayCommand RedeemCommand { get; }
        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public RelayCommand RefreshCommand { get; }

        async Task RedeemAsync()
        {
            UpdateStatus("Redeeming…", Colors.DeepSkyBlue);
            var (ok, msg, id) = await _gw.RedeemAsync(RedeemCode?.Trim(), CancellationToken.None);
            if (!ok) { UpdateStatus("Redeem failed", Colors.IndianRed, "Reason: " + msg); return; }
            _redeemId = id;
            UpdateStatus("Code ready", Colors.MediumSpringGreen, "ID: " + id);
            RaiseCanStates();
        }

        async Task ConnectAsync()
        {
            UpdateStatus("Preparing config…", Colors.CornflowerBlue);

            if (LooksLikeVless(RedeemCode))
                _redeemId ??= "direct";

            var (ok, msg) = await _gw.ConnectAsync(_redeemId, CancellationToken.None);
            if (!ok) { UpdateStatus("Config error", Colors.IndianRed, msg); return; }

            UpdateStatus("Starting sing-box…", Colors.LightSkyBlue, "You may see a UAC prompt.");
            var started = await _sb.StartAsync(CancellationToken.None);
            if (!started) { UpdateStatus("sing-box failed", Colors.IndianRed, LastLogLine); return; }

            _connected = true;
            _hb.Start(_redeemId);
            UpdateStatus("Connected", Colors.MediumSpringGreen);
            RaiseCanStates();
        }

        void Disconnect()
        {
            _hb.Stop();
            _sb.Stop();
            _connected = false;
            UpdateStatus("Disconnected", Colors.SlateGray);
            RaiseCanStates();
        }

        async Task RefreshAsync()
        {
            var (ok, msg) = await _gw.RefreshAsync(_redeemId, CancellationToken.None);
            if (!ok) { UpdateStatus("Refresh failed", Colors.IndianRed, msg); return; }
            UpdateStatus("Refreshed", Colors.MediumSpringGreen);
        }

        static bool LooksLikeVless(string s) => !string.IsNullOrWhiteSpace(s) && s.Trim().StartsWith("vless://", StringComparison.OrdinalIgnoreCase);

        void UpdateStatus(string text, Color c, string minor = "")
        {
            StatusText = text;
            StatusBrush = new SolidColorBrush(c);
            MinorStatus = minor;
        }

        void RaiseCanStates()
        {
            ConnectCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }
}
