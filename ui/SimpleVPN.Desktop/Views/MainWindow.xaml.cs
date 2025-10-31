using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Drawing;              // Icon
using WF = System.Windows.Forms;
using SimpleVPN.Desktop.Services;
// WinForms tray

using SimpleVPN.Desktop.Services;

namespace SimpleVPN.Desktop.Views
{
    public partial class MainWindow : Window
    {
        private readonly WF.NotifyIcon _tray;
        private static readonly string AppDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleVPN");

        // Firebase project creds
        private readonly Firebase _fb = new Firebase(
            "simplevpn-3272d",
            "AIzaSyCV3CSoZ7V8uSGRuFwY82FJ4fnC66Ky5jc"
        );

        private LicensingService? _lic;

        public MainWindow()
        {
            InitializeComponent();

            Directory.CreateDirectory(AppDataDir);

            _tray = new WF.NotifyIcon
            {
                Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "logo.ico")),
                Visible = false,
                Text = "SimpleVPN",
                ContextMenuStrip = new WF.ContextMenuStrip()
            };

            _tray.ContextMenuStrip.Items.Add("Show", null, (_, __) =>
            {
                Show();
                WindowState = WindowState.Normal;
                _tray.Visible = false;
            });

            _tray.ContextMenuStrip.Items.Add("Disconnect", null, async (_, __) =>
            {
                await DisconnectAsync();
            });

            _tray.ContextMenuStrip.Items.Add("Exit (keeps VPN)", null, (_, __) =>
            {
                // Do NOT disconnect — keep tunnel alive
                _tray.Visible = false;
                System.Windows.Application.Current.Shutdown();
            });

            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Hide to tray if VPN is running
            if (SingBox.IsRunning)
            {
                e.Cancel = true;
                Hide();
                _tray.Visible = true;
            }
        }

        // ----------------- UI Event handlers -----------------
        private async void ConnectBtn_Click(object sender, RoutedEventArgs e) => await ConnectAsync();
        private async void DisconnectBtn_Click(object sender, RoutedEventArgs e) => await DisconnectAsync();

        // ----------------- Logic -----------------
        private async Task ConnectAsync()
        {
            try
            {
                StatusText.Text = "Status: redeeming…";
                var code = RedeemInput.Text?.Trim();
                if (string.IsNullOrWhiteSpace(code))
                    throw new Exception("Enter redeem code.");

                _lic ??= new LicensingService(_fb);
                await _lic.AcquireOrResumeLockAsync(code);

                StatusText.Text = "Status: fetching VLESS…";
                var vless = await _lic.FetchVlessAsync();

                StatusText.Text = "Status: starting tunnel…";
                await SingBox.StartAsync(vless);

                _lic.StartHeartbeat();

                StatusText.Text = "Status: Connected";
            }
            catch (Exception ex)
            {
                Logger.Log("Connect failed: " + ex);
                StatusText.Text = "Status: Connect failed";
                System.Windows.MessageBox.Show(
                    "Connect failed. See log:\n" + Logger.LogPath,
                    "SimpleVPN", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DisconnectAsync()
        {
            try
            {
                StatusText.Text = "Status: stopping…";
                await SingBox.StopAsync();
                if (_lic != null) await _lic.SafeReleaseAsync();
                StatusText.Text = "Status: Disconnected";
            }
            catch (Exception ex)
            {
                Logger.Log("Disconnect failed: " + ex);
                StatusText.Text = "Status: Disconnect failed";
            }
        }
    }
}
