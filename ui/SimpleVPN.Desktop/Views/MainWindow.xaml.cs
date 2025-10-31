using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WF = System.Windows.Forms; // WinForms alias ONLY for tray

namespace SimpleVPN.Desktop.Views
{
    public partial class MainWindow : Window
    {
        private readonly WF.NotifyIcon _tray;

        public MainWindow()
        {
            InitializeComponent();

            // Build NotifyIcon (tray)
            _tray = new WF.NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "SimpleVPN",
                Visible = false,
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
                _tray.Visible = false;
                System.Windows.Application.Current.Shutdown();
            });

            // keep VPN running if window is closed
            Closing += MainWindow_Closing;
        }

        private static System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var icoPath = Path.Combine(baseDir, "assets", "logo.ico");
                if (File.Exists(icoPath))
                    return new System.Drawing.Icon(icoPath);
            }
            catch { /* ignore */ }

            // fallback: generic system icon
            return System.Drawing.SystemIcons.Application;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Keep VPN running. Just hide window when connected.
            try
            {
                if (SingBox.IsRunning)
                {
                    e.Cancel = true;
                    Hide();
                    _tray.Visible = true;
                }
            }
            catch
            {
                // if anything goes wrong, allow normal close
            }
        }

        // ===== UI handlers =====

        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Status: Connecting…";
            try
            {
                // TODO: call your actual redeem + fetch VLESS flow here
                // await LicensingServiceFlow.ConnectAsync(...);

                await Task.Delay(300); // placeholder
                StatusText.Text = "Status: Connected";
            }
            catch (Exception ex)
            {
                Logger.Log("Connect failed: " + ex);
                StatusText.Text = "Status: Connect failed";
                System.Windows.MessageBox.Show(
                    "Connect failed. See log:\n" + Logger.LogPath,
                    "SimpleVPN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void DisconnectBtn_Click(object sender, RoutedEventArgs e)
        {
            _ = DisconnectAsync(); // fire & forget to keep WPF handler void
        }

        private async Task DisconnectAsync()
        {
            StatusText.Text = "Status: Disconnecting…";
            try
            {
                // TODO: stop sing-box + release lock
                await Task.Delay(200); // placeholder
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
