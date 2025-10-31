using System.Windows;

namespace SimpleVPN.Desktop.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Status: Connecting…";
            try
            {
                // TODO: call your connect flow (redeem + fetch vless + start sing-box)
                await System.Threading.Tasks.Task.Delay(300); // placeholder
                StatusText.Text = "Status: Connected";
            }
            catch (System.Exception ex)
            {
                SimpleVPN.Desktop.Logger.Log("Connect failed: " + ex);
                StatusText.Text = "Status: Connect failed";
                MessageBox.Show("Connect failed. See log:\n" + SimpleVPN.Desktop.Logger.LogPath,
                    "SimpleVPN", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DisconnectBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Status: Disconnecting…";
            try
            {
                // TODO: stop sing-box + release lock
                await System.Threading.Tasks.Task.Delay(200); // placeholder
                StatusText.Text = "Status: Disconnected";
            }
            catch (System.Exception ex)
            {
                SimpleVPN.Desktop.Logger.Log("Disconnect failed: " + ex);
                StatusText.Text = "Status: Disconnect failed";
            }
        }
    }
}
