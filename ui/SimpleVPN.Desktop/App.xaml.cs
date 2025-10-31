using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace SimpleVPN.Desktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Logger.Initialize(
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SimpleVPN", "svpn.log"));

            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                Logger.Log("UNHANDLED: " + ex.ExceptionObject);
                MessageBox.Show("App crashed. See log:\n" + Logger.LogPath, "SimpleVPN",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            };

            this.DispatcherUnhandledException += (s, ex) =>
            {
                Logger.Log("UI UNHANDLED: " + ex.Exception);
                MessageBox.Show("UI error. See log:\n" + Logger.LogPath, "SimpleVPN",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            Logger.Log("==== SimpleVPN starting ====");

            if (!IsElevated())
            {
                Logger.Log("Not elevated → relaunching with runas");
                try
                {
                    var exe = Process.GetCurrentProcess().MainModule!.FileName!;
                    var psi = new ProcessStartInfo(exe) { Verb = "runas", UseShellExecute = true };
                    Process.Start(psi);
                }
                catch (Exception rex)
                {
                    Logger.Log("runas failed: " + rex.Message);
                    MessageBox.Show("Need Administrator permission to create VPN adapter.\n" + Logger.LogPath,
                        "SimpleVPN", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Current.Shutdown();
                return;
            }

            Logger.Log("Elevated OK");
            base.OnStartup(e);
        }

        static bool IsElevated()
        {
            using var id = WindowsIdentity.GetCurrent();
            var p = new WindowsPrincipal(id);
            return p.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
