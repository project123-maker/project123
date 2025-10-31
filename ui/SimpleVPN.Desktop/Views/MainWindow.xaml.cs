using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using WF = System.Windows.Forms; // WinForms alias

namespace SimpleVPN.Desktop.Views
{
    public partial class MainWindow : Window
    {
        private readonly WF.NotifyIcon _tray;

        public MainWindow()
        {
            InitializeComponent();

            // Create tray icon
            _tray = new WF.NotifyIcon
            {
                Icon = new System.Drawing.Icon("assets/logo.ico"),
                Text = "SimpleVPN",
                Visible = false,
                ContextMenuStrip = new WF.ContextMenuStrip()
            };

            // Tray menu items
            _tray.ContextMenuStrip.Items.Add("Show", null, (_, __) =>
            {
                Show();
                WindowState = WindowState.Normal;
                _tray.Visible = false;
            });

            _tray.ContextMenuStrip.Items.Add("Disconnect", null, (_, __) =>
            {
                // just call async method fire-and-forget
                _ = DisconnectAsync();
            });

            _tray.ContextMenuStrip.Items.Add("Exit (keeps VPN)", null, (_, __) =>
            {
                _tray.Visible = false;
                System.Windows.Application.Current.Shutdown();
            });

            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                if (SingBoxHelper.IsRunning())
                {
                    e.Cancel = true;
                    Hide();
                    _tray.Visible = true;
                }
            }
            catch { /* ignore */ }
        }

        private async void ConnectBtn_Click(object? sender, RoutedEventArgs? e)
        {
            StatusText.Text = "Status: Connecting…";
            try
            {
                await Task.Delay(500); // placeholder
                StatusText.Text = "Status: Connected";
            }
            catch (Exception ex)
            {
                Logger.Log("Connect failed: " + ex);
                StatusText.Text = "Status: Connect failed";
                System.Windows.MessageBox.Show("Connect failed. See log:\n" + Logger.LogPath,
                    "SimpleVPN", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DisconnectBtn_Click(object? sender, RoutedEventArgs? e)
        {
            await DisconnectAsync();
        }

        private async Task DisconnectAsync()
        {
            StatusText.Text = "Status: Disconnecting…";
            try
            {
                await Task.Delay(300); // placeholder
                StatusText.Text = "Status: Disconnected";
            }
            catch (Exception ex)
            {
                Logger.Log("Disconnect failed: " + ex);
                StatusText.Text = "Status: Disconnect failed";
            }
        }
    }

    internal static class SingBoxHelper
    {
        public static bool IsRunning()
        {
            try { return System.Diagnostics.Process.GetProcessesByName("sing-box").Length > 0; }
            catch { return false; }
        }
    }
}
