using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SimpleVPN.Desktop
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly LicensingService _licensing;
        private readonly SingBoxService _singbox;
        private CancellationTokenSource? _cts;

        private string _redeemCode = "";
        public string RedeemCode { get => _redeemCode; set { _redeemCode = value.Trim(); OnPropertyChanged(); } }

        private string _status = "Idle";
        public string StatusText { get => _status; set { _status = value; OnPropertyChanged(); } }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        public MainViewModel()
        {
            var settings = AppSettings.Load();                 // reads appsettings.json
            var fs = new FirestoreService(settings.ProjectId); // Firestore client
            _licensing = new LicensingService(fs, settings);
            _singbox = new SingBoxService(settings);

            ConnectCommand = new RelayCommand(async _ => await Connect(), _ => true);
            DisconnectCommand = new RelayCommand(async _ => await Disconnect(), _ => true);

            // Load last code (if you want) without showing VLESS
            RedeemCode = LocalState.TryLoadLastCode() ?? "";
        }

        public async Task Connect()
        {
            if (string.IsNullOrWhiteSpace(RedeemCode))
            {
                StatusText = "Enter redeem code.";
                return;
            }

            try
            {
                StatusText = "Validating / locking…";
                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                var lockResult = await _licensing.AcquireOrResumeLockAsync(RedeemCode, _cts.Token);
                if (!lockResult.Success) { StatusText = lockResult.Message; return; }

                StatusText = "Fetching VLESS…";
                string vless = await _licensing.FetchVlessAsync(RedeemCode, _cts.Token);

                StatusText = "Writing config & starting tunnel…";
                await _singbox.StartAsync(vless, _cts.Token);

                // Kick heartbeat loop
                StatusText = "Connected. Heartbeat running.";
                _ = _licensing.StartHeartbeatAsync(RedeemCode, _cts.Token, onStaleKick: async () =>
                {
                    // If server says lock stolen, stop immediately
                    await _singbox.StopAsync();
                    StatusText = "Lock lost. Disconnected.";
                });

                LocalState.SaveLastCode(RedeemCode);
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                StatusText = "Failed: " + ex.Message;
                await _singbox.StopAsync();
                await _licensing.SafeReleaseAsync(RedeemCode);
            }
        }

        public async Task Disconnect()
        {
            try
            {
                _cts?.Cancel();
                await _singbox.StopAsync();
                await _licensing.SafeReleaseAsync(RedeemCode);
                StatusText = "Disconnected.";
            }
            catch (Exception ex)
            {
                StatusText = "Disconnect error: " + ex.Message;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
