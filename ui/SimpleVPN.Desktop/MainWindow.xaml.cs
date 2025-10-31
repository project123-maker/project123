using System.Text.Json;
using Microsoft.Extensions.Configuration;

public partial class MainWindow : Window
{
    Firebase _fb = null!;
    LicensingService _lic = null!;
    IConfiguration _cfg;
    string? _activeCode;

    public MainWindow()
    {
        InitializeComponent();

        _cfg = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional:false, reloadOnChange:true)
            .Build();

        Loaded += async (_, __) =>
        {
            var proj = _cfg["firebase:projectId"]!;
            var key  = _cfg["firebase:apiKey"]!;
            _fb = new Firebase(proj, key);
            await _fb.SignInAnonymouslyAsync();
            _lic = new LicensingService(
                _fb,
                _cfg["locks:platform"] ?? "windows",
                int.Parse(_cfg["locks:heartbeatSeconds"] ?? "7"),
                int.Parse(_cfg["locks:staleSeconds"] ?? "45")
            );
            Status("Ready.");
        };
    }

    async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConnectBtn.IsEnabled = false;
            var code = (CodeBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(code)) { Status("Enter code."); return; }

            Status("Redeeming...");
            var (vless, usedCode) = await _lic.RedeemAndFetchAsync(code, CancellationToken.None);
            _activeCode = usedCode;

            // Write vless to your existing pipeline
            // -> your existing Write-Config/Start-VPN wrapper call here
            // Example:
            // await SingBox.StartAsync(vless);

            Status("Connected (starting tunnel)...");
        }
        catch (Exception ex)
        {
            Status("Connect failed: " + ex.Message);
        }
        finally
        {
            ConnectBtn.IsEnabled = true;
        }
    }

    async void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DisconnectBtn.IsEnabled = false;
            // await SingBox.StopAsync();
            if (!string.IsNullOrEmpty(_activeCode))
                await _lic.ReleaseAsync(_activeCode!, CancellationToken.None);
            Status("Disconnected.");
        }
        catch (Exception ex)
        {
            Status("Disconnect failed: " + ex.Message);
        }
        finally
        {
            DisconnectBtn.IsEnabled = true;
        }
    }

    void Status(string s) => Dispatcher.Invoke(() => StatusText.Text = s);
}
